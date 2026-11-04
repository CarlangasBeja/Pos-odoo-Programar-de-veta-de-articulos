using Pos.Domain.Money;

namespace Pos.Domain.Taxes;

/// <summary>Línea a calcular: precio unitario de Odoo, cantidad, descuento porcentual e impuestos.</summary>
public sealed record TaxableLine(decimal UnitPrice, decimal Quantity, decimal DiscountPercent, IReadOnlyList<Tax> Taxes);

/// <summary>Importes de una línea ya redondeados a la moneda.</summary>
public sealed record LineAmounts(decimal Subtotal, decimal TaxAmount, decimal Total);

/// <summary>Total por impuesto, para mostrar el desglose.</summary>
public sealed record TaxTotal(int TaxId, string Name, decimal Amount);

public sealed record TaxCalculation(
    IReadOnlyList<LineAmounts> Lines,
    decimal Untaxed,
    decimal TaxAmount,
    decimal Total,
    IReadOnlyList<TaxTotal> Taxes);

/// <summary>
/// Réplica del cálculo de totales de Odoo 20 (<c>account.tax._get_tax_details</c>,
/// <c>_round_base_lines_tax_details</c> y <c>_get_tax_totals_summary</c>) para los casos admitidos:
/// impuestos porcentuales, todos excluidos del precio o uno solo incluido por producto.
/// Odoo recalcula el total al registrar la venta y exige que el pago coincida, por eso el cálculo debe ser idéntico.
/// La equivalencia se comprueba con vectores generados por el propio motor de Odoo (ver TaxCalculatorOdooParityTests).
/// </summary>
public static class TaxCalculator
{
    public static TaxCalculation Calculate(IReadOnlyList<TaxableLine> lines, Currency currency, TaxRoundingMode mode)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(currency);

        var rawLines = lines.Select(line => ComputeRawLine(line, currency, mode)).ToArray();

        var taxTotals = rawLines
            .SelectMany(line => line.Taxes)
            .GroupBy(tax => tax.Tax.Id)
            .Select(group => new TaxTotal(group.Key, group.First().Tax.Name, currency.Round(group.Sum(t => t.RawAmount))))
            .ToArray();

        var taxAmount = taxTotals.Sum(t => t.Amount);

        // Odoo decide el modo de ajuste del documento: si alguna línea tiene un impuesto excluido distinto de cero,
        // se redondea la base; si todos los impuestos con importe están incluidos en el precio, se redondea el total.
        var hasExcludedTax = rawLines.Any(line =>
            line.Taxes.Any(t => !t.Tax.PriceIncluded && !currency.IsZero(t.RawAmount)));

        decimal untaxed;
        decimal total;
        if (hasExcludedTax)
        {
            untaxed = currency.Round(rawLines.Sum(l => l.RawTotalExcluded));
            total = untaxed + taxAmount;
        }
        else
        {
            total = currency.Round(rawLines.Sum(l => l.RawTotalIncluded));
            untaxed = total - taxAmount;
        }

        var lineAmounts = rawLines
            .Select(l =>
            {
                var subtotal = currency.Round(l.RawTotalExcluded);
                var lineTotal = currency.Round(l.RawTotalIncluded);
                return new LineAmounts(subtotal, lineTotal - subtotal, lineTotal);
            })
            .ToArray();

        return new TaxCalculation(lineAmounts, untaxed, taxAmount, total, taxTotals);
    }

    /// <summary>Indica si la combinación de impuestos de un producto puede calcularse con exactitud.</summary>
    public static bool SupportsTaxes(IReadOnlyList<Tax> taxes)
    {
        ArgumentNullException.ThrowIfNull(taxes);
        if (taxes.Any(t => !t.IsSupported))
        {
            return false;
        }

        var included = taxes.Count(t => t.PriceIncluded);
        return included == 0 || (included == 1 && taxes.Count == 1);
    }

    private static RawLine ComputeRawLine(TaxableLine line, Currency currency, TaxRoundingMode mode)
    {
        if (!SupportsTaxes(line.Taxes))
        {
            throw new InvalidOperationException("La combinación de impuestos de la línea no está soportada.");
        }

        var priceAfterDiscount = line.UnitPrice * (1m - (line.DiscountPercent / 100m));
        var rawBase = line.Quantity * priceAfterDiscount;
        if (mode == TaxRoundingMode.PerLine)
        {
            rawBase = currency.Round(rawBase);
        }

        var includedTax = line.Taxes.SingleOrDefault(t => t.PriceIncluded);
        if (includedTax is not null)
        {
            var taxAmount = RoundIfPerLine(rawBase * includedTax.Rate / (1m + includedTax.Rate), currency, mode);
            var excluded = RoundIfPerLine(rawBase - taxAmount, currency, mode);
            return new RawLine(excluded, rawBase, [new RawTax(includedTax, taxAmount)]);
        }

        var taxes = line.Taxes
            .Select(tax => new RawTax(tax, RoundIfPerLine(rawBase * tax.Rate, currency, mode)))
            .ToArray();
        var totalIncluded = RoundIfPerLine(rawBase + taxes.Sum(t => t.RawAmount), currency, mode);
        return new RawLine(rawBase, totalIncluded, taxes);
    }

    private static decimal RoundIfPerLine(decimal amount, Currency currency, TaxRoundingMode mode) =>
        mode == TaxRoundingMode.PerLine ? currency.Round(amount) : amount;

    private sealed record RawTax(Tax Tax, decimal RawAmount);

    private sealed record RawLine(decimal RawTotalExcluded, decimal RawTotalIncluded, IReadOnlyList<RawTax> Taxes);
}
