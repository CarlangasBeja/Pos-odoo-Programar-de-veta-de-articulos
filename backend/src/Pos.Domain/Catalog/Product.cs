using Pos.Domain.Common;
using Pos.Domain.Taxes;

namespace Pos.Domain.Catalog;

public sealed record ProductCategory(int Id, string Name);

public enum AvailabilityStatus
{
    /// <summary>Odoo informa existencias positivas.</summary>
    Available,

    /// <summary>Odoo informa existencias en cero o negativas.</summary>
    OutOfStock,

    /// <summary>Odoo no gestiona existencias para este producto (o el módulo de inventario no está instalado).</summary>
    NotTracked,
}

/// <summary>Disponibilidad del producto; solo se informa una cantidad cuando Odoo la entrega de forma fiable.</summary>
public sealed record Availability(AvailabilityStatus Status, decimal? Quantity)
{
    public static readonly Availability NotTracked = new(AvailabilityStatus.NotTracked, null);

    public static Availability FromStock(decimal quantity) =>
        new(quantity > 0 ? AvailabilityStatus.Available : AvailabilityStatus.OutOfStock, quantity);
}

/// <summary>
/// Producto del catálogo de Odoo (<c>product.product</c>). Odoo es la fuente de verdad:
/// este objeto es una instantánea de solo lectura usada para validar y calcular la venta.
/// </summary>
public sealed record Product(
    int Id,
    string Name,
    string? Code,
    string? Barcode,
    decimal UnitPrice,
    IReadOnlyList<Tax> Taxes,
    IReadOnlyList<ProductCategory> Categories,
    bool SaleOk,
    bool AvailableInPos,
    bool Active,
    Availability Availability)
{
    /// <summary>
    /// Comprueba que el producto está habilitado para venderse desde el POS (P04).
    /// Devuelve todos los motivos para que el operador entienda por qué no puede venderlo.
    /// </summary>
    public Result EnsureSellable()
    {
        var errors = new List<Error>();
        if (!Active)
        {
            errors.Add(ProductErrors.Inactive(this));
        }

        if (!SaleOk)
        {
            errors.Add(ProductErrors.NotForSale(this));
        }

        if (!AvailableInPos)
        {
            errors.Add(ProductErrors.NotAvailableInPos(this));
        }

        if (UnitPrice < 0)
        {
            errors.Add(ProductErrors.NegativePrice(this));
        }

        if (!TaxCalculator.SupportsTaxes(Taxes))
        {
            errors.Add(ProductErrors.UnsupportedTaxes(this));
        }

        return errors.Count == 0 ? Result.Success() : Result.Failure(errors);
    }

    public bool IsSellable => EnsureSellable().IsSuccess;
}
