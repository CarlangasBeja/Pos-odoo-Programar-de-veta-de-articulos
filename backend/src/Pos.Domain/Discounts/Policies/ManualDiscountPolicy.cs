using Pos.Domain.Common;
using Pos.Domain.Operators;

namespace Pos.Domain.Discounts.Policies;

/// <param name="MaxPercentByRole">Porcentaje máximo por rol. Un rol ausente no puede aplicar descuento manual.</param>
public sealed record ManualDiscountOptions(IReadOnlyDictionary<OperatorRole, decimal> MaxPercentByRole);

/// <summary>Descuento porcentual indicado por el operador, aplicado a toda la venta y limitado por rol (RN-04, RF-08).</summary>
public sealed class ManualDiscountPolicy(ManualDiscountOptions options) : IDiscountPolicy
{
    public const string PolicyCode = "MANUAL";

    public string Code => PolicyCode;

    public string Name => "Descuento manual";

    public string Description => "Porcentaje sobre toda la venta, con un máximo según el rol del operador.";

    public DiscountPolicyKind Kind => DiscountPolicyKind.Manual;

    public bool IsAvailableFor(OperatorRole role) => MaxPercentageFor(role) is > 0m;

    public decimal? MaxPercentageFor(OperatorRole role) =>
        options.MaxPercentByRole.TryGetValue(role, out var max) ? max : null;

    public Result<DiscountDecision> Evaluate(DiscountContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.RequestedPercentage is not { } requested || requested <= 0m)
        {
            return DiscountErrors.PercentageRequired();
        }

        var percentage = DiscountPercentage.Create(requested);
        if (percentage.IsFailure)
        {
            return Result<DiscountDecision>.Failure(percentage.Errors);
        }

        if (MaxPercentageFor(context.Role) is not { } max || max <= 0m)
        {
            return DiscountErrors.RoleNotAllowed(Name, context.Role);
        }

        if (requested > max)
        {
            return DiscountErrors.ExceedsRoleLimit(requested, max, context.Role);
        }

        var perLine = Enumerable.Repeat(percentage.Value, context.Lines.Count).ToArray();
        return new DiscountDecision(Code, $"{Name} del {percentage.Value}", perLine);
    }
}
