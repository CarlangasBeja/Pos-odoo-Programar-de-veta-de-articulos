namespace Pos.Domain.Taxes;

/// <summary>Tipo de cálculo del impuesto en Odoo (<c>account.tax.amount_type</c>).</summary>
public enum TaxAmountType
{
    Percent,
    Fixed,
    Division,
    Group,
    Other,
}

/// <summary>Impuesto de venta tal como está configurado en Odoo.</summary>
public sealed record Tax(
    int Id,
    string Name,
    TaxAmountType AmountType,
    decimal Amount,
    bool PriceIncluded,
    bool IncludeBaseAmount)
{
    /// <summary>Tasa como fracción: 19 % → 0.19.</summary>
    public decimal Rate => Amount / 100m;

    /// <summary>
    /// Solo se admiten impuestos porcentuales que no alteran la base de impuestos posteriores.
    /// Con otros tipos no podemos garantizar el mismo total que calcula Odoo, así que el producto no se vende.
    /// </summary>
    public bool IsSupported => AmountType == TaxAmountType.Percent && !IncludeBaseAmount;
}
