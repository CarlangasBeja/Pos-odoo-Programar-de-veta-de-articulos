using MediatR;
using Pos.Application.Common;
using Pos.Domain.Common;

namespace Pos.Application.Features.History.Queries.GetSalesHistory;

/// <summary>RF-13: historial de ventas registradas en Odoo para el punto de venta configurado.</summary>
public sealed record GetSalesHistoryQuery(
    string? Text,
    OrderStateFilter State = OrderStateFilter.All,
    DateOnly? From = null,
    DateOnly? To = null,
    int? Page = null,
    int? PageSize = null) : IRequest<Result<PagedResult<OrderSummary>>>
{
    public const int DefaultPageSize = 20;
    public const int MaxRangeDays = 366;
    public const int MaxTextLength = 60;
}
