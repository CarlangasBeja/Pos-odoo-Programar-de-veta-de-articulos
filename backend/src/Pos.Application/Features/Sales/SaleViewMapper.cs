using Pos.Domain.Sales;

namespace Pos.Application.Features.Sales;

internal static class SaleViewMapper
{
    public static SalePreview ToPreview(Sale sale)
    {
        var totals = sale.Totals;
        var discount = sale.Discount.IsNone
            ? null
            : new AppliedDiscountView(sale.Discount.PolicyCode, sale.Discount.Description, totals.DiscountAmount);

        return new SalePreview(
            sale.Session.Id,
            sale.Session.Currency.Code,
            sale.Lines.Select(ToLineView).ToArray(),
            totals.GrossBeforeDiscount,
            discount,
            totals.Untaxed,
            totals.Tax,
            totals.Total,
            totals.Taxes.Select(t => new TaxTotalView(t.TaxId, t.Name, t.Amount)).ToArray());
    }

    private static SaleLineView ToLineView(SaleLine line) => new(
        line.Product.Id,
        line.Product.Name,
        line.Product.Code,
        line.Quantity.Value,
        line.Product.UnitPrice,
        line.Discount.Value,
        line.GrossAmount,
        line.Amounts.Subtotal,
        line.Amounts.TaxAmount,
        line.Amounts.Total);
}
