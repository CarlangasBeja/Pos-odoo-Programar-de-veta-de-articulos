namespace Pos.Domain.Money;

/// <summary>
/// Moneda con la precisión de redondeo configurada en Odoo (campo <c>res.currency.rounding</c>).
/// Se usa <see cref="decimal"/> para evitar los errores de representación de los números en coma flotante.
/// </summary>
public sealed record Currency
{
    public Currency(string code, decimal rounding)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("El código de la moneda es obligatorio.", nameof(code));
        }

        if (rounding <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rounding), "La precisión de redondeo debe ser positiva.");
        }

        Code = code.Trim().ToUpperInvariant();
        Rounding = rounding;
        DecimalPlaces = CountDecimals(rounding);
    }

    public string Code { get; }

    /// <summary>Unidad mínima de redondeo (0.01, 1, 0.05...).</summary>
    public decimal Rounding { get; }

    public int DecimalPlaces { get; }

    /// <summary>Redondeo "mitad hacia arriba" (HALF-UP), equivalente a <c>float_round</c> de Odoo.</summary>
    public decimal Round(decimal amount)
    {
        var steps = Math.Round(amount / Rounding, 0, MidpointRounding.AwayFromZero);
        return Math.Round(steps * Rounding, DecimalPlaces, MidpointRounding.AwayFromZero);
    }

    public bool IsZero(decimal amount) => Round(amount) == 0m;

    private static int CountDecimals(decimal value)
    {
        var normalized = value / 1.000000000000000000000000000000000m;
        return BitConverter.GetBytes(decimal.GetBits(normalized)[3])[2];
    }
}
