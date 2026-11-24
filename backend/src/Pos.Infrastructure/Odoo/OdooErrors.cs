using Pos.Domain.Common;

namespace Pos.Infrastructure.Odoo;

/// <summary>Traduce las fallas de Odoo a errores comprensibles, sin exponer detalles técnicos ni credenciales.</summary>
internal static class OdooErrors
{
    public const string UnavailableCode = "ODOO.UNAVAILABLE";
    public const string UnauthorizedCode = "ODOO.UNAUTHORIZED";
    public const string ForbiddenCode = "ODOO.FORBIDDEN";
    public const string RejectedCode = "ODOO.REJECTED";
    public const string UnexpectedResponseCode = "ODOO.UNEXPECTED_RESPONSE";
    public const string CustomerForbiddenCode = "ODOO.CUSTOMER_FORBIDDEN";
    private const int MaxOdooMessageLength = 300;

    public static Error From(OdooResponse response) => response switch
    {
        OdooResponse.TransportFailure => Unavailable(),
        OdooResponse.Fault { IsAuthenticationProblem: true } => Unauthorized(),
        OdooResponse.Fault { IsPermissionProblem: true } fault => Forbidden(fault),
        OdooResponse.Fault fault => Rejected(fault),
        _ => UnexpectedResponse(),
    };

    public static Error Unavailable() => new(
        UnavailableCode,
        "No fue posible comunicarse con Odoo. Verifique que el servidor esté en línea e intente de nuevo.",
        ErrorKind.ExternalUnavailable);

    public static Error Unauthorized() => new(
        UnauthorizedCode,
        "Odoo rechazó las credenciales de integración (API key inválida o vencida). Avise al administrador del sistema.",
        ErrorKind.ExternalUnavailable);

    public static Error Forbidden(OdooResponse.Fault fault) => new(
        ForbiddenCode,
        $"El usuario de integración no tiene permisos en Odoo para esta operación. Detalle: {Trim(fault.Message)}",
        ErrorKind.ExternalUnavailable);

    public static Error Rejected(OdooResponse.Fault fault) => new(
        RejectedCode,
        $"Odoo rechazó la operación: {Trim(fault.Message)}",
        ErrorKind.ExternalRejected);

    /// <summary>El usuario de integración no tiene el grupo "Contacto / Creación". La clave de la venta no se consume: se puede reintentar.</summary>
    public static Error CustomerCreationForbidden() => new(
        CustomerForbiddenCode,
        "Odoo no permite al usuario de integración crear clientes. Ejecute de nuevo odoo/setup.ps1 (agrega el permiso «Contacto / Creación») o venda a Consumidor final.",
        ErrorKind.ExternalUnavailable);

    public static Error UnexpectedResponse() => new(
        UnexpectedResponseCode,
        "Odoo devolvió una respuesta inesperada. Revise la versión y configuración de Odoo.",
        ErrorKind.ExternalUnavailable);

    private static string Trim(string message)
    {
        var singleLine = message.ReplaceLineEndings(" ").Trim();
        return singleLine.Length <= MaxOdooMessageLength ? singleLine : singleLine[..MaxOdooMessageLength] + "…";
    }
}
