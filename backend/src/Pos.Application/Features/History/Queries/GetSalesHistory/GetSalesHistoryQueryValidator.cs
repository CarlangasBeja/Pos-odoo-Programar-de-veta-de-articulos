using FluentValidation;
using Pos.Application.Common.Validation;

namespace Pos.Application.Features.History.Queries.GetSalesHistory;

public sealed class GetSalesHistoryQueryValidator : AbstractValidator<GetSalesHistoryQuery>
{
    public GetSalesHistoryQueryValidator()
    {
        RuleFor(q => q.Text)
            .MustHaveAtMost(GetSalesHistoryQuery.MaxTextLength)
            .WithErrorCode("HISTORY.SEARCH_TOO_LONG")
            .WithMessage($"La búsqueda admite como máximo {GetSalesHistoryQuery.MaxTextLength} caracteres.")
            .OverridePropertyName("search");

        RuleFor(q => q.From)
            .Must((query, from) => !(from > query.To))
            .WithErrorCode("HISTORY.INVALID_RANGE")
            .WithMessage("La fecha inicial no puede ser posterior a la final.");

        RuleFor(q => q.To)
            .Must((query, to) => query.From is not { } from || to is not { } end || from > end || end.DayNumber - from.DayNumber <= GetSalesHistoryQuery.MaxRangeDays)
            .WithErrorCode("HISTORY.RANGE_TOO_LONG")
            .WithMessage($"El rango de fechas no puede superar {GetSalesHistoryQuery.MaxRangeDays} días.");

        RuleFor(q => q.Page).MustBeValidPage();
        RuleFor(q => q.PageSize).MustBeValidPageSize();
    }
}
