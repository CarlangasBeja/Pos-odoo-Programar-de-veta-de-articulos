using FluentValidation;

namespace Pos.Application.Features.Sales.Commands.RegisterSale;

public sealed class RegisterSaleCommandValidator : SaleDraftValidator<RegisterSaleCommand>
{
    public RegisterSaleCommandValidator()
    {
        RuleFor(c => c.IdempotencyKey)
            .Must(key => key != Guid.Empty)
            .WithErrorCode(IdempotencyErrors.KeyRequiredCode)
            .WithMessage(IdempotencyErrors.KeyRequired().Message)
            .OverridePropertyName("Idempotency-Key");

        RuleFor(c => c.Payment)
            .NotNull()
            .WithErrorCode(IdempotencyErrors.PaymentRequiredCode)
            .WithMessage(IdempotencyErrors.PaymentRequired().Message);

        RuleFor(c => c.Payment!).SetValidator(new PaymentRequestValidator());
    }
}

public sealed class PaymentRequestValidator : AbstractValidator<PaymentRequest>
{
    public PaymentRequestValidator()
    {
        RuleFor(p => p.PaymentMethodId)
            .GreaterThan(0)
            .WithErrorCode("PAYMENT.INVALID_METHOD")
            .WithMessage("Elija un método de pago válido.");
    }
}
