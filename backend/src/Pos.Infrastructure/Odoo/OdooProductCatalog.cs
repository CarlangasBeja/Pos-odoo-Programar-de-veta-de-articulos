using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Pos.Application.Common;
using Pos.Application.Contracts.Gateways;
using Pos.Application.Features.Catalog;
using Pos.Domain.Catalog;
using Pos.Domain.Common;
using Pos.Domain.Taxes;

namespace Pos.Infrastructure.Odoo;

/// <summary>Adaptador del catálogo: <c>product.product</c> de Odoo → <see cref="Product"/> del dominio.</summary>
public sealed class OdooProductCatalog(OdooJson2Client odoo, OdooReferenceData referenceData, IMemoryCache cache) : IProductCatalog
{
    private const string Model = "product.product";

    /// <summary>
    /// Con un millón de productos, contar el total de una búsqueda amplia cuesta varios cientos de milisegundos y se
    /// repetía en cada página. Los totales grandes se guardan un minuto: que la pantalla diga "1 000 011 productos" o
    /// uno menos no cambia nada para el cajero. Los totales pequeños se cuentan siempre (son exactos y baratos).
    /// </summary>
    internal const int LargeCountThreshold = 10_000;

    internal static readonly TimeSpan LargeCountCacheTime = TimeSpan.FromSeconds(60);

    private static readonly string[] BaseFields =
        ["id", "name", "default_code", "barcode", "lst_price", "taxes_id", "pos_categ_ids", "available_in_pos", "sale_ok", "active"];

    private static readonly string[] StockFields = ["qty_available", "is_storable"];

    public async Task<Result<PagedResult<Product>>> SearchAsync(ProductSearchCriteria criteria, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        var supportsStock = await referenceData.SupportsStockAsync(cancellationToken);
        var domain = BuildDomain(criteria, supportsStock);

        var count = await CountAsync(domain, cancellationToken);
        if (count.IsFailure)
        {
            return Result<PagedResult<Product>>.Failure(count.Errors);
        }

        var response = await odoo.CallAsync(
            Model,
            "search_read",
            new Dictionary<string, object?>
            {
                ["domain"] = domain,
                ["fields"] = Fields(supportsStock),
                ["offset"] = criteria.Page.Offset,
                ["limit"] = criteria.Page.PageSize,
                ["order"] = OrderBy(criteria.Sort),
            },
            cancellationToken);
        if (response is not OdooResponse.Success success)
        {
            return OdooErrors.From(response);
        }

        var products = await MapProductsAsync(success.Result, supportsStock, cancellationToken);
        return products.Map(list => new PagedResult<Product>(list, criteria.Page.Page, criteria.Page.PageSize, count.Value));
    }

    public async Task<Result<IReadOnlyList<Product>>> GetByIdsAsync(IReadOnlyCollection<int> productIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(productIds);
        if (productIds.Count == 0)
        {
            return Result<IReadOnlyList<Product>>.Ok([]);
        }

        var supportsStock = await referenceData.SupportsStockAsync(cancellationToken);
        var response = await odoo.CallAsync(
            Model,
            "search_read",
            new Dictionary<string, object?>
            {
                ["domain"] = new object[] { new object[] { "id", "in", productIds } },
                ["fields"] = Fields(supportsStock),
            },
            cancellationToken,
            context: new Dictionary<string, object?> { ["active_test"] = false }); // para informar "archivado" en lugar de "no existe"
        if (response is not OdooResponse.Success success)
        {
            return OdooErrors.From(response);
        }

        return await MapProductsAsync(success.Result, supportsStock, cancellationToken);
    }

    private async Task<Result<int>> CountAsync(object[] domain, CancellationToken cancellationToken)
    {
        var key = "odoo:product-count:" + JsonSerializer.Serialize(domain);
        if (cache.TryGetValue(key, out int cached))
        {
            return cached;
        }

        var response = await odoo.CallAsync(Model, "search_count", new Dictionary<string, object?> { ["domain"] = domain }, cancellationToken);
        if (response is not OdooResponse.Success success)
        {
            return OdooErrors.From(response);
        }

        var total = success.Result.GetInt32();
        if (total >= LargeCountThreshold)
        {
            cache.Set(key, total, LargeCountCacheTime);
        }

        return total;
    }

    public Task<Result<IReadOnlyList<ProductCategory>>> GetCategoriesAsync(CancellationToken cancellationToken) =>
        referenceData.GetCategoriesAsync(cancellationToken);

    internal static object[] BuildDomain(ProductSearchCriteria criteria, bool supportsStock)
    {
        var domain = new List<object>
        {
            new object[] { "available_in_pos", "=", true },
            new object[] { "sale_ok", "=", true },
        };

        if (criteria.Text is { } text)
        {
            // Rendimiento con un millón de productos: el nombre vive en product.template y los códigos en product.product.
            // Un OR entre columnas de las dos tablas obliga a PostgreSQL a recorrer todo el catálogo. Con "any" sobre la
            // plantilla (que también guarda el código y el código de barras de los productos de una sola variante),
            // Odoo genera "product_tmpl_id IN (SELECT ... FROM product_template WHERE ...)" y PostgreSQL combina los
            // índices trigrama de esa tabla (odoo/tools/indices_rendimiento.sql). Ver docs/RENDIMIENTO_Y_RED.md.
            domain.Add(new object[]
            {
                "product_tmpl_id",
                "any",
                new object[]
                {
                    "|",
                    "|",
                    new object[] { "name", "ilike", text },
                    new object[] { "default_code", "ilike", text },
                    new object[] { "barcode", "ilike", text },
                },
            });
        }

        if (criteria.CategoryId is { } categoryId)
        {
            domain.Add(new object[] { "pos_categ_ids", "child_of", categoryId });
        }

        if (criteria.OnlyInStock && supportsStock)
        {
            domain.Add("|");
            domain.Add(new object[] { "is_storable", "=", false });
            domain.Add(new object[] { "qty_available", ">", 0 });
        }

        return domain.ToArray();
    }

    internal static string OrderBy(ProductSort sort) => sort switch
    {
        ProductSort.NameDesc => "name desc, id desc",
        ProductSort.PriceAsc => "list_price asc, name asc, id asc",
        ProductSort.PriceDesc => "list_price desc, name asc, id asc",
        ProductSort.CodeAsc => "default_code asc, name asc, id asc",
        _ => "name asc, id asc",
    };

    private static string[] Fields(bool supportsStock) => supportsStock ? [.. BaseFields, .. StockFields] : BaseFields;

    private async Task<Result<IReadOnlyList<Product>>> MapProductsAsync(JsonElement records, bool supportsStock, CancellationToken cancellationToken)
    {
        var rows = OdooValues.Records(records).ToArray();

        var taxes = await referenceData.GetTaxesAsync(rows.SelectMany(r => OdooValues.Ids(r, "taxes_id")), cancellationToken);
        if (taxes.IsFailure)
        {
            return Result<IReadOnlyList<Product>>.Failure(taxes.Errors);
        }

        var categories = await referenceData.GetCategoriesAsync(cancellationToken);
        if (categories.IsFailure)
        {
            return Result<IReadOnlyList<Product>>.Failure(categories.Errors);
        }

        var categoriesById = categories.Value.ToDictionary(c => c.Id);
        var products = rows.Select(row => MapProduct(row, taxes.Value, categoriesById, supportsStock)).ToArray();
        return Result<IReadOnlyList<Product>>.Ok(products);
    }

    private static Product MapProduct(
        JsonElement row,
        IReadOnlyDictionary<int, Tax> taxes,
        IReadOnlyDictionary<int, ProductCategory> categories,
        bool supportsStock)
    {
        var productTaxes = OdooValues.Ids(row, "taxes_id")
            .Select(id => taxes.TryGetValue(id, out var tax) ? tax : new Tax(id, $"Impuesto {id}", TaxAmountType.Other, 0m, false, false))
            .ToArray();

        var productCategories = OdooValues.Ids(row, "pos_categ_ids")
            .Where(categories.ContainsKey)
            .Select(id => categories[id])
            .ToArray();

        var availability = supportsStock && OdooValues.Bool(row, "is_storable")
            ? Availability.FromStock(OdooValues.Decimal(row, "qty_available"))
            : Availability.NotTracked;

        return new Product(
            OdooValues.Int(row, "id"),
            OdooValues.String(row, "name") ?? string.Empty,
            OdooValues.String(row, "default_code"),
            OdooValues.String(row, "barcode"),
            OdooValues.Decimal(row, "lst_price"),
            productTaxes,
            productCategories,
            OdooValues.Bool(row, "sale_ok"),
            OdooValues.Bool(row, "available_in_pos"),
            OdooValues.Bool(row, "active"),
            availability);
    }
}
