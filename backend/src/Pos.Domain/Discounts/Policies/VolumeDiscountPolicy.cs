using Pos.Domain.Common;
using Pos.Domain.Operators;

namespace Pos.Domain.Discounts.Policies;

public sealed record VolumeDiscountOptions(decimal MinQuantity, decimal Percentage, IReadOnlySet<OperatorRole> AllowedRoles);

/// <summary>Descuento automático en cada línea cuya cantidad alcanza el mínimo configurado.</summary>
public sealed class VolumeDiscountPolicy(VolumeDiscountOptions options) : AutomaticDiscountPolicy(options.AllowedRoles)
{
    public const string PolicyCode = "VOLUMEN";

    private readonly DiscountPercentage _percentage = ValidPercentage(options.Percentage);

    public override string Code => PolicyCode;

    public override string Name => "Descuento por volumen";

    public override string Description =>
        $"{_percentage} en cada producto del que se lleven {options.MinQuantity:0.##} unidades o más.";

    protected override Result<DiscountDecision> Decide(DiscountContext context)
    {
        var perLine = context.Lines
            .Select(line => line.Quantity >= options.MinQuantity ? _percentage : DiscountPercentage.Zero)
            .ToArray();

        if (perLine.All(p => p.IsZero))
        {
            return DiscountErrors.NotApplicable(
                $"Ningún producto alcanza las {options.MinQuantity:0.##} unidades necesarias para el descuento por volumen.");
        }

        return new DiscountDecision(Code, $"{Name} ({_percentage})", perLine);
    }
}
