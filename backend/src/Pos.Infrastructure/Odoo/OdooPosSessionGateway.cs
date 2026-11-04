using System.Text.Json;
using Microsoft.Extensions.Options;
using Pos.Application.Contracts.Gateways;
using Pos.Domain.Common;
using Pos.Domain.Payments;
using Pos.Domain.Sessions;

namespace Pos.Infrastructure.Odoo;

/// <summary>Adaptador de la sesión POS: combina <c>pos.config</c>, <c>pos.session</c> y <c>pos.payment.method</c>.</summary>
public sealed class OdooPosSessionGateway(
    OdooJson2Client odoo,
    OdooReferenceData referenceData,
    IOptions<OdooOptions> options) : IPosSessionGateway
{
    private static readonly string[] ConfigFields =
        ["id", "name", "currency_id", "company_id", "cash_rounding", "default_fiscal_position_id", "payment_method_ids"];

    private readonly int _configId = options.Value.PosConfigId;

    public async Task<Result<PosSessionSnapshot>> GetActiveSessionAsync(CancellationToken cancellationToken)
    {
        var config = await ReadConfigAsync(cancellationToken);
        if (config.IsFailure)
        {
            return Result<PosSessionSnapshot>.Failure(config.Errors);
        }

        var sessionResponse = await odoo.CallAsync(
            "pos.session",
            "search_read",
            new Dictionary<string, object?>
            {
                ["domain"] = new object[]
                {
                    new object[] { "config_id", "=", _configId },
                    new object[] { "state", "!=", "closed" },
                },
                ["fields"] = new[] { "id", "name", "state", "start_at", "payment_method_ids" },
                ["order"] = "id desc",
                ["limit"] = 1,
            },
            cancellationToken);
        if (sessionResponse is not OdooResponse.Success sessionSuccess)
        {
            return OdooErrors.From(sessionResponse);
        }

        if (OdooValues.Records(sessionSuccess.Result).FirstOrDefault() is not { ValueKind: JsonValueKind.Object } sessionRecord)
        {
            return SessionErrors.NoOpenSession();
        }

        var pricing = await GetPricingAsync(config.Value, cancellationToken);
        if (pricing.IsFailure)
        {
            return Result<PosSessionSnapshot>.Failure(pricing.Errors);
        }

        var methodIds = OdooValues.Ids(sessionRecord, "payment_method_ids");
        var methods = await ReadPaymentMethodsAsync(methodIds.Count > 0 ? methodIds : config.Value.PaymentMethodIds, cancellationToken);
        if (methods.IsFailure)
        {
            return Result<PosSessionSnapshot>.Failure(methods.Errors);
        }

        var session = new PosSession(
            OdooValues.Int(sessionRecord, "id"),
            OdooValues.String(sessionRecord, "name") ?? string.Empty,
            MapState(OdooValues.String(sessionRecord, "state")),
            config.Value.Id,
            config.Value.Name,
            OdooValues.DateTimeUtc(sessionRecord, "start_at"),
            pricing.Value.Currency,
            pricing.Value.Rounding,
            methods.Value,
            UnsupportedSettings(config.Value, pricing.Value.CompanyCurrencyId));

        return new PosSessionSnapshot(session, pricing.Value.Format);
    }

    public async Task<Result<PricingSettings>> GetPricingSettingsAsync(CancellationToken cancellationToken)
    {
        var config = await ReadConfigAsync(cancellationToken);
        if (config.IsFailure)
        {
            return Result<PricingSettings>.Failure(config.Errors);
        }

        var pricing = await GetPricingAsync(config.Value, cancellationToken);
        return pricing.Map(p => new PricingSettings(p.Currency, p.Rounding));
    }

    private async Task<Result<ConfigRecord>> ReadConfigAsync(CancellationToken cancellationToken)
    {
        var response = await odoo.CallAsync(
            "pos.config",
            "search_read",
            new Dictionary<string, object?>
            {
                ["domain"] = new object[] { new object[] { "id", "=", _configId } },
                ["fields"] = ConfigFields,
                ["limit"] = 1,
            },
            cancellationToken);
        if (response is not OdooResponse.Success success)
        {
            return OdooErrors.From(response);
        }

        if (OdooValues.Records(success.Result).FirstOrDefault() is not { ValueKind: JsonValueKind.Object } record)
        {
            return Error.NotFound(
                "SESSION.CONFIG_NOT_FOUND",
                $"El punto de venta {_configId} no existe en Odoo. Revise Odoo:PosConfigId en la configuración del backend.");
        }

        return new ConfigRecord(
            OdooValues.Int(record, "id"),
            OdooValues.String(record, "name") ?? string.Empty,
            OdooValues.Many2One(record, "currency_id")?.Id ?? 0,
            OdooValues.Many2One(record, "company_id")?.Id ?? 0,
            OdooValues.Bool(record, "cash_rounding"),
            OdooValues.Many2One(record, "default_fiscal_position_id") is not null,
            OdooValues.Ids(record, "payment_method_ids"));
    }

    private async Task<Result<PricingRecord>> GetPricingAsync(ConfigRecord config, CancellationToken cancellationToken)
    {
        var company = await referenceData.GetCompanySettingsAsync(config.CompanyId, cancellationToken);
        if (company.IsFailure)
        {
            return Result<PricingRecord>.Failure(company.Errors);
        }

        var currency = await referenceData.GetCurrencyAsync(config.CurrencyId, cancellationToken);
        if (currency.IsFailure)
        {
            return Result<PricingRecord>.Failure(currency.Errors);
        }

        return new PricingRecord(currency.Value.Currency, currency.Value.Format, company.Value.Rounding, company.Value.CurrencyId);
    }

    private async Task<Result<IReadOnlyList<PaymentMethod>>> ReadPaymentMethodsAsync(IReadOnlyList<int> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return Result<IReadOnlyList<PaymentMethod>>.Ok([]);
        }

        var response = await odoo.CallAsync(
            "pos.payment.method",
            "read",
            new Dictionary<string, object?> { ["ids"] = ids, ["fields"] = new[] { "id", "name", "type", "payment_method_type" } },
            cancellationToken);
        if (response is not OdooResponse.Success success)
        {
            return OdooErrors.From(response);
        }

        var methods = OdooValues.Records(success.Result)
            .Select(r => new PaymentMethod(
                OdooValues.Int(r, "id"),
                OdooValues.String(r, "name") ?? string.Empty,
                OdooValues.String(r, "type") switch
                {
                    "cash" => PaymentMethodType.Cash,
                    "bank" => PaymentMethodType.Bank,
                    _ => PaymentMethodType.Other,
                },
                UsesTerminal: (OdooValues.String(r, "payment_method_type") ?? "none") != "none"))
            .ToArray();
        return Result<IReadOnlyList<PaymentMethod>>.Ok(methods);
    }

    private static PosSessionState MapState(string? state) => state switch
    {
        "opening_control" => PosSessionState.OpeningControl,
        "opened" => PosSessionState.Opened,
        "closing_control" => PosSessionState.ClosingControl,
        _ => PosSessionState.Closed,
    };

    /// <summary>Configuraciones de Odoo que cambian el total calculado y que este POS no replica.</summary>
    private static string[] UnsupportedSettings(ConfigRecord config, int companyCurrencyId)
    {
        var issues = new List<string>();
        if (config.CashRounding)
        {
            issues.Add("El punto de venta tiene activo el redondeo de efectivo, que este POS no soporta. Desactívelo en Odoo.");
        }

        if (config.HasDefaultFiscalPosition)
        {
            issues.Add("El punto de venta tiene una posición fiscal predeterminada, que este POS no soporta. Quítela en Odoo.");
        }

        if (companyCurrencyId != 0 && config.CurrencyId != companyCurrencyId)
        {
            issues.Add("La moneda del punto de venta es distinta de la moneda de la compañía; este POS no soporta multimoneda.");
        }

        return issues.ToArray();
    }

    private sealed record ConfigRecord(
        int Id,
        string Name,
        int CurrencyId,
        int CompanyId,
        bool CashRounding,
        bool HasDefaultFiscalPosition,
        IReadOnlyList<int> PaymentMethodIds);

    private sealed record PricingRecord(
        Pos.Domain.Money.Currency Currency,
        CurrencyFormat Format,
        Pos.Domain.Taxes.TaxRoundingMode Rounding,
        int CompanyCurrencyId);
}
