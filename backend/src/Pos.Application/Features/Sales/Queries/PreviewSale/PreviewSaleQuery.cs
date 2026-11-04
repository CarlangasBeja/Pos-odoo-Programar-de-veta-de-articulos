using MediatR;
using Pos.Application.Common;
using Pos.Domain.Common;

namespace Pos.Application.Features.Sales.Queries.PreviewSale;

/// <summary>
/// RF-05 / RF-06 / RN-03 / RN-04: calcula la venta con precios de Odoo y la política de descuento autorizada,
/// sin registrarla. Es una Query: no cambia nada ni en Odoo ni en el POS.
/// </summary>
public sealed record PreviewSaleQuery(
    int SessionId,
    IReadOnlyList<SaleLineRequest>? Lines,
    DiscountRequest? Discount,
    OperatorIdentity Operator) : IRequest<Result<SalePreview>>, ISaleDraft;
