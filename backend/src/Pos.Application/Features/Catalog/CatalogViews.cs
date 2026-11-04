using Pos.Application.Common;
using Pos.Domain.Catalog;

namespace Pos.Application.Features.Catalog;

public enum ProductSort
{
    NameAsc,
    NameDesc,
    PriceAsc,
    PriceDesc,
    CodeAsc,
}

/// <summary>Criterios de búsqueda del catálogo (RF-02).</summary>
public sealed record ProductSearchCriteria(
    string? Text,
    int? CategoryId,
    bool OnlyInStock,
    ProductSort Sort,
    PageRequest Page);

public sealed record TaxView(int Id, string Name, decimal Rate, bool PriceIncluded);

public sealed record CategoryView(int Id, string Name);

public sealed record AvailabilityView(AvailabilityStatus Status, decimal? Quantity);

/// <summary>Producto listo para mostrarse. El precio con impuestos se calcula con las reglas verificadas.</summary>
public sealed record ProductView(
    int Id,
    string Name,
    string? Code,
    string? Barcode,
    decimal UnitPrice,
    decimal PriceWithTaxes,
    bool PriceIncludesTax,
    IReadOnlyList<TaxView> Taxes,
    IReadOnlyList<CategoryView> Categories,
    AvailabilityView Availability,
    bool Sellable,
    IReadOnlyList<string> NotSellableReasons);
