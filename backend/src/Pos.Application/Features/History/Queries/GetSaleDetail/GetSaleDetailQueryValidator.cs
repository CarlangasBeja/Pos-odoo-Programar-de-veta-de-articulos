using FluentValidation;

namespace Pos.Application.Features.History.Queries.GetSaleDetail;

public sealed class GetSaleDetailQueryValidator : AbstractValidator<GetSaleDetailQuery>
{
    public GetSaleDetailQueryValidator()
    {
        RuleFor(q => q.OrderId)
            .GreaterThan(0)
            .WithErrorCode("HISTORY.INVALID_ID")
            .WithMessage("El identificador de la venta no es válido.")
            .OverridePropertyName("id");
    }
}
