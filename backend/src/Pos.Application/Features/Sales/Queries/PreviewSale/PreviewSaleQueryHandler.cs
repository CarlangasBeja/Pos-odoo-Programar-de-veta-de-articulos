using MediatR;
using Pos.Application.Contracts.Gateways;
using Pos.Domain.Common;

namespace Pos.Application.Features.Sales.Queries.PreviewSale;

/// <summary>El frontend la usa para mostrar el total verificado antes de cobrar.</summary>
public sealed class PreviewSaleQueryHandler(IPosSessionGateway sessions, SaleComposer composer)
    : IRequestHandler<PreviewSaleQuery, Result<SalePreview>>
{
    public async Task<Result<SalePreview>> Handle(PreviewSaleQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var session = await sessions.GetActiveSessionAsync(cancellationToken);
        if (session.IsFailure)
        {
            return Result<SalePreview>.Failure(session.Errors);
        }

        var sale = await composer.ComposeAsync(Guid.NewGuid(), session.Value.Session, query, query.Operator, cancellationToken);
        return sale.Map(SaleViewMapper.ToPreview);
    }
}
