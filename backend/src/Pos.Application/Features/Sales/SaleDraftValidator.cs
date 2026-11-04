using FluentValidation;

namespace Pos.Application.Features.Sales;

/// <summary>
/// Reglas de forma comunes a toda venta en preparación (vista previa y registro). Las reglas del negocio
/// (al menos un producto, cantidades mayores que cero, descuento autorizado) NO están aquí: viven en el dominio
/// (<c>Sale</c>, <c>Quantity</c>, políticas de descuento) para que haya una sola fuente de verdad.
/// </summary>
public abstract class SaleDraftValidator<TDraft> : AbstractValidator<TDraft>
    where TDraft : ISaleDraft
{
    protected SaleDraftValidator()
    {
        RuleFor(d => d.SessionId)
            .GreaterThan(0)
            .WithErrorCode("SALE.INVALID_SESSION")
            .WithMessage("Indique la sesión POS en la que se registra la venta.");

        RuleForEach(d => d.Lines!).SetValidator(new SaleLineRequestValidator());

        RuleFor(d => d.Discount!).SetValidator(new DiscountRequestValidator());
    }
}

public sealed class SaleLineRequestValidator : AbstractValidator<SaleLineRequest>
{
    public SaleLineRequestValidator()
    {
        RuleFor(l => l.ProductId)
            .GreaterThan(0)
            .WithErrorCode("SALE.INVALID_PRODUCT")
            .WithMessage("El identificador del producto no es válido.");
    }
}

public sealed class DiscountRequestValidator : AbstractValidator<DiscountRequest>
{
    public const int MaxPolicyCodeLength = 40;

    public DiscountRequestValidator()
    {
        RuleFor(d => d.PolicyCode)
            .Must(code => code is null || code.Length <= MaxPolicyCodeLength)
            .WithErrorCode("DISCOUNT.INVALID_POLICY")
            .WithMessage("El código de la política de descuento no es válido.");
    }
}
