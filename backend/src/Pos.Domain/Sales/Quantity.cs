using Pos.Domain.Common;

namespace Pos.Domain.Sales;

/// <summary>Cantidad vendida: mayor que cero (RN-02), acotada y con tres decimales como máximo.</summary>
public readonly record struct Quantity
{
    public const decimal Max = 10_000m;
    public const int MaxDecimals = 3;

    private Quantity(decimal value) => Value = value;

    public decimal Value { get; }

    public static Result<Quantity> Create(decimal value, string? target = null)
    {
        if (value <= 0m)
        {
            return SaleErrors.QuantityNotPositive(value, target);
        }

        if (value > Max)
        {
            return SaleErrors.QuantityTooLarge(value, Max, target);
        }

        if (decimal.Round(value, MaxDecimals) != value)
        {
            return SaleErrors.QuantityTooPrecise(value, MaxDecimals, target);
        }

        return new Quantity(value);
    }

    public Result<Quantity> Add(Quantity other) => Create(Value + other.Value);

    public override string ToString() => Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
}
