using MediatR;
using Pos.Application.Common;
using Pos.Application.Contracts.Gateways;
using Pos.Domain.Common;

namespace Pos.Application.Features.History.Queries.GetSalesHistory;

public sealed class GetSalesHistoryQueryHandler(IPosOrderGateway orders)
    : IRequestHandler<GetSalesHistoryQuery, Result<PagedResult<OrderSummary>>>
{
    public async Task<Result<PagedResult<OrderSummary>>> Handle(GetSalesHistoryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var page = PageRequest.Create(query.Page, query.PageSize, GetSalesHistoryQuery.DefaultPageSize);
        if (page.IsFailure)
        {
            return Result<PagedResult<OrderSummary>>.Failure(page.Errors);
        }

        var text = query.Text?.Trim();
        var criteria = new OrderSearchCriteria(string.IsNullOrEmpty(text) ? null : text, query.State, query.From, query.To, page.Value);
        return await orders.SearchAsync(criteria, cancellationToken);
    }
}
