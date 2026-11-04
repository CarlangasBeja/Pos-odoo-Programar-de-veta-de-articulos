using MediatR;
using Pos.Application.Contracts.Gateways;
using Pos.Domain.Common;

namespace Pos.Application.Features.Catalog.Queries.GetProductCategories;

public sealed class GetProductCategoriesQueryHandler(IProductCatalog catalog)
    : IRequestHandler<GetProductCategoriesQuery, Result<IReadOnlyList<CategoryView>>>
{
    public async Task<Result<IReadOnlyList<CategoryView>>> Handle(GetProductCategoriesQuery query, CancellationToken cancellationToken)
    {
        var categories = await catalog.GetCategoriesAsync(cancellationToken);
        return categories.Map(list => (IReadOnlyList<CategoryView>)list.Select(c => new CategoryView(c.Id, c.Name)).ToArray());
    }
}
