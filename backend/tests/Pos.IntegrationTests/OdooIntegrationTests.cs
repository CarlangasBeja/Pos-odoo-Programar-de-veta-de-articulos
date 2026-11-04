using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pos.Application;
using Pos.Application.Common;
using Pos.Application.Contracts.Gateways;
using Pos.Application.Features.Catalog.Queries.SearchProducts;
using Pos.Application.Features.History;
using Pos.Application.Features.Sales;
using Pos.Application.Features.Sales.Commands.RegisterSale;
using Pos.Application.Features.Sales.Queries.PreviewSale;
using Pos.Domain.Discounts;
using Pos.Domain.Discounts.Policies;
using Pos.Domain.Operators;
using Pos.Domain.Payments;
using Pos.Infrastructure;
using Pos.Infrastructure.Odoo;

namespace Pos.IntegrationTests;

/// <summary>
/// P09: registra y verifica en Odoo 20 una venta de dos productos con pago en efectivo, usando exactamente el mismo
/// código que la API (sin simuladores): ISender → behaviors (logging, validación) → Handler → adaptadores JSON-2.
/// </summary>
[TestFixture]
[Category("OdooReal")]
[NonParallelizable]
public sealed class OdooIntegrationTests
{
    /// <summary>Mismo UserSecretsId que Pos.Api: el script de preparación deja ahí la configuración.</summary>
    private const string ApiUserSecretsId = "pos-odoo20-api-7f3c2a9e";

    private static readonly OperatorIdentity Tester = new("pruebas", "Prueba de integración", OperatorRole.Supervisor);

    private ServiceProvider _services = null!;
    private string _attemptsFile = string.Empty;

    [OneTimeSetUp]
    public void SetUp()
    {
        _attemptsFile = Path.Combine(Path.GetTempPath(), $"pos-integration-{Guid.NewGuid():N}.json");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Odoo:BaseUrl"] = "http://localhost:8069",
                ["Odoo:Database"] = "pos",
                ["Idempotency:FilePath"] = _attemptsFile,
            })
            .AddUserSecrets(ApiUserSecretsId)
            .AddEnvironmentVariables()
            .Build();

        if (string.IsNullOrWhiteSpace(configuration["Odoo:ApiKey"]) || configuration.GetValue<int>("Odoo:PosConfigId") <= 0)
        {
            Assert.Ignore("Odoo no está configurado. Ejecute odoo/setup.ps1 (o defina Odoo__ApiKey, Odoo__PosConfigId, "
                + "Odoo__BaseUrl y Odoo__Database) para ejecutar las pruebas de integración real.");
        }

        // Igual que la API: módulo de Application + módulo de Infrastructure. La prueba hace de host (registra el logging).
        _services = new ServiceCollection()
            .AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning))
            .AddApplicationServices()
            .AddInfrastructure(configuration)
            .AddSingleton<IDiscountPolicy>(new ManualDiscountPolicy(new ManualDiscountOptions(
                new Dictionary<OperatorRole, decimal> { [OperatorRole.Cashier] = 10m, [OperatorRole.Supervisor] = 30m })))
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        _services?.Dispose();
        if (File.Exists(_attemptsFile))
        {
            File.Delete(_attemptsFile);
        }
    }

    /// <summary>Cada envío usa su propio ámbito, como una petición HTTP de la API.</summary>
    private async Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request)
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request, CancellationToken.None);
    }

    private async Task<(int SessionId, int CashMethodId, int[] ProductIds)> PrepareAsync()
    {
        var session = await _services.GetRequiredService<IPosSessionGateway>().GetActiveSessionAsync(CancellationToken.None);
        Assert.That(session.IsSuccess, Is.True, () => $"No hay sesión POS: {string.Join(" ", session.Errors.Select(e => e.Message))}");
        Assert.That(session.Value.Session.AcceptsSales, Is.True, "La sesión POS debe estar abierta y con configuración compatible.");

        var cash = session.Value.Session.PaymentMethods.FirstOrDefault(m => m.Type == PaymentMethodType.Cash && m.IsSupported);
        Assert.That(cash, Is.Not.Null, "El punto de venta necesita un método de pago en efectivo.");

        var products = await SendAsync(new SearchProductsQuery(null, null, PageSize: 50));
        Assert.That(products.IsSuccess, Is.True);
        var sellable = products.Value.Items.Where(p => p.Sellable).Select(p => p.Id).Take(2).ToArray();
        Assert.That(sellable, Has.Length.EqualTo(2), "Se necesitan al menos dos productos vendibles en el POS.");

        return (session.Value.Session.Id, cash!.Id, sellable);
    }

    [Test]
    public async Task Odoo_ExisteUnaSesionAbiertaConEfectivo() => await PrepareAsync();

    [Test]
    public async Task P09_RegistraEnOdooUnaVentaDeDosProductosEnEfectivoYLaVerifica()
    {
        var (sessionId, cashId, productIds) = await PrepareAsync();
        SaleLineRequest[] lines = [new SaleLineRequest(productIds[0], 2m), new SaleLineRequest(productIds[1], 1m)];

        var preview = await SendAsync(new PreviewSaleQuery(sessionId, lines, null, Tester));
        Assert.That(preview.IsSuccess, Is.True);

        var tendered = Math.Ceiling(preview.Value.Total) + 10m; // efectivo con cambio
        var command = new RegisterSaleCommand(Guid.NewGuid(), sessionId, lines, null, new PaymentRequest(cashId, tendered), Tester);

        var result = await SendAsync(command);

        Assert.That(result.IsSuccess, Is.True, () => string.Join(" | ", result.Errors.Select(e => e.Message)));
        var sale = result.Value;
        Assert.That(sale.Status, Is.EqualTo(SaleRegistrationStatus.Registered), sale.Message);

        // Verificación independiente en Odoo: la orden existe, está pagada y el total coincide con el calculado.
        var inOdoo = await _services.GetRequiredService<IPosOrderGateway>().FindBySaleKeyAsync(command.IdempotencyKey, CancellationToken.None);
        var order = inOdoo.Value!;
        Assert.Multiple(() =>
        {
            Assert.That(order.Id, Is.EqualTo(sale.OrderId));
            Assert.That(order.State, Is.EqualTo(OrderState.Paid));
            Assert.That(order.AmountTotal, Is.EqualTo(preview.Value.Total), "Odoo calculó el mismo total que el backend");
            Assert.That(order.AmountPaid, Is.EqualTo(preview.Value.Total));
            Assert.That(order.AmountReturn, Is.EqualTo(tendered - preview.Value.Total));
        });

        var detail = await _services.GetRequiredService<IPosOrderGateway>().GetDetailAsync(order.Id, CancellationToken.None);
        Assert.That(detail.Value!.Lines, Has.Count.EqualTo(2));
        Assert.That(detail.Value.Payments.Any(p => p.IsChange), Is.True, "Odoo registró la devolución del cambio");
        TestContext.Out.WriteLine($"Venta registrada en Odoo: {order.Name} (id {order.Id}), total {order.AmountTotal}, estado {order.State}");
    }

    [Test]
    public async Task P09_RepetirLaSolicitudNoCreaUnaSegundaOrdenEnOdoo()
    {
        var (sessionId, cashId, productIds) = await PrepareAsync();
        var command = new RegisterSaleCommand(
            Guid.NewGuid(),
            sessionId,
            [new SaleLineRequest(productIds[0], 1m)],
            null,
            new PaymentRequest(cashId, 1_000_000m),
            Tester);

        var first = await SendAsync(command);
        var second = await SendAsync(command);

        Assert.That(first.Value.Status, Is.EqualTo(SaleRegistrationStatus.Registered));
        Assert.That(second.Value.Replayed, Is.True);
        Assert.That(second.Value.OrderId, Is.EqualTo(first.Value.OrderId));

        // Aun reenviando directamente a Odoo con el mismo uuid, Odoo conserva una sola orden.
        var odoo = _services.GetRequiredService<OdooJson2Client>();
        var count = await odoo.CallAsync(
            "pos.order",
            "search_count",
            new Dictionary<string, object?> { ["domain"] = new object[] { new object[] { "uuid", "=", command.IdempotencyKey.ToString() } } },
            CancellationToken.None);
        Assert.That(((OdooResponse.Success)count).Result.GetInt32(), Is.EqualTo(1));
    }

    [Test]
    public async Task P09_UnDescuentoAutorizadoSeRegistraConElTotalQueCalculaOdoo()
    {
        var (sessionId, cashId, productIds) = await PrepareAsync();
        var command = new RegisterSaleCommand(
            Guid.NewGuid(),
            sessionId,
            [new SaleLineRequest(productIds[0], 3m), new SaleLineRequest(productIds[1], 2m)],
            new DiscountRequest("MANUAL", 12.5m),
            new PaymentRequest(cashId, 1_000_000m),
            Tester);

        var result = await SendAsync(command);

        Assert.That(result.IsSuccess, Is.True, () => string.Join(" | ", result.Errors.Select(e => e.Message)));
        Assert.That(result.Value.Status, Is.EqualTo(SaleRegistrationStatus.Registered), result.Value.Message);
    }

    [Test]
    public async Task Odoo_ElHistorialEncuentraLasVentasDelPuntoDeVenta()
    {
        var history = await _services.GetRequiredService<IPosOrderGateway>().SearchAsync(
            new OrderSearchCriteria(null, OrderStateFilter.All, null, null, PageRequest.Create(1, 5, 5).Value),
            CancellationToken.None);

        Assert.That(history.IsSuccess, Is.True);
        Assert.That(history.Value.Items.Select(o => o.Date), Is.Ordered.Descending);
    }
}
