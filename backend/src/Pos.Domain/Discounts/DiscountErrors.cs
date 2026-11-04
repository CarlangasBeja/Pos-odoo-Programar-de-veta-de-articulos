using Pos.Domain.Common;
using Pos.Domain.Operators;

namespace Pos.Domain.Discounts;

public static class DiscountErrors
{
    public const string NotAuthorizedCode = "DISCOUNT.NOT_AUTHORIZED";
    public const string NotApplicableCode = "DISCOUNT.NOT_APPLICABLE";
    public const string UnknownPolicyCode = "DISCOUNT.UNKNOWN_POLICY";
    public const string InvalidPercentageCode = "DISCOUNT.INVALID_PERCENTAGE";
    private const string Target = "discount";

    public static Error PercentageOutOfRange(decimal value) => Error.Validation(
        InvalidPercentageCode,
        $"El porcentaje de descuento debe estar entre 0 y 100 (se recibió {value:0.##}).",
        Target);

    public static Error PercentageTooPrecise(decimal value) => Error.Validation(
        InvalidPercentageCode,
        $"El porcentaje de descuento admite como máximo dos decimales (se recibió {value}).",
        Target);

    public static Error PercentageRequired() => Error.Validation(
        InvalidPercentageCode,
        "Indique un porcentaje de descuento mayor que cero.",
        Target);

    public static Error UnknownPolicy(string code) => Error.Validation(
        UnknownPolicyCode,
        $"La política de descuento «{code}» no existe o no está habilitada.",
        Target);

    public static Error RoleNotAllowed(string policyName, OperatorRole role) => new(
        NotAuthorizedCode,
        $"El rol {role.ToDisplayName()} no está autorizado para aplicar «{policyName}».",
        ErrorKind.Forbidden,
        Target);

    public static Error ExceedsRoleLimit(decimal requested, decimal max, OperatorRole role) => new(
        NotAuthorizedCode,
        $"Descuento no autorizado: el rol {role.ToDisplayName()} puede aplicar como máximo {max:0.##} % "
        + $"y se solicitó {requested:0.##} %. Pida a un supervisor que registre la venta.",
        ErrorKind.Forbidden,
        Target);

    public static Error NotApplicable(string reason) => Error.Rule(NotApplicableCode, reason, Target);
}
