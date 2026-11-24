using Pos.Domain.Payments;

namespace Pos.Application.Features.Sales;

// ---------- Solicitudes (el cliente nunca envía precios: RN-03) ----------

public sealed record SaleLineRequest(int ProductId, decimal Quantity);

public sealed record DiscountRequest(string PolicyCode, decimal? Percentage);

public sealed record PaymentRequest(int PaymentMethodId, decimal AmountTendered);

/// <summary>Cliente identificado (nombre y cédula). Sin cliente en la solicitud, la venta es a consumidor final.</summary>
public sealed record CustomerRequest(string? Name, string? Identification);

/// <summary>
/// Venta en preparación tal como la envía el cliente: sesión, productos con cantidades y descuento pedido.
/// La implementan la Query de vista previa y el Command de registro, que comparten reglas de forma y de negocio.
/// </summary>
public interface ISaleDraft
{
    int SessionId { get; }

    IReadOnlyList<SaleLineRequest>? Lines { get; }

    DiscountRequest? Discount { get; }
}

// ---------- Respuestas ----------

public sealed record SaleLineView(
    int ProductId,
    string ProductName,
    string? ProductCode,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountPercent,
    decimal GrossAmount,
    decimal Subtotal,
    decimal TaxAmount,
    decimal Total);

public sealed record TaxTotalView(int TaxId, string Name, decimal Amount);

public sealed record AppliedDiscountView(string PolicyCode, string Description, decimal Amount);

/// <summary>Venta calculada por el backend con precios y reglas verificadas, sin registrarla.</summary>
public sealed record SalePreview(
    int SessionId,
    string CurrencyCode,
    IReadOnlyList<SaleLineView> Lines,
    decimal GrossBeforeDiscount,
    AppliedDiscountView? Discount,
    decimal Untaxed,
    decimal TaxAmount,
    decimal Total,
    IReadOnlyList<TaxTotalView> Taxes);

public enum SaleRegistrationStatus
{
    /// <summary>Odoo confirmó la orden con estado pagado.</summary>
    Registered,

    /// <summary>No hay confirmación: no se afirma éxito (RF-17, RN-07).</summary>
    Uncertain,
}

public sealed record SaleRegistrationResult(
    Guid SaleKey,
    SaleRegistrationStatus Status,
    string Message,
    int? OrderId,
    string? OrderName,
    string? PosReference,
    OrderState? OrderState,
    decimal Total,
    decimal AmountTendered,
    decimal Change,
    string PaymentMethodName,
    PaymentMethodType PaymentMethodType,
    DateTimeOffset ProcessedAt,
    bool Replayed = false);

// ---------- Datos de órdenes en el ERP ----------

/// <summary>Estado de la orden en Odoo (<c>pos.order.state</c>).</summary>
public enum OrderState
{
    Draft,
    Paid,
    Posted,
    Cancelled,
    Unknown,
}

public sealed record RegisteredOrder(
    int Id,
    string Name,
    string? PosReference,
    OrderState State,
    decimal AmountTotal,
    decimal AmountPaid,
    decimal AmountReturn,
    DateTimeOffset DateOrder)
{
    public bool IsPaid => State is OrderState.Paid or OrderState.Posted;
}
