using MediatR;
using Pos.Application.Common;
using Pos.Domain.Common;

namespace Pos.Application.Features.Sales.Commands.RegisterSale;

/// <summary>
/// RF-09 a RF-12 y RF-15 a RF-17: confirmar la venta y registrarla en Odoo con su pago.
/// <see cref="IdempotencyKey"/> identifica el intento de cobro: repetir el Command con la misma clave no crea otra venta (RN-08).
/// El cliente nunca envía precios (RN-03).
/// </summary>
public sealed record RegisterSaleCommand(
    Guid IdempotencyKey,
    int SessionId,
    IReadOnlyList<SaleLineRequest>? Lines,
    DiscountRequest? Discount,
    PaymentRequest? Payment,
    OperatorIdentity Operator) : IRequest<Result<SaleRegistrationResult>>, ISaleDraft;
