using Pos.Application.Contracts.Persistence;
using Pos.Domain.Common;
using Pos.Domain.Payments;
using Pos.Domain.Sales;

namespace Pos.Application.Features.Sales;

/// <summary>Datos del cobro que se conservan junto al intento para poder informarlos más tarde.</summary>
internal sealed record PaymentSummary(decimal Total, decimal AmountTendered, decimal Change, string MethodName, PaymentMethodType MethodType)
{
    public static PaymentSummary From(Sale sale)
    {
        var payment = sale.Payment ?? throw new InvalidOperationException("La venta confirmada debe tener pago.");
        return new PaymentSummary(sale.Totals.Total, payment.AmountTendered, payment.Change, payment.Method.Name, payment.Method.Type);
    }

    public static PaymentSummary From(SaleRegistrationResult result) =>
        new(result.Total, result.AmountTendered, result.Change, result.PaymentMethodName, result.PaymentMethodType);
}

/// <summary>Construye los intentos de venta y sus mensajes a partir de lo que realmente respondió el ERP.</summary>
internal sealed class SaleAttemptFactory(TimeProvider time)
{
    public const string UncertainMessage =
        "No se recibió confirmación de Odoo. No vuelva a cobrar: verifique el estado de la venta. "
        + "Reintentar con la misma venta es seguro y no la duplica.";

    public SaleAttempt FromOrder(Guid key, string fingerprint, RegisteredOrder order, PaymentSummary payment, DateTimeOffset createdAt)
    {
        var now = time.GetUtcNow();
        if (order.IsPaid)
        {
            var registered = new SaleRegistrationResult(
                key,
                SaleRegistrationStatus.Registered,
                $"Venta registrada y pagada en Odoo ({order.Name}).",
                order.Id,
                order.Name,
                order.PosReference,
                order.State,
                order.AmountTotal,
                payment.AmountTendered,
                payment.Change,
                payment.MethodName,
                payment.MethodType,
                now);
            return new SaleAttempt(key, fingerprint, SaleAttemptStatus.Registered, registered, null, createdAt, now);
        }

        // RN-07: la orden existe, pero Odoo no confirma el pago; no se informa éxito.
        var pending = new SaleRegistrationResult(
            key,
            SaleRegistrationStatus.Uncertain,
            $"Odoo creó la orden {order.Name}, pero su estado es «{Describe(order.State)}» y el pago no está confirmado. "
            + "Revise la orden en Odoo antes de entregar la mercancía.",
            order.Id,
            order.Name,
            order.PosReference,
            order.State,
            payment.Total,
            payment.AmountTendered,
            payment.Change,
            payment.MethodName,
            payment.MethodType,
            now);
        return new SaleAttempt(key, fingerprint, SaleAttemptStatus.Uncertain, pending, null, createdAt, now);
    }

    public SaleAttempt Uncertain(Guid key, string fingerprint, PaymentSummary payment, string message, DateTimeOffset createdAt)
    {
        var now = time.GetUtcNow();
        var result = new SaleRegistrationResult(
            key,
            SaleRegistrationStatus.Uncertain,
            message,
            null,
            null,
            null,
            null,
            payment.Total,
            payment.AmountTendered,
            payment.Change,
            payment.MethodName,
            payment.MethodType,
            now);
        return new SaleAttempt(key, fingerprint, SaleAttemptStatus.Uncertain, result, null, createdAt, now);
    }

    public SaleAttempt Rejected(Guid key, string fingerprint, Error error, DateTimeOffset createdAt) =>
        new(key, fingerprint, SaleAttemptStatus.Rejected, null, error, createdAt, time.GetUtcNow());

    public static Result<SaleRegistrationResult> ToResult(SaleAttempt attempt, bool replayed) => attempt.Status switch
    {
        SaleAttemptStatus.Rejected => Result<SaleRegistrationResult>.Failure(
            attempt.Error ?? throw new InvalidOperationException("Un intento rechazado debe tener error.")),
        _ => (attempt.Result ?? throw new InvalidOperationException("El intento debe tener resultado.")) with { Replayed = replayed },
    };

    private static string Describe(OrderState state) => state switch
    {
        OrderState.Draft => "nueva (sin pagar)",
        OrderState.Paid => "pagada",
        OrderState.Posted => "contabilizada",
        OrderState.Cancelled => "cancelada",
        _ => "desconocido",
    };
}
