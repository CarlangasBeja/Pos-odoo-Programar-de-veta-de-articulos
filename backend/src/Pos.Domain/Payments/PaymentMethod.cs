using Pos.Domain.Common;
using Pos.Domain.Money;

namespace Pos.Domain.Payments;

public enum PaymentMethodType
{
    Cash,
    Bank,
    Other,
}

/// <summary>Método de pago configurado en el punto de venta de Odoo (<c>pos.payment.method</c>).</summary>
public sealed record PaymentMethod(int Id, string Name, PaymentMethodType Type, bool UsesTerminal)
{
    /// <summary>Efectivo o banco sin terminal integrada; los demás requieren hardware o un cliente asociado.</summary>
    public bool IsSupported => (Type is PaymentMethodType.Cash or PaymentMethodType.Bank) && !UsesTerminal;

    /// <summary>Solo el efectivo admite entregar cambio.</summary>
    public bool AllowsChange => Type == PaymentMethodType.Cash;
}

/// <summary>Pago de la venta: lo recibido y el cambio a devolver.</summary>
public sealed record Payment(PaymentMethod Method, decimal AmountTendered, decimal Change)
{
    public static Result<Payment> Create(PaymentMethod method, decimal amountTendered, decimal total, Currency currency)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(currency);

        if (!method.IsSupported)
        {
            return PaymentErrors.MethodNotSupported(method);
        }

        if (amountTendered <= 0m || currency.Round(amountTendered) != amountTendered)
        {
            return PaymentErrors.InvalidAmount(amountTendered, currency);
        }

        if (amountTendered < total)
        {
            return PaymentErrors.Insufficient(amountTendered, total);
        }

        if (!method.AllowsChange && amountTendered != total)
        {
            return PaymentErrors.ExactAmountRequired(method, total);
        }

        return new Payment(method, amountTendered, amountTendered - total);
    }
}

public static class PaymentErrors
{
    public const string MethodNotFoundCode = "PAYMENT.METHOD_NOT_FOUND";
    public const string MethodNotSupportedCode = "PAYMENT.METHOD_NOT_SUPPORTED";
    public const string InvalidAmountCode = "PAYMENT.INVALID_AMOUNT";
    public const string InsufficientCode = "PAYMENT.INSUFFICIENT";
    public const string ExactAmountRequiredCode = "PAYMENT.EXACT_AMOUNT_REQUIRED";
    private const string Target = "payment";

    public static Error MethodNotFound(int methodId) => Error.Validation(
        MethodNotFoundCode,
        $"El método de pago {methodId} no está configurado en la sesión POS abierta.",
        Target);

    public static Error MethodNotSupported(PaymentMethod method) => Error.Rule(
        MethodNotSupportedCode,
        $"El método de pago «{method.Name}» no se puede usar desde este POS (requiere terminal o cliente).",
        Target);

    public static Error InvalidAmount(decimal amount, Currency currency) => Error.Validation(
        InvalidAmountCode,
        $"El monto recibido ({amount}) debe ser mayor que cero y tener como máximo {currency.DecimalPlaces} decimales.",
        Target);

    public static Error Insufficient(decimal tendered, decimal total) => Error.Rule(
        InsufficientCode,
        $"El monto recibido ({tendered:#,0.##}) es menor que el total de la venta ({total:#,0.##}).",
        Target);

    public static Error ExactAmountRequired(PaymentMethod method, decimal total) => Error.Rule(
        ExactAmountRequiredCode,
        $"Con «{method.Name}» el monto debe ser exactamente el total ({total:#,0.##}); solo el efectivo admite cambio.",
        Target);
}
