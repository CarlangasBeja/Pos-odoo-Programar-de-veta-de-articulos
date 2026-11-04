using MediatR;
using Pos.Application.Common;
using Pos.Domain.Common;

namespace Pos.Application.Features.Catalog.Queries.SearchProducts;

/// <summary>RF-01 / RF-02: buscar productos de Odoo por nombre, código o código de barras.</summary>
public sealed record SearchProductsQuery(
    string? Text,
    int? CategoryId,
    bool OnlyInStock = false,
    ProductSort Sort = ProductSort.NameAsc,
    int? Page = null,
    int? PageSize = null) : IRequest<Result<PagedResult<ProductView>>>
{
    public const int DefaultPageSize = 24;
    public const int MaxTextLength = 80;
}
