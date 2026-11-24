using FluentValidation;
using Pos.Domain.Customers;

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
        // Sin cliente = consumidor final: el validador hijo solo se aplica cuando viene el cliente.
        RuleFor(c => c.Customer!).SetValidator(new CustomerRequestValidator());
    }
}

/// <summary>Forma de los datos del cliente. El dígito verificador de la cédula lo valida el dominio (<c>Customer.Identified</c>).</summary>
public sealed class CustomerRequestValidator : AbstractValidator<CustomerRequest>
{
    public CustomerRequestValidator()
    {
        RuleFor(c => c.Name)
            .NotEmpty()
            .WithErrorCode(CustomerErrors.NameRequiredCode)
            .WithMessage(CustomerErrors.NameRequired().Message)
            .Must(name => name is null || name.Trim().Length <= Customer.NameMaxLength)
            .WithErrorCode(CustomerErrors.NameTooLongCode)
            .WithMessage(CustomerErrors.NameTooLong().Message);

        RuleFor(c => c.Identification)
            .Must(id => id?.Trim() is { Length: Cedula.Length } digits && digits.All(char.IsAsciiDigit))
            .WithErrorCode(CustomerErrors.InvalidCedulaCode)
            .WithMessage(CustomerErrors.InvalidCedula().Message);
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
