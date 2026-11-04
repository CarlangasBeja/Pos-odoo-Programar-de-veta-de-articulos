namespace Pos.Domain.Operators;

/// <summary>Rol del operador del POS. Determina, entre otras cosas, el descuento máximo autorizado.</summary>
public enum OperatorRole
{
    Cashier,
    Supervisor,
}

public static class OperatorRoleNames
{
    public static string ToDisplayName(this OperatorRole role) => role switch
    {
        OperatorRole.Cashier => "Cajero",
        OperatorRole.Supervisor => "Supervisor",
        _ => role.ToString(),
    };
}
