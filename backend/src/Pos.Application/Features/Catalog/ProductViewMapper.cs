using Pos.Application.Contracts.Gateways;
using Pos.Domain.Catalog;
using Pos.Domain.Taxes;

namespace Pos.Application.Features.Catalog;

/// <summary>Convierte un producto del dominio en la vista que muestra el catálogo, con el precio calculado con las reglas verificadas.</summary>
internal static class ProductViewMapper
{
    public static ProductView ToView(Product product, PricingSettings pricing)
    {
        var sellable = product.EnsureSellable();
        var priceWithTaxes = sellable.IsSuccess
            ? TaxCalculator.Calculate(
                [new TaxableLine(product.UnitPrice, 1m, 0m, product.Taxes)],
                pricing.Currency,
                pricing.TaxRounding).Total
            : pricing.Currency.Round(product.UnitPrice);

        return new ProductView(
            product.Id,
            product.Name,
            product.Code,
            product.Barcode,
            product.UnitPrice,
            priceWithTaxes,
            product.Taxes.Any(t => t.PriceIncluded),
            product.Taxes.Select(t => new TaxView(t.Id, t.Name, t.Amount, t.PriceIncluded)).ToArray(),
            product.Categories.Select(c => new CategoryView(c.Id, c.Name)).ToArray(),
            new AvailabilityView(product.Availability.Status, product.Availability.Quantity),
            sellable.IsSuccess,
            sellable.Errors.Select(e => e.Message).ToArray());
    }
}
