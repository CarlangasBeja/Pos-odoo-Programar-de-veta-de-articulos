using Pos.Domain.Common;

namespace Pos.Domain.Catalog;

/// <summary>Errores del catálogo con códigos estables (evita cadenas mágicas repartidas por el código).</summary>
public static class ProductErrors
{
    public const string NotSellableCode = "PRODUCT.NOT_SELLABLE";
    public const string NotFoundCode = "PRODUCT.NOT_FOUND";

    public static Error NotFound(int productId) => Error.NotFound(
        NotFoundCode,
        $"El producto {productId} no existe en Odoo o no está disponible.",
        Target(productId));

    public static Error Inactive(Product product) => Error.Rule(
        NotSellableCode,
        $"El producto «{product.Name}» está archivado en Odoo.",
        Target(product.Id));

    public static Error NotForSale(Product product) => Error.Rule(
        NotSellableCode,
        $"El producto «{product.Name}» no está marcado como vendible en Odoo.",
        Target(product.Id));

    public static Error NotAvailableInPos(Product product) => Error.Rule(
        NotSellableCode,
        $"El producto «{product.Name}» no está habilitado para el punto de venta.",
        Target(product.Id));

    public static Error NegativePrice(Product product) => Error.Rule(
        NotSellableCode,
        $"El producto «{product.Name}» tiene un precio negativo en Odoo.",
        Target(product.Id));

    public static Error UnsupportedTaxes(Product product) => Error.Rule(
        NotSellableCode,
        $"El producto «{product.Name}» tiene una configuración de impuestos que este POS no puede calcular con exactitud.",
        Target(product.Id));

    private static string Target(int productId) => $"products[{productId}]";
}
