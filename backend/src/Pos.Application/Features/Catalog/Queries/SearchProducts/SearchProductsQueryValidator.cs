using FluentValidation;
using Pos.Application.Common.Validation;

namespace Pos.Application.Features.Catalog.Queries.SearchProducts;

public sealed class SearchProductsQueryValidator : AbstractValidator<SearchProductsQuery>
{
    public SearchProductsQueryValidator()
    {
        RuleFor(q => q.Text)
            .MustHaveAtMost(SearchProductsQuery.MaxTextLength)
            .WithErrorCode("CATALOG.SEARCH_TOO_LONG")
            .WithMessage($"El texto de búsqueda admite como máximo {SearchProductsQuery.MaxTextLength} caracteres.")
            .OverridePropertyName("search");

        RuleFor(q => q.CategoryId)
            .Must(id => id is null or > 0)
            .WithErrorCode("CATALOG.INVALID_CATEGORY")
            .WithMessage("La categoría indicada no es válida.");

        RuleFor(q => q.Page).MustBeValidPage();
        RuleFor(q => q.PageSize).MustBeValidPageSize();
    }
}
