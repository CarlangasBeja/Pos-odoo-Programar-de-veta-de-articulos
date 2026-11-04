using MediatR;
using Pos.Domain.Common;

namespace Pos.Application.Features.History.Queries.GetSaleDetail;

/// <summary>RF-14: detalle de una venta (líneas y pagos) tal como quedó en Odoo.</summary>
public sealed record GetSaleDetailQuery(int OrderId) : IRequest<Result<OrderDetail>>;
