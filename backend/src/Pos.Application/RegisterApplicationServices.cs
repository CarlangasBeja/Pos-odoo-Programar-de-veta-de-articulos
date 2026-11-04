using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Pos.Application.Behaviors;
using Pos.Application.Features.Discounts;
using Pos.Application.Features.Sales;

namespace Pos.Application;

/// <summary>
/// Módulo de registro de la capa Application (parte de la raíz de composición, como en las prácticas de clase).
/// Con los dos escaneos, un caso de uso nuevo (su Command o Query, su Handler y su Validator) no toca este archivo.
/// </summary>
public static class RegisterApplicationServices
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        var assembly = typeof(RegisterApplicationServices).Assembly;

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
            cfg.Lifetime = ServiceLifetime.Scoped;

            // Orden de registro = orden de ejecución antes de next(): el primero es el más externo.
            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>), ServiceLifetime.Scoped);
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>), ServiceLifetime.Scoped);
        });

        services.AddValidatorsFromAssembly(assembly, ServiceLifetime.Scoped);

        // Piezas compartidas por varios Handlers (no son casos de uso).
        services.AddSingleton<DiscountPolicyCatalog>();
        services.AddScoped<SaleComposer>();

        return services;
    }
}
