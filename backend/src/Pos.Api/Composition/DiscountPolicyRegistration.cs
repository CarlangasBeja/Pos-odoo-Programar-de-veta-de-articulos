using Pos.Domain.Discounts;
using Pos.Domain.Discounts.Policies;
using Pos.Domain.Operators;

namespace Pos.Api.Composition;

/// <summary>
/// Activa las políticas de descuento según la configuración (sección "Discounts").
/// Agregar una política nueva = una clase que implemente <see cref="IDiscountPolicy"/> + una línea aquí + su configuración.
/// Ningún Handler ni controlador cambia (RF-07).
/// </summary>
public static class DiscountPolicyRegistration
{
    public static IServiceCollection AddDiscountPolicies(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection("Discounts");

        var manual = section.GetSection("Manual");
        if (manual.GetValue("Enabled", true))
        {
            var limits = manual.GetSection("MaxPercentByRole").Get<Dictionary<OperatorRole, decimal>>()
                ?? new Dictionary<OperatorRole, decimal> { [OperatorRole.Cashier] = 10m, [OperatorRole.Supervisor] = 30m };
            services.AddSingleton<IDiscountPolicy>(new ManualDiscountPolicy(new ManualDiscountOptions(limits)));
        }

        var volume = section.GetSection("Volume");
        if (volume.GetValue("Enabled", true))
        {
            services.AddSingleton<IDiscountPolicy>(new VolumeDiscountPolicy(new VolumeDiscountOptions(
                volume.GetValue("MinQuantity", 10m),
                volume.GetValue("Percentage", 5m),
                Roles(volume))));
        }

        var minimumAmount = section.GetSection("MinimumAmount");
        if (minimumAmount.GetValue("Enabled", true))
        {
            services.AddSingleton<IDiscountPolicy>(new MinimumAmountDiscountPolicy(new MinimumAmountDiscountOptions(
                minimumAmount.GetValue("MinimumAmount", 100m),
                minimumAmount.GetValue("Percentage", 3m),
                Roles(minimumAmount))));
        }

        return services;
    }

    private static HashSet<OperatorRole> Roles(IConfigurationSection section) =>
        section.GetSection("AllowedRoles").Get<OperatorRole[]>() is { Length: > 0 } roles
            ? [.. roles]
            : [OperatorRole.Cashier, OperatorRole.Supervisor];
}
