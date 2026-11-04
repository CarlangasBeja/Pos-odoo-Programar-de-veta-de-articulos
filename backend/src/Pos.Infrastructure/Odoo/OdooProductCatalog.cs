using System.Text.Json;
using Pos.Application.Common;
using Pos.Application.Contracts.Gateways;
using Pos.Application.Features.Catalog;
using Pos.Domain.Catalog;
using Pos.Domain.Common;
using Pos.Domain.Taxes;

namespace Pos.Infrastructure.Odoo;

/// <summary>Adaptador del catálogo: <c>product.product</c> de Odoo → <see cref="Product"/> del dominio.</summary>
public sealed class OdooProductCatalog(OdooJson2Client odoo, OdooReferenceData referenceData) : IProductCatalog
{
    private const string Model = "product.product";

    private static readonly string[] BaseFields =
        ["id", "name", "default_code", "barcode", "lst_price", "taxes_id", "pos_categ_ids", "available_in_pos", "sale_ok", "active"];

    private static readonly string[] StockFields = ["qty_available", "is_storable"];

    public async Task<Result<PagedResult<Product>>> SearchAsync(ProductSearchCriteria criteria, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        var supportsStock = await referenceData.SupportsStockAsync(cancellationToken);
        var domain = BuildDomain(criteria, supportsStock);

        var count = await odoo.CallAsync(Model, "search_count", new Dictionary<string, object?> { ["domain"] = domain }, cancellationToken);
        if (count is not OdooResponse.Success countResult)
        {
            return OdooErrors.From(count);
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
        return products.Map(list => new PagedResult<Product>(list, criteria.Page.Page, criteria.Page.PageSize, countResult.Result.GetInt32()));
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
            domain.Add("|");
            domain.Add("|");
            domain.Add(new object[] { "name", "ilike", text });
            domain.Add(new object[] { "default_code", "ilike", text });
            domain.Add(new object[] { "barcode", "ilike", text });
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
