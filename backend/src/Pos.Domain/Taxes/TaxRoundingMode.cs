namespace Pos.Domain.Taxes;

/// <summary>Método de redondeo de impuestos de la compañía (<c>res.company.tax_calculation_rounding_method</c>).</summary>
public enum TaxRoundingMode
{
    /// <summary><c>round_per_line</c>: se redondea cada línea y luego se suma.</summary>
    PerLine,

    /// <summary><c>round_globally</c>: se suman los valores sin redondear y se redondea el total (predeterminado en Odoo 20).</summary>
    Global,
}
