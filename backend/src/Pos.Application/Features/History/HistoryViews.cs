using Pos.Application.Common;
using Pos.Application.Features.Sales;

namespace Pos.Application.Features.History;

public enum OrderStateFilter
{
    All,
    Paid,
    Posted,
    Draft,
    Cancelled,
}

public sealed record OrderSearchCriteria(
    string? Text,
    OrderStateFilter State,
    DateOnly? From,
    DateOnly? To,
    PageRequest Page);

public sealed record OrderSummary(
    int Id,
    string Name,
    string? PosReference,
    DateTimeOffset Date,
    OrderState State,
    decimal AmountTotal,
    decimal AmountPaid,
    string SessionName,
    string? Note);

public sealed record OrderLineDetail(
    int ProductId,
    string ProductName,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountPercent,
    decimal Subtotal,
    decimal Total);

public sealed record OrderPaymentDetail(string MethodName, decimal Amount, bool IsChange, DateTimeOffset? Date);

public sealed record OrderDetail(
    int Id,
    string Name,
    string? PosReference,
    string? SaleKey,
    DateTimeOffset Date,
    OrderState State,
    string SessionName,
    decimal AmountTax,
    decimal AmountTotal,
    decimal AmountPaid,
    decimal AmountReturn,
    string? Note,
    IReadOnlyList<OrderLineDetail> Lines,
    IReadOnlyList<OrderPaymentDetail> Payments,
    OrderCustomer? Customer = null);

/// <summary>Cliente de la orden en Odoo (<c>partner_id</c>). Nulo = consumidor final.</summary>
public sealed record OrderCustomer(string Name, string? Identification);
