using Pos.Domain.Common;

namespace Pos.Application.Features.Sales;

public static class IdempotencyErrors
{
    public const string KeyRequiredCode = "IDEMPOTENCY.KEY_REQUIRED";
    public const string KeyReusedCode = "IDEMPOTENCY.KEY_REUSED";
    public const string InProgressCode = "SALE.IN_PROGRESS";
    public const string AttemptNotFoundCode = "SALE.ATTEMPT_NOT_FOUND";
    public const string PaymentRequiredCode = "PAYMENT.REQUIRED";

    public static Error KeyRequired() => Error.Validation(
        KeyRequiredCode,
        "Falta la clave de idempotencia de la venta (encabezado Idempotency-Key con un UUID).",
        "Idempotency-Key");

    public static Error KeyReused() => Error.Conflict(
        KeyReusedCode,
        "La clave de idempotencia ya se usó para otra venta distinta. Genere una clave nueva para esta venta.");

    public static Error InProgress() => Error.Conflict(
        InProgressCode,
        "Esta venta ya se está procesando. Espere el resultado antes de volver a intentarlo.");

    public static Error AttemptNotFound(Guid key) => Error.NotFound(
        AttemptNotFoundCode,
        $"No hay ningún intento de venta registrado con la clave {key}.");

    public static Error PaymentRequired() => Error.Validation(
        PaymentRequiredCode,
        "Indique el método de pago y el monto recibido.",
        "payment");
}
