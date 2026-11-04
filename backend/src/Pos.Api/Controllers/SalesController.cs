using MediatR;
using Microsoft.AspNetCore.Mvc;
using Pos.Api.Http;
using Pos.Application.Common;
using Pos.Application.Features.History;
using Pos.Application.Features.History.Queries.GetSaleDetail;
using Pos.Application.Features.History.Queries.GetSalesHistory;
using Pos.Application.Features.Sales;
using Pos.Application.Features.Sales.Commands.RegisterSale;
using Pos.Application.Features.Sales.Commands.VerifySaleAttempt;
using Pos.Application.Features.Sales.Queries.PreviewSale;

namespace Pos.Api.Controllers;

/// <summary>Cuerpo de la vista previa: el cliente envía productos y cantidades, nunca precios (RN-03).</summary>
public sealed record PreviewSaleRequest(int SessionId, IReadOnlyList<SaleLineRequest>? Lines, DiscountRequest? Discount);

public sealed record RegisterSaleRequest(
    int SessionId,
    IReadOnlyList<SaleLineRequest>? Lines,
    DiscountRequest? Discount,
    PaymentRequest? Payment);

/// <summary>
/// Controlador delgado: traduce HTTP en Commands y Queries, los envía por <see cref="ISender"/> y traduce el resultado
/// a HTTP. No nombra a ningún Handler (la conexión la hace el Mediator).
/// </summary>
[ApiController]
[Route("api/sales")]
public sealed class SalesController(ISender sender) : ControllerBase
{
    public const string IdempotencyHeader = "Idempotency-Key";

    /// <summary>RF-05 / RF-06: calcula la venta con precios de Odoo y la política de descuento, sin registrarla.</summary>
    [HttpPost("preview")]
    public async Task<ActionResult<SalePreview>> Preview([FromBody] PreviewSaleRequest request, CancellationToken cancellationToken)
    {
        var query = new PreviewSaleQuery(request.SessionId, request.Lines, request.Discount, User.ToOperator());
        var result = await sender.Send(query, cancellationToken);
        return result.IsSuccess ? result.Value : (ActionResult)result.ToProblem(HttpContext);
    }

    /// <summary>
    /// RF-09 a RF-12: confirma y registra la venta con su pago en Odoo.
    /// 201 = registrada y pagada; 200 = repetición de una venta ya registrada; 202 = resultado incierto.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<SaleRegistrationResult>> Register(
        [FromHeader(Name = IdempotencyHeader)] string? idempotencyKey,
        [FromBody] RegisterSaleRequest request,
        CancellationToken cancellationToken)
    {
        var key = Guid.TryParse(idempotencyKey, out var parsed) ? parsed : Guid.Empty;
        var command = new RegisterSaleCommand(key, request.SessionId, request.Lines, request.Discount, request.Payment, User.ToOperator());

        var result = await sender.Send(command, cancellationToken);
        if (result.IsFailure)
        {
            return (ActionResult)result.ToProblem(HttpContext);
        }

        var sale = result.Value;
        return sale switch
        {
            { Status: SaleRegistrationStatus.Uncertain } => AcceptedAtAction(nameof(Attempt), new { key = sale.SaleKey }, sale),
            { Replayed: true } => Ok(sale),
            _ => CreatedAtAction(nameof(Detail), new { id = sale.OrderId }, sale),
        };
    }

    /// <summary>
    /// RF-17: consulta en Odoo el estado real de una venta con resultado incierto. Es seguro repetirlo:
    /// nunca reenvía la venta, solo actualiza lo que el POS sabe de ella.
    /// </summary>
    [HttpGet("attempts/{key:guid}")]
    public async Task<ActionResult<SaleRegistrationResult>> Attempt(Guid key, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new VerifySaleAttemptCommand(key), cancellationToken);
        return result.IsSuccess ? result.Value : (ActionResult)result.ToProblem(HttpContext);
    }

    /// <summary>RF-13: historial de ventas registradas en Odoo.</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<OrderSummary>>> History(
        [FromQuery] string? search,
        [FromQuery] OrderStateFilter state,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetSalesHistoryQuery(search, state, from, to, page, pageSize), cancellationToken);
        return result.IsSuccess ? result.Value : (ActionResult)result.ToProblem(HttpContext);
    }

    /// <summary>RF-14: detalle de una venta.</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<OrderDetail>> Detail(int id, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetSaleDetailQuery(id), cancellationToken);
        return result.IsSuccess ? result.Value : (ActionResult)result.ToProblem(HttpContext);
    }
}
