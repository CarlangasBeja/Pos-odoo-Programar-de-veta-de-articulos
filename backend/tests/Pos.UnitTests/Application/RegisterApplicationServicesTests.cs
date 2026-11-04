using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Pos.Application;
using Pos.Application.Behaviors;
using Pos.Application.Features.Catalog.Queries.GetProductCategories;
using Pos.Application.Features.Discounts.Queries.GetDiscountPolicies;
using Pos.Application.Features.Sales.Commands.RegisterSale;
using Pos.Application.Features.Sessions.Queries.GetActiveSession;
using Pos.Domain.Common;

namespace Pos.UnitTests.Application;

/// <summary>
/// Pruebas de completitud del registro. El escaneo registra lo que existe, pero no puede detectar lo que falta:
/// una solicitud sin Handler, una respuesta que la validación no sabe construir o un Validator olvidado.
/// Se recorre el ensamblado de Application (no el de MediatR: allí no hay ninguna solicitud del POS).
/// </summary>
[TestFixture]
public sealed class RegisterApplicationServicesTests
{
    private static readonly IReadOnlyList<Type> RequestTypes = typeof(RegisterSaleCommand).Assembly.GetTypes()
        .Where(t => t is { IsAbstract: false, IsInterface: false } && ResponseTypeOf(t) is not null)
        .ToArray();

    private static readonly IServiceCollection Services = new ServiceCollection().AddApplicationServices();

    private static Type? ResponseTypeOf(Type requestType) => requestType.GetInterfaces()
        .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>))
        ?.GetGenericArguments()[0];

    [Test]
    public void ElEnsambladoTieneLosCasosDeUsoEsperados() =>
        Assert.That(RequestTypes, Has.Count.EqualTo(10));

    [Test]
    public void CadaSolicitudTieneSuHandlerRegistradoConCicloDeVidaScoped()
    {
        var missing = RequestTypes
            .Where(request => !Services.Any(d =>
                d.ServiceType == typeof(IRequestHandler<,>).MakeGenericType(request, ResponseTypeOf(request)!)
                && d.Lifetime == ServiceLifetime.Scoped))
            .Select(t => t.Name)
            .ToArray();

        Assert.That(missing, Is.Empty, () => $"Solicitudes sin Handler registrado: {string.Join(", ", missing)}");
    }

    [Test]
    public void CadaRespuestaEsUnResultQueLaValidacionPuedeConstruir()
    {
        // ValidationBehavior devuelve TResponse.FromErrors(...): toda respuesta debe ser Result o Result<T>.
        var notResults = RequestTypes
            .Where(request =>
            {
                var response = ResponseTypeOf(request)!;
                return !typeof(IFailureFactory<>).MakeGenericType(response).IsAssignableFrom(response);
            })
            .Select(t => t.Name)
            .ToArray();

        Assert.That(notResults, Is.Empty, () => $"Respuestas que no son Result: {string.Join(", ", notResults)}");
    }

    [Test]
    public void SoloLasSolicitudesSinDatosDelClienteNoTienenValidator()
    {
        // Atenúa la "lista vacía ambigua" del taller: si alguien agrega un Command sin Validator, esta prueba falla
        // hasta que se escriba el Validator o se justifique agregándolo a esta lista.
        var withoutValidator = RequestTypes
            .Where(request => !Services.Any(d => d.ServiceType == typeof(IValidator<>).MakeGenericType(request)))
            .ToArray();

        Assert.That(withoutValidator, Is.EquivalentTo(new[]
        {
            typeof(GetProductCategoriesQuery),
            typeof(GetActiveSessionQuery),
            typeof(GetDiscountPoliciesQuery),
        }));
    }

    [Test]
    public void LosBehaviorsSeRegistranEnOrdenLoggingYLuegoValidacion()
    {
        // Solo los behaviors propios: la librería puede registrar los suyos (por ejemplo, para excepciones).
        var behaviors = Services
            .Where(d => d.ServiceType == typeof(IPipelineBehavior<,>))
            .Select(d => d.ImplementationType)
            .Where(t => t?.Assembly == typeof(RegisterApplicationServices).Assembly)
            .ToArray();

        Assert.That(behaviors, Is.EqualTo(new[] { typeof(LoggingBehavior<,>), typeof(ValidationBehavior<,>) }));
    }
}
