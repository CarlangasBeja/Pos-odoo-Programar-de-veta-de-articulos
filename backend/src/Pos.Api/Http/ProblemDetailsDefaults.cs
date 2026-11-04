using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Pos.Api.Http;

/// <summary>Mensajes en español para las respuestas que genera el propio framework (401, 403, 404, 405...).</summary>
public static class ProblemDetailsDefaults
{
    public static void Customize(ProblemDetailsContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var problem = context.ProblemDetails;
        problem.Extensions.TryAdd("traceId", Activity.Current?.Id ?? context.HttpContext.TraceIdentifier);

        if (!string.IsNullOrEmpty(problem.Detail))
        {
            return;
        }

        (problem.Title, problem.Detail, var code) = problem.Status switch
        {
            StatusCodes.Status401Unauthorized => ("No autenticado", "Su sesión no es válida o expiró. Inicie sesión de nuevo.", "AUTH.UNAUTHENTICATED"),
            StatusCodes.Status403Forbidden => ("Operación no autorizada", "No tiene permisos para realizar esta operación.", "AUTH.FORBIDDEN"),
            StatusCodes.Status404NotFound => ("No encontrado", "El recurso solicitado no existe.", "HTTP.NOT_FOUND"),
            StatusCodes.Status405MethodNotAllowed => ("Método no permitido", "La operación no está disponible con ese método HTTP.", "HTTP.METHOD_NOT_ALLOWED"),
            StatusCodes.Status415UnsupportedMediaType => ("Formato no soportado", "Envíe el cuerpo de la solicitud como JSON.", "HTTP.UNSUPPORTED_MEDIA_TYPE"),
            _ => (problem.Title, "No se pudo completar la solicitud.", "HTTP.ERROR"),
        };
        problem.Extensions.TryAdd("code", code);
    }
}

public sealed record RequestError(string Code, string Message, string Target);

/// <summary>Respuesta 400 cuando el cuerpo o los parámetros no se pueden interpretar (JSON mal formado, tipos incorrectos).</summary>
public static class InvalidRequestResponse
{
    public static IActionResult Create(ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        // Primero los errores de campos concretos ("$.lines[0].quantity"), luego los generales del cuerpo.
        var errors = context.ModelState
            .Where(entry => entry.Value is { ValidationState: ModelValidationState.Invalid })
            .OrderBy(entry => entry.Key.StartsWith("$.", StringComparison.Ordinal) ? 0 : 1)
            .Select(entry => Describe(entry.Key))
            .DistinctBy(error => error.Message)
            .ToArray();

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Datos inválidos",
            Detail = errors.Length > 0 ? errors[0].Message : "La solicitud no es válida.",
            Instance = context.HttpContext.Request.Path,
        };
        problem.Extensions["code"] = "REQUEST.INVALID";
        problem.Extensions["errors"] = errors;
        problem.Extensions["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
        return new BadRequestObjectResult(problem) { ContentTypes = { "application/problem+json" } };
    }

    private static RequestError Describe(string key)
    {
        if (key.StartsWith("$.", StringComparison.Ordinal))
        {
            var field = key[2..];
            return new RequestError("REQUEST.INVALID_VALUE", $"El campo «{field}» tiene un valor con formato incorrecto.", field);
        }

        if (key.StartsWith('$') || key is "request" or "")
        {
            return new RequestError("REQUEST.INVALID_BODY", "El cuerpo de la solicitud no es un JSON válido o le faltan datos.", "body");
        }

        return new RequestError("REQUEST.INVALID_VALUE", $"El parámetro «{key}» tiene un valor no válido.", key);
    }
}

/// <summary>Errores no previstos: se registran con detalle y al cliente solo se le da un mensaje claro y un traceId.</summary>
public sealed class UnexpectedExceptionHandler(IProblemDetailsService problemDetails, ILogger<UnexpectedExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            return true; // el cliente cerró la conexión; no hay a quién responder
        }

        logger.LogError(exception, "Error no controlado en {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "Error inesperado",
            Detail = "Ocurrió un error inesperado en el servidor. Intente de nuevo; si persiste, contacte al administrador.",
        };
        problem.Extensions["code"] = "SERVER.UNEXPECTED";
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = problem, Exception = exception });
    }
}
