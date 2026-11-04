using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pos.Application.Common;
using Pos.Application.Contracts.Gateways;
using Pos.Application.Features.History;
using Pos.Application.Features.Sales;
using Pos.Domain.Common;
using Pos.Domain.Sales;

namespace Pos.Infrastructure.Odoo;

/// <summary>
/// Adaptador de órdenes POS. Registra la venta con <c>pos.order.sync_from_ui</c> (mecanismo del POS web de Odoo 20)
/// y nunca concluye "éxito" sin leer la orden de vuelta desde Odoo (RN-07).
/// </summary>
public sealed class OdooPosOrderGateway(
    OdooJson2Client odoo,
    IOptions<OdooOptions> options,
    ILogger<OdooPosOrderGateway> logger) : IPosOrderGateway
{
    private const string OrderModel = "pos.order";

    private static readonly string[] SummaryFields =
        ["id", "name", "pos_reference", "date_order", "state", "amount_total", "amount_paid", "amount_return", "session_id", "internal_note"];

    private static readonly string[] DetailFields = [.. SummaryFields, "amount_tax", "uuid"];

    private readonly OdooOptions _options = options.Value;

    public async Task<OrderSubmissionOutcome> SubmitAsync(Sale sale, OperatorIdentity registeredBy, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sale);

        var payload = PosOrderPayloadMapper.Map(sale, registeredBy);
        var response = await odoo.CallAsync(
            OrderModel,
            "sync_from_ui",
            new Dictionary<string, object?> { ["orders"] = new object[] { payload } },
            cancellationToken,
            odoo.SubmitTimeout);

        switch (response)
        {
            case OdooResponse.TransportFailure { RequestMayHaveBeenProcessed: false }:
                return new OrderSubmissionOutcome.Rejected(OdooErrors.Unavailable());

            case OdooResponse.Fault { IsAuthenticationProblem: true }:
                return new OrderSubmissionOutcome.Rejected(OdooErrors.Unauthorized());

            case OdooResponse.Fault { IsPermissionProblem: true } forbidden:
                return new OrderSubmissionOutcome.Rejected(OdooErrors.Forbidden(forbidden));
        }

        // Respuesta exitosa, error de Odoo o falla ambigua: en todos los casos se verifica la orden en Odoo.
        var verification = await FindBySaleKeyAsync(sale.Key, cancellationToken);
        if (verification is { IsSuccess: true, Value: { } order })
        {
            return new OrderSubmissionOutcome.Accepted(order);
        }

        switch (response)
        {
            case OdooResponse.Fault fault when verification.IsSuccess:
                logger.LogWarning("Odoo rechazó la venta {SaleKey}: {ErrorName} {Message}", sale.Key, fault.ErrorName, fault.Message);
                return new OrderSubmissionOutcome.Rejected(SaleRejected(fault));

            case OdooResponse.Success when verification.IsSuccess:
                // Odoo respondió bien pero la orden no aparece: algo raro pasó; no se afirma nada.
                return new OrderSubmissionOutcome.Unknown("Odoo respondió, pero la orden no se encontró al verificarla.");

            default:
                logger.LogWarning("Resultado incierto para la venta {SaleKey}", sale.Key);
                return new OrderSubmissionOutcome.Unknown("No hubo confirmación de Odoo.");
        }
    }

    public async Task<Result<RegisteredOrder?>> FindBySaleKeyAsync(Guid saleKey, CancellationToken cancellationToken)
    {
        var response = await odoo.CallAsync(
            OrderModel,
            "search_read",
            new Dictionary<string, object?>
            {
                ["domain"] = new object[] { new object[] { "uuid", "=", saleKey.ToString() } },
                ["fields"] = SummaryFields,
                ["limit"] = 1,
            },
            cancellationToken);
        if (response is not OdooResponse.Success success)
        {
            return OdooErrors.From(response);
        }

        var order = OdooValues.Records(success.Result).Select(MapRegistered).FirstOrDefault();
        return Result<RegisteredOrder?>.Ok(order);
    }

    public async Task<Result<PagedResult<OrderSummary>>> SearchAsync(OrderSearchCriteria criteria, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        var domain = BuildHistoryDomain(criteria);

        var count = await odoo.CallAsync(OrderModel, "search_count", new Dictionary<string, object?> { ["domain"] = domain }, cancellationToken);
        if (count is not OdooResponse.Success countResult)
        {
            return OdooErrors.From(count);
        }

        var response = await odoo.CallAsync(
            OrderModel,
            "search_read",
            new Dictionary<string, object?>
            {
                ["domain"] = domain,
                ["fields"] = SummaryFields,
                ["offset"] = criteria.Page.Offset,
                ["limit"] = criteria.Page.PageSize,
                ["order"] = "date_order desc, id desc",
            },
            cancellationToken);
        if (response is not OdooResponse.Success success)
        {
            return OdooErrors.From(response);
        }

        var items = OdooValues.Records(success.Result).Select(MapSummary).ToArray();
        return new PagedResult<OrderSummary>(items, criteria.Page.Page, criteria.Page.PageSize, countResult.Result.GetInt32());
    }

    public async Task<Result<OrderDetail?>> GetDetailAsync(int orderId, CancellationToken cancellationToken)
    {
        var orderResponse = await odoo.CallAsync(
            OrderModel,
            "search_read",
            new Dictionary<string, object?>
            {
                ["domain"] = new object[]
                {
                    new object[] { "id", "=", orderId },
                    new object[] { "config_id", "=", _options.PosConfigId },
                },
                ["fields"] = DetailFields,
                ["limit"] = 1,
            },
            cancellationToken);
        if (orderResponse is not OdooResponse.Success orderSuccess)
        {
            return OdooErrors.From(orderResponse);
        }

        if (OdooValues.Records(orderSuccess.Result).FirstOrDefault() is not { ValueKind: JsonValueKind.Object } order)
        {
            return Result<OrderDetail?>.Ok(null);
        }

        var linesResponse = await odoo.CallAsync(
            "pos.order.line",
            "search_read",
            new Dictionary<string, object?>
            {
                ["domain"] = new object[] { new object[] { "order_id", "=", orderId } },
                ["fields"] = new[] { "product_id", "full_product_name", "qty", "price_unit", "discount", "price_subtotal", "price_subtotal_incl" },
                ["order"] = "id asc",
            },
            cancellationToken);
        if (linesResponse is not OdooResponse.Success linesSuccess)
        {
            return OdooErrors.From(linesResponse);
        }

        var paymentsResponse = await odoo.CallAsync(
            "pos.payment",
            "search_read",
            new Dictionary<string, object?>
            {
                ["domain"] = new object[] { new object[] { "pos_order_id", "=", orderId } },
                ["fields"] = new[] { "payment_method_id", "amount", "is_change", "payment_date" },
                ["order"] = "is_change asc, id asc",
            },
            cancellationToken);
        if (paymentsResponse is not OdooResponse.Success paymentsSuccess)
        {
            return OdooErrors.From(paymentsResponse);
        }

        var summary = MapSummary(order);
        var lines = OdooValues.Records(linesSuccess.Result).Select(l => new OrderLineDetail(
            OdooValues.Many2One(l, "product_id")?.Id ?? 0,
            OdooValues.String(l, "full_product_name") ?? OdooValues.Many2One(l, "product_id")?.Name ?? string.Empty,
            OdooValues.Decimal(l, "qty"),
            OdooValues.Decimal(l, "price_unit"),
            OdooValues.Decimal(l, "discount"),
            OdooValues.Decimal(l, "price_subtotal"),
            OdooValues.Decimal(l, "price_subtotal_incl"))).ToArray();
        var payments = OdooValues.Records(paymentsSuccess.Result).Select(p => new OrderPaymentDetail(
            OdooValues.Many2One(p, "payment_method_id")?.Name ?? string.Empty,
            OdooValues.Decimal(p, "amount"),
            OdooValues.Bool(p, "is_change"),
            OdooValues.DateTimeUtc(p, "payment_date"))).ToArray();

        return new OrderDetail(
            summary.Id,
            summary.Name,
            summary.PosReference,
            OdooValues.String(order, "uuid"),
            summary.Date,
            summary.State,
            summary.SessionName,
            OdooValues.Decimal(order, "amount_tax"),
            summary.AmountTotal,
            summary.AmountPaid,
            OdooValues.Decimal(order, "amount_return"),
            summary.Note,
            lines,
            payments);
    }

    internal object[] BuildHistoryDomain(OrderSearchCriteria criteria)
    {
        var domain = new List<object> { new object[] { "config_id", "=", _options.PosConfigId } };

        if (criteria.Text is { } text)
        {
            domain.Add("|");
            domain.Add("|");
            domain.Add(new object[] { "name", "ilike", text });
            domain.Add(new object[] { "pos_reference", "ilike", text });
            domain.Add(new object[] { "lines.full_product_name", "ilike", text });
        }

        var state = criteria.State switch
        {
            OrderStateFilter.Paid => "paid",
            OrderStateFilter.Posted => "done",
            OrderStateFilter.Draft => "draft",
            OrderStateFilter.Cancelled => "cancel",
            _ => null,
        };
        if (state is not null)
        {
            domain.Add(new object[] { "state", "=", state });
        }

        var zone = ResolveTimeZone(_options.TimeZone);
        if (criteria.From is { } from)
        {
            domain.Add(new object[] { "date_order", ">=", OdooValues.FormatDateTimeUtc(StartOfDayUtc(from, zone)) });
        }

        if (criteria.To is { } to)
        {
            domain.Add(new object[] { "date_order", "<", OdooValues.FormatDateTimeUtc(StartOfDayUtc(to.AddDays(1), zone)) });
        }

        return domain.ToArray();
    }

    private static Error SaleRejected(OdooResponse.Fault fault)
    {
        var odooError = OdooErrors.Rejected(fault);
        return odooError with { Code = "SALE.REJECTED_BY_ODOO", Message = $"La venta no se registró. {odooError.Message}" };
    }

    private static RegisteredOrder MapRegistered(JsonElement record) => new(
        OdooValues.Int(record, "id"),
        OdooValues.String(record, "name") ?? string.Empty,
        OdooValues.String(record, "pos_reference"),
        MapState(OdooValues.String(record, "state")),
        OdooValues.Decimal(record, "amount_total"),
        OdooValues.Decimal(record, "amount_paid"),
        OdooValues.Decimal(record, "amount_return"),
        OdooValues.DateTimeUtc(record, "date_order") ?? DateTimeOffset.MinValue);

    private static OrderSummary MapSummary(JsonElement record) => new(
        OdooValues.Int(record, "id"),
        OdooValues.String(record, "name") ?? string.Empty,
        OdooValues.String(record, "pos_reference"),
        OdooValues.DateTimeUtc(record, "date_order") ?? DateTimeOffset.MinValue,
        MapState(OdooValues.String(record, "state")),
        OdooValues.Decimal(record, "amount_total"),
        OdooValues.Decimal(record, "amount_paid"),
        OdooValues.Many2One(record, "session_id")?.Name ?? string.Empty,
        OdooValues.String(record, "internal_note"));

    private static OrderState MapState(string? state) => state switch
    {
        "draft" => OrderState.Draft,
        "paid" => OrderState.Paid,
        "done" => OrderState.Posted,
        "cancel" => OrderState.Cancelled,
        _ => OrderState.Unknown,
    };

    private static DateTimeOffset StartOfDayUtc(DateOnly date, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }

    private static TimeZoneInfo ResolveTimeZone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}
