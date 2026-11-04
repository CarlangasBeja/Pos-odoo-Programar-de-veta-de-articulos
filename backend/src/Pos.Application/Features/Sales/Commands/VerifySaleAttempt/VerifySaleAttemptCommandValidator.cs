using FluentValidation;

namespace Pos.Application.Features.Sales.Commands.VerifySaleAttempt;

public sealed class VerifySaleAttemptCommandValidator : AbstractValidator<VerifySaleAttemptCommand>
{
    public VerifySaleAttemptCommandValidator()
    {
        RuleFor(c => c.SaleKey)
            .Must(key => key != Guid.Empty)
            .WithErrorCode(IdempotencyErrors.KeyRequiredCode)
            .WithMessage("Indique la clave de la venta que quiere verificar.")
            .OverridePropertyName("key");
    }
}
