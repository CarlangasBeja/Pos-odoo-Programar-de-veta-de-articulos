using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Pos.Domain.Common;

namespace Pos.Api.Http;

/// <summary>
/// Traduce los errores de la aplicación a respuestas RFC 9457 (Problem Details) con mensajes en español.
/// Todas las respuestas de error de la API tienen la misma forma: el frontend las interpreta en un solo lugar.
/// </summary>
public static class ErrorResults
{
    public static IActionResult ToProblem(this IReadOnlyList<Error> errors, HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(context);

        var first = errors[0];
        var status = StatusFor(first.Kind);
        var problem = new ProblemDetails
        {
            Status = status,
            Title = TitleFor(first.Kind),
            Detail = first.Message,
            Instance = context.Request.Path,
            Type = $"https://httpstatuses.io/{status}",
        };
        problem.Extensions["code"] = first.Code;
        problem.Extensions["errors"] = errors.Select(e => new { code = e.Code, message = e.Message, target = e.Target }).ToArray();
        problem.Extensions["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier;

        return new ObjectResult(problem) { StatusCode = status, ContentTypes = { "application/problem+json" } };
    }

    public static IActionResult ToProblem(this Result result, HttpContext context) => result.Errors.ToProblem(context);

    public static int StatusFor(ErrorKind kind) => kind switch
    {
        ErrorKind.Validation => StatusCodes.Status400BadRequest,
        ErrorKind.BusinessRule => StatusCodes.Status422UnprocessableEntity,
        ErrorKind.NotFound => StatusCodes.Status404NotFound,
        ErrorKind.Conflict => StatusCodes.Status409Conflict,
        ErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
        ErrorKind.ExternalRejected => StatusCodes.Status422UnprocessableEntity,
        ErrorKind.ExternalUnavailable => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status500InternalServerError,
    };

    private static string TitleFor(ErrorKind kind) => kind switch
    {
        ErrorKind.Validation => "Datos inválidos",
        ErrorKind.BusinessRule => "La operación no cumple las reglas del negocio",
        ErrorKind.NotFound => "No encontrado",
        ErrorKind.Conflict => "Conflicto con otra operación",
        ErrorKind.Unauthorized => "No autenticado",
        ErrorKind.Forbidden => "Operación no autorizada",
        ErrorKind.ExternalRejected => "Odoo rechazó la operación",
        ErrorKind.ExternalUnavailable => "Odoo no está disponible",
        _ => "Error inesperado",
    };
}
