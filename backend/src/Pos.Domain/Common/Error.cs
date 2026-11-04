namespace Pos.Domain.Common;

/// <summary>
/// Error esperado (de validación, de negocio o de integración) con un código estable
/// que el frontend puede interpretar y un mensaje comprensible para el operador.
/// </summary>
public sealed record Error(string Code, string Message, ErrorKind Kind, string? Target = null)
{
    public static Error Validation(string code, string message, string? target = null) =>
        new(code, message, ErrorKind.Validation, target);

    public static Error Rule(string code, string message, string? target = null) =>
        new(code, message, ErrorKind.BusinessRule, target);

    public static Error NotFound(string code, string message, string? target = null) =>
        new(code, message, ErrorKind.NotFound, target);

    public static Error Conflict(string code, string message) =>
        new(code, message, ErrorKind.Conflict);
}

/// <summary>Clasificación del error; la capa de presentación la traduce a un código HTTP.</summary>
public enum ErrorKind
{
    Validation,
    BusinessRule,
    NotFound,
    Conflict,
    Unauthorized,
    Forbidden,
    ExternalRejected,
    ExternalUnavailable,
    Unexpected,
}
