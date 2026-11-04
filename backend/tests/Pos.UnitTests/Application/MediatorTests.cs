using System.Collections.Concurrent;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pos.Application;
using Pos.Application.Contracts.Gateways;
using Pos.Application.Contracts.Persistence;
using Pos.Application.Contracts.Security;
using Pos.Application.Features.Auth.Commands.Login;
using Pos.Application.Features.Sales;
using Pos.Application.Features.Sessions.Queries.GetActiveSession;
using Pos.Domain.Common;
using Pos.Domain.Discounts;
using Pos.Domain.Operators;
using Pos.Domain.Sales;
using Pos.Infrastructure.Security;
using Pos.UnitTests.Fakes;

namespace Pos.UnitTests.Application;

/// <summary>
/// El Mediator ensamblado con el contenedor, como lo usa la API: aquí se prueba lo que el Handler solo no puede mostrar
/// (que la validación ocurre antes del Handler, el orden de los behaviors y la falta de un Handler).
/// El logging lo registra la prueba, que hace de host; Application no lo registra (decisión D5 del taller).
/// </summary>
[TestFixture]
public sealed class MediatorTests
{
    private static readonly Pbkdf2PasswordHasher Hasher = new();

    /// <summary>Solicitud sin Handler escrito: el escaneo no puede registrar algo que no existe.</summary>
    public sealed record OrphanQuery : IRequest<Result<int>>;

    private sealed class SingleOperatorDirectory : IOperatorDirectory
    {
        private readonly OperatorAccount _account = new("cajero", "Ana", OperatorRole.Cashier, Hasher.Hash("Cajero2026!"), true);

        public Task<OperatorAccount?> FindByUsernameAsync(string username, CancellationToken cancellationToken) =>
            Task.FromResult(username == _account.Username ? _account : null);
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider, ILogger
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Enqueue(formatter(state, exception));

        public void Dispose()
        {
        }
    }

    private static ServiceProvider BuildServiceProvider(SalesFixture fixture, ILoggerProvider? logs = null, bool addLogging = true)
    {
        var services = new ServiceCollection();
        if (addLogging)
        {
            services.AddLogging(builder =>
            {
                if (logs is not null)
                {
                    builder.AddProvider(logs);
                }
            });
        }

        services.AddApplicationServices();

        services.AddSingleton<IProductCatalog>(fixture.Catalog);
        services.AddSingleton<IPosSessionGateway>(fixture.Sessions);
        services.AddSingleton<IPosOrderGateway>(fixture.Orders);
        services.AddSingleton<IIdempotencyStore>(fixture.Attempts);
        services.AddSingleton<TimeProvider>(fixture.Time);
        services.AddSingleton<IOperatorDirectory, SingleOperatorDirectory>();
        services.AddSingleton<IPasswordHasher>(Hasher);
        foreach (var policy in SalesFixture.DefaultPolicies())
        {
            services.AddSingleton<IDiscountPolicy>(policy);
        }

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    [Test]
    public async Task Send_UnCommandValidoLlegaAlHandlerYRegistraLaVenta()
    {
        var fixture = new SalesFixture();
        await using var provider = BuildServiceProvider(fixture);
        await using var scope = provider.CreateAsyncScope();

        var result = await scope.ServiceProvider.GetRequiredService<ISender>().Send(SalesFixture.TwoProductsCash());

        Assert.That(result.Value.Status, Is.EqualTo(SaleRegistrationStatus.Registered));
        Assert.That(fixture.Orders.SubmitCount, Is.EqualTo(1));
    }

    [Test]
    public async Task Send_UnCommandMalFormadoSeRechazaAntesDelHandler()
    {
        var fixture = new SalesFixture();
        await using var provider = BuildServiceProvider(fixture);
        await using var scope = provider.CreateAsyncScope();
        var command = SalesFixture.WithLines(new SaleLineRequest(0, 1m)) with { IdempotencyKey = Guid.Empty };

        var result = await scope.ServiceProvider.GetRequiredService<ISender>().Send(command);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Errors.Select(e => e.Kind), Is.All.EqualTo(ErrorKind.Validation));
            Assert.That(result.Errors.Select(e => (e.Code, e.Target)), Is.EquivalentTo(new[]
            {
                (IdempotencyErrors.KeyRequiredCode, (string?)"Idempotency-Key"),
                ("SALE.INVALID_PRODUCT", "lines[0].productId"),
            }));
            Assert.That(fixture.Attempts.Attempts, Is.Empty, "El Handler no se ejecutó: no reservó la clave.");
            Assert.That(fixture.Catalog.Calls, Is.Zero, "No se consultó a Odoo.");
            Assert.That(fixture.Orders.SubmitCount, Is.Zero);
        });
    }

    [Test]
    public async Task Send_LasReglasDelNegocioLasSigueAplicandoElDominio()
    {
        var fixture = new SalesFixture();
        await using var provider = BuildServiceProvider(fixture);
        await using var scope = provider.CreateAsyncScope();

        var result = await scope.ServiceProvider.GetRequiredService<ISender>().Send(SalesFixture.WithLines());

        Assert.That(result.FirstError.Code, Is.EqualTo(SaleErrors.EmptyCode));
        Assert.That(fixture.Orders.SubmitCount, Is.Zero);
    }

    [Test]
    public async Task Send_UnaQuerySinValidatorPasaPorElPipeline()
    {
        var fixture = new SalesFixture();
        await using var provider = BuildServiceProvider(fixture);
        await using var scope = provider.CreateAsyncScope();

        var result = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new GetActiveSessionQuery());

        Assert.That(result.Value.AcceptsSales, Is.True);
    }

    [Test]
    public void Send_UnaSolicitudSinHandlerFallaConInvalidOperationException()
    {
        var fixture = new SalesFixture();
        using var provider = BuildServiceProvider(fixture);
        using var scope = provider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        Assert.ThrowsAsync<InvalidOperationException>(() => sender.Send(new OrphanQuery()));
    }

    [Test]
    public async Task Logging_RegistraTambienLasSolicitudesQueLaValidacionRechaza()
    {
        var fixture = new SalesFixture();
        var logs = new CapturingLoggerProvider();
        await using var provider = BuildServiceProvider(fixture, logs);
        await using var scope = provider.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        await sender.Send(new LoginCommand("", ""));
        await sender.Send(new LoginCommand("cajero", "Cajero2026!"));

        var behaviorLogs = logs.Messages.Where(m => m.StartsWith(nameof(LoginCommand), StringComparison.Ordinal)).ToArray();
        Assert.That(behaviorLogs, Has.Length.EqualTo(2));
        Assert.That(behaviorLogs[0], Does.Contain("rechazada").And.Contain("AUTH.USERNAME_REQUIRED"));
        Assert.That(behaviorLogs[1], Does.Contain("atendida"));
        Assert.That(logs.Messages, Has.None.Contains("Cajero2026!"), "La contraseña nunca se registra.");
    }

    [Test]
    public void SinLoggingRegistradoElMediatorNoSePuedeUsar()
    {
        // MediatR 14 exige ILoggerFactory. En la API lo registra ASP.NET Core; aquí se comprueba que no se omitió en el host.
        var fixture = new SalesFixture();

        var exception = Assert.Catch(() =>
        {
            using var provider = BuildServiceProvider(fixture, addLogging: false);
            using var scope = provider.CreateScope();
            scope.ServiceProvider.GetRequiredService<ISender>().Send(new GetActiveSessionQuery()).GetAwaiter().GetResult();
        });
        Assert.That(exception!.ToString(), Does.Contain("ILogger"));
    }
}
