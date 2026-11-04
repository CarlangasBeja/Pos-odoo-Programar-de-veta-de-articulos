using MediatR;
using Pos.Application.Common;
using Pos.Application.Contracts.Gateways;
using Pos.Domain.Common;

namespace Pos.Application.Features.Catalog.Queries.SearchProducts;

public sealed class SearchProductsQueryHandler(IProductCatalog catalog, IPosSessionGateway sessions)
    : IRequestHandler<SearchProductsQuery, Result<PagedResult<ProductView>>>
{
    public async Task<Result<PagedResult<ProductView>>> Handle(SearchProductsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var page = PageRequest.Create(query.Page, query.PageSize, SearchProductsQuery.DefaultPageSize);
        if (page.IsFailure)
        {
            return Result<PagedResult<ProductView>>.Failure(page.Errors);
        }

        var pricing = await sessions.GetPricingSettingsAsync(cancellationToken);
        if (pricing.IsFailure)
        {
            return Result<PagedResult<ProductView>>.Failure(pricing.Errors);
        }

        var text = query.Text?.Trim();
        var criteria = new ProductSearchCriteria(
            string.IsNullOrEmpty(text) ? null : text,
            query.CategoryId,
            query.OnlyInStock,
            query.Sort,
            page.Value);

        var products = await catalog.SearchAsync(criteria, cancellationToken);
        return products.Map(result => result.Map(p => ProductViewMapper.ToView(p, pricing.Value)));
    }
}
