using Pos.Domain.Common;

namespace Pos.Domain.Discounts;

/// <summary>Porcentaje de descuento entre 0 y 100 con dos decimales como máximo.</summary>
public readonly record struct DiscountPercentage
{
    public const decimal Max = 100m;
    public const int MaxDecimals = 2;

    public static readonly DiscountPercentage Zero = new(0m);

    private DiscountPercentage(decimal value) => Value = value;

    public decimal Value { get; }

    public bool IsZero => Value == 0m;

    public static Result<DiscountPercentage> Create(decimal value)
    {
        if (value < 0m || value > Max)
        {
            return DiscountErrors.PercentageOutOfRange(value);
        }

        if (decimal.Round(value, MaxDecimals) != value)
        {
            return DiscountErrors.PercentageTooPrecise(value);
        }

        return new DiscountPercentage(value);
    }

    public override string ToString() => $"{Value:0.##} %";
}
