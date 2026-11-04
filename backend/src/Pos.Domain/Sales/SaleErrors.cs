using Pos.Domain.Common;

namespace Pos.Domain.Sales;

public static class SaleErrors
{
    public const string EmptyCode = "SALE.EMPTY";
    public const string InvalidQuantityCode = "SALE.INVALID_QUANTITY";
    public const string NotEditableCode = "SALE.NOT_EDITABLE";
    public const string TooManyLinesCode = "SALE.TOO_MANY_LINES";
    public const string TotalNotPositiveCode = "SALE.TOTAL_NOT_POSITIVE";
    public const string InvalidTransitionCode = "SALE.INVALID_TRANSITION";
    public const string DiscountMismatchCode = "SALE.DISCOUNT_MISMATCH";

    public static Error Empty() => Error.Validation(
        EmptyCode,
        "La venta debe contener al menos un producto.",
        "lines");

    public static Error QuantityNotPositive(decimal value, string? target) => Error.Validation(
        InvalidQuantityCode,
        $"La cantidad debe ser mayor que cero (se recibió {value:0.###}).",
        target ?? "quantity");

    public static Error QuantityTooLarge(decimal value, decimal max, string? target) => Error.Validation(
        InvalidQuantityCode,
        $"La cantidad {value:0.###} supera el máximo permitido por línea ({max:#,0}).",
        target ?? "quantity");

    public static Error QuantityTooPrecise(decimal value, int decimals, string? target) => Error.Validation(
        InvalidQuantityCode,
        $"La cantidad {value} admite como máximo {decimals} decimales.",
        target ?? "quantity");

    public static Error TooManyLines(int max) => Error.Validation(
        TooManyLinesCode,
        $"La venta no puede tener más de {max} productos distintos.",
        "lines");

    public static Error NotEditable(SaleStatus status) => Error.Rule(
        NotEditableCode,
        $"La venta ya está {Describe(status)} y no puede modificarse como una venta en preparación.");

    public static Error TotalNotPositive() => Error.Rule(
        TotalNotPositiveCode,
        "El total de la venta debe ser mayor que cero.");

    public static Error InvalidTransition(SaleStatus from, SaleStatus to) => Error.Rule(
        InvalidTransitionCode,
        $"Una venta {Describe(from)} no puede pasar a {Describe(to)}.");

    public static Error DiscountMismatch() => Error.Rule(
        DiscountMismatchCode,
        "El descuento calculado no corresponde con las líneas de la venta.");

    public static string Describe(SaleStatus status) => status switch
    {
        SaleStatus.Draft => "en preparación",
        SaleStatus.Confirmed => "confirmada",
        SaleStatus.Registered => "registrada en Odoo",
        SaleStatus.Rejected => "rechazada",
        SaleStatus.Uncertain => "con resultado incierto",
        _ => status.ToString(),
    };
}
