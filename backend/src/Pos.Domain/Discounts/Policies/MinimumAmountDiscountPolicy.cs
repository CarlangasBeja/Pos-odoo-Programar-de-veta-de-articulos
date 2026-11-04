using Pos.Domain.Common;
using Pos.Domain.Operators;

namespace Pos.Domain.Discounts.Policies;

public sealed record MinimumAmountDiscountOptions(decimal MinimumAmount, decimal Percentage, IReadOnlySet<OperatorRole> AllowedRoles);

/// <summary>Descuento automático sobre toda la venta cuando el total bruto alcanza un monto mínimo.</summary>
public sealed class MinimumAmountDiscountPolicy(MinimumAmountDiscountOptions options)
    : AutomaticDiscountPolicy(options.AllowedRoles)
{
    public const string PolicyCode = "MONTO_MINIMO";

    private readonly DiscountPercentage _percentage = ValidPercentage(options.Percentage);

    public override string Code => PolicyCode;

    public override string Name => "Descuento por monto de compra";

    public override string Description =>
        $"{_percentage} en toda la venta cuando el total bruto es de {options.MinimumAmount:#,0.##} o más.";

    protected override Result<DiscountDecision> Decide(DiscountContext context)
    {
        if (context.GrossTotal < options.MinimumAmount)
        {
            return DiscountErrors.NotApplicable(
                $"La venta ({context.GrossTotal:#,0.##}) no alcanza el monto mínimo de {options.MinimumAmount:#,0.##} para este descuento.");
        }

        var perLine = Enumerable.Repeat(_percentage, context.Lines.Count).ToArray();
        return new DiscountDecision(Code, $"{Name} ({_percentage})", perLine);
    }
}
