using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Pos.Application.Contracts.Gateways;
using Pos.Domain.Catalog;
using Pos.Domain.Common;
using Pos.Domain.Money;
using Pos.Domain.Taxes;

namespace Pos.Infrastructure.Odoo;

/// <summary>
/// Datos de referencia de Odoo que cambian poco (impuestos, categorías, moneda, redondeo).
/// Se guardan en caché por un tiempo corto para no repetir llamadas en cada búsqueda.
/// </summary>
public sealed class OdooReferenceData(OdooJson2Client odoo, IMemoryCache cache, IOptions<OdooOptions> options)
{
    private static readonly string[] TaxFields = ["id", "name", "amount", "amount_type", "price_include", "include_base_amount"];

    private readonly TimeSpan _ttl = TimeSpan.FromSeconds(options.Value.ReferenceDataCacheSeconds);

    public async Task<Result<IReadOnlyDictionary<int, Tax>>> GetTaxesAsync(IEnumerable<int> taxIds, CancellationToken cancellationToken)
    {
        var requested = taxIds.Distinct().ToArray();
        var result = new Dictionary<int, Tax>();
        var missing = new List<int>();
        foreach (var id in requested)
        {
            if (cache.TryGetValue(TaxKey(id), out Tax? tax) && tax is not null)
            {
                result[id] = tax;
            }
            else
            {
                missing.Add(id);
            }
        }

        if (missing.Count > 0)
        {
            var response = await odoo.CallAsync(
                "account.tax",
                "read",
                new Dictionary<string, object?> { ["ids"] = missing, ["fields"] = TaxFields },
                cancellationToken,
                context: new Dictionary<string, object?> { ["active_test"] = false });
            if (response is not OdooResponse.Success success)
            {
                return OdooErrors.From(response);
            }

            foreach (var record in OdooValues.Records(success.Result))
            {
                var tax = MapTax(record);
                cache.Set(TaxKey(tax.Id), tax, _ttl);
                result[tax.Id] = tax;
            }
        }

        return Result<IReadOnlyDictionary<int, Tax>>.Ok(result);
    }

    public async Task<Result<IReadOnlyList<ProductCategory>>> GetCategoriesAsync(CancellationToken cancellationToken)
    {
        const string key = "odoo:pos-categories";
        if (cache.TryGetValue(key, out IReadOnlyList<ProductCategory>? cached) && cached is not null)
        {
            return Result<IReadOnlyList<ProductCategory>>.Ok(cached);
        }

        var response = await odoo.CallAsync(
            "pos.category",
            "search_read",
            new Dictionary<string, object?>
            {
                ["domain"] = Array.Empty<object>(),
                ["fields"] = new[] { "id", "display_name" },
                ["order"] = "sequence, name",
            },
            cancellationToken);
        if (response is not OdooResponse.Success success)
        {
            return OdooErrors.From(response);
        }

        IReadOnlyList<ProductCategory> categories = OdooValues.Records(success.Result)
            .Select(r => new ProductCategory(OdooValues.Int(r, "id"), OdooValues.String(r, "display_name") ?? string.Empty))
            .ToArray();
        cache.Set(key, categories, _ttl);
        return Result<IReadOnlyList<ProductCategory>>.Ok(categories);
    }

    public async Task<Result<(Currency Currency, CurrencyFormat Format)>> GetCurrencyAsync(int currencyId, CancellationToken cancellationToken)
    {
        var key = $"odoo:currency:{currencyId}";
        if (cache.TryGetValue(key, out (Currency, CurrencyFormat) cached) && cached.Item1 is not null)
        {
            return cached;
        }

        var response = await odoo.CallAsync(
            "res.currency",
            "read",
            new Dictionary<string, object?> { ["ids"] = new[] { currencyId }, ["fields"] = new[] { "name", "symbol", "position", "rounding" } },
            cancellationToken);
        if (response is not OdooResponse.Success success || OdooValues.Records(success.Result).FirstOrDefault() is not { ValueKind: JsonValueKind.Object } record)
        {
            return OdooErrors.From(response);
        }

        var currency = new Currency(OdooValues.String(record, "name") ?? "XXX", OdooValues.Decimal(record, "rounding"));
        var position = OdooValues.String(record, "position") == "after" ? CurrencySymbolPosition.After : CurrencySymbolPosition.Before;
        var value = (currency, new CurrencyFormat(OdooValues.String(record, "symbol") ?? currency.Code, position));
        cache.Set(key, value, _ttl);
        return value;
    }

    public async Task<Result<(TaxRoundingMode Rounding, int CurrencyId)>> GetCompanySettingsAsync(int companyId, CancellationToken cancellationToken)
    {
        var key = $"odoo:company:{companyId}";
        if (cache.TryGetValue(key, out (TaxRoundingMode, int) cached) && cached.Item2 > 0)
        {
            return cached;
        }

        var response = await odoo.CallAsync(
            "res.company",
            "read",
            new Dictionary<string, object?> { ["ids"] = new[] { companyId }, ["fields"] = new[] { "tax_calculation_rounding_method", "currency_id" } },
            cancellationToken);
        if (response is not OdooResponse.Success success || OdooValues.Records(success.Result).FirstOrDefault() is not { ValueKind: JsonValueKind.Object } record)
        {
            return OdooErrors.From(response);
        }

        var rounding = OdooValues.String(record, "tax_calculation_rounding_method") == "round_per_line"
            ? TaxRoundingMode.PerLine
            : TaxRoundingMode.Global;
        var value = (rounding, OdooValues.Many2One(record, "currency_id")?.Id ?? 0);
        cache.Set(key, value, _ttl);
        return value;
    }

    /// <summary>Indica si Odoo expone existencias (módulo de inventario instalado).</summary>
    public async Task<bool> SupportsStockAsync(CancellationToken cancellationToken)
    {
        const string key = "odoo:supports-stock";
        if (cache.TryGetValue(key, out bool supported))
        {
            return supported;
        }

        var response = await odoo.CallAsync(
            "product.product",
            "fields_get",
            new Dictionary<string, object?> { ["allfields"] = new[] { "qty_available", "is_storable" }, ["attributes"] = new[] { "type" } },
            cancellationToken);
        supported = response is OdooResponse.Success success
            && OdooValues.Has(success.Result, "qty_available")
            && OdooValues.Has(success.Result, "is_storable");
        cache.Set(key, supported, _ttl);
        return supported;
    }

    private static Tax MapTax(JsonElement record) => new(
        OdooValues.Int(record, "id"),
        OdooValues.String(record, "name") ?? string.Empty,
        OdooValues.String(record, "amount_type") switch
        {
            "percent" => TaxAmountType.Percent,
            "fixed" => TaxAmountType.Fixed,
            "division" => TaxAmountType.Division,
            "group" => TaxAmountType.Group,
            _ => TaxAmountType.Other,
        },
        OdooValues.Decimal(record, "amount"),
        OdooValues.Bool(record, "price_include"),
        OdooValues.Bool(record, "include_base_amount"));

    private static string TaxKey(int id) => $"odoo:tax:{id}";
}
