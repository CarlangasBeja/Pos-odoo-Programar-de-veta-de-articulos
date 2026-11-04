using Pos.Domain.Common;
using Pos.Domain.Operators;

namespace Pos.Domain.Discounts;

public enum DiscountPolicyKind
{
    /// <summary>El operador indica el porcentaje (limitado por su rol).</summary>
    Manual,

    /// <summary>La política calcula el porcentaje a partir de la venta.</summary>
    Automatic,
}

/// <summary>Línea vista por las políticas de descuento: importe bruto (con impuestos) antes del descuento.</summary>
public sealed record DiscountableLine(int ProductId, string ProductName, decimal Quantity, decimal GrossAmount);

public sealed record DiscountContext(OperatorRole Role, IReadOnlyList<DiscountableLine> Lines, decimal? RequestedPercentage)
{
    public decimal GrossTotal => Lines.Sum(l => l.GrossAmount);
}

/// <summary>Resultado de una política: el porcentaje que corresponde a cada línea, en el mismo orden.</summary>
public sealed record DiscountDecision(string PolicyCode, string Description, IReadOnlyList<DiscountPercentage> LinePercentages)
{
    public const string NoneCode = "NONE";

    public static DiscountDecision None(int lineCount) =>
        new(NoneCode, "Sin descuento", Enumerable.Repeat(DiscountPercentage.Zero, lineCount).ToArray());

    public bool IsNone => LinePercentages.All(p => p.IsZero);
}

/// <summary>
/// Estrategia de descuento (patrón Strategy). Cada política encapsula su regla y su autorización,
/// de modo que agregar o cambiar una política no obliga a modificar los casos de uso (RF-07, P10).
/// </summary>
public interface IDiscountPolicy
{
    /// <summary>Código estable usado por el frontend y la configuración.</summary>
    string Code { get; }

    string Name { get; }

    string Description { get; }

    DiscountPolicyKind Kind { get; }

    bool IsAvailableFor(OperatorRole role);

    /// <summary>Porcentaje máximo que el rol puede solicitar (solo políticas manuales).</summary>
    decimal? MaxPercentageFor(OperatorRole role);

    Result<DiscountDecision> Evaluate(DiscountContext context);
}
