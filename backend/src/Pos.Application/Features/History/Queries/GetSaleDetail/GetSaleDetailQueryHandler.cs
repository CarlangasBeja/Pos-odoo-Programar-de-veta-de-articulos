using MediatR;
using Pos.Application.Contracts.Gateways;
using Pos.Domain.Common;

namespace Pos.Application.Features.History.Queries.GetSaleDetail;

public sealed class GetSaleDetailQueryHandler(IPosOrderGateway orders) : IRequestHandler<GetSaleDetailQuery, Result<OrderDetail>>
{
    public async Task<Result<OrderDetail>> Handle(GetSaleDetailQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var detail = await orders.GetDetailAsync(query.OrderId, cancellationToken);
        if (detail.IsFailure)
        {
            return Result<OrderDetail>.Failure(detail.Errors);
        }

        return detail.Value is { } found
            ? found
            : Error.NotFound("HISTORY.NOT_FOUND", $"No existe una venta con el identificador {query.OrderId} en este punto de venta.");
    }
}
