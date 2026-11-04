namespace Pos.Domain.Sales;

/// <summary>
/// Ciclo de vida de la venta. Se modela como máquina de estados con transiciones explícitas
/// (ver <see cref="SaleStateMachine"/>); el patrón State no aporta valor con tan poco comportamiento por estado.
/// </summary>
public enum SaleStatus
{
    /// <summary>En preparación: se pueden agregar productos y descuentos.</summary>
    Draft,

    /// <summary>Validada y lista para enviarse a Odoo; ya no se edita (RN-06).</summary>
    Confirmed,

    /// <summary>Odoo confirmó la orden y el pago (RN-07).</summary>
    Registered,

    /// <summary>Odoo rechazó la orden; no quedó registrada.</summary>
    Rejected,

    /// <summary>No hubo confirmación (timeout o error de red): no se afirma éxito ni fracaso (RF-17).</summary>
    Uncertain,
}

public static class SaleStateMachine
{
    private static readonly Dictionary<SaleStatus, SaleStatus[]> AllowedTransitions = new()
    {
        [SaleStatus.Draft] = [SaleStatus.Confirmed],
        [SaleStatus.Confirmed] = [SaleStatus.Registered, SaleStatus.Rejected, SaleStatus.Uncertain],
        [SaleStatus.Uncertain] = [SaleStatus.Registered, SaleStatus.Rejected, SaleStatus.Uncertain],
        [SaleStatus.Registered] = [],
        [SaleStatus.Rejected] = [],
    };

    public static bool CanTransition(SaleStatus from, SaleStatus to) =>
        AllowedTransitions.TryGetValue(from, out var targets) && targets.Contains(to);
}
