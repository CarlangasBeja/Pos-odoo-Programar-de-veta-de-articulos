using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Pos.Api.Composition;

public static class WebHostExtensions
{
    /// <summary>Limita los intentos de inicio de sesión por dirección IP (protección ante fuerza bruta).</summary>
    public static IServiceCollection AddLoginRateLimiting(this IServiceCollection services, string policyName)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(policyName, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "desconocido",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            options.OnRejected = async (context, cancellationToken) =>
            {
                var problem = new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = "Demasiados intentos",
                    Detail = "Se superó el número de intentos de inicio de sesión. Espere un minuto e intente de nuevo.",
                };
                problem.Extensions["code"] = "AUTH.TOO_MANY_ATTEMPTS";
                await context.HttpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
            };
        });
        return services;
    }

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers.CacheControl = "no-store";
            await next();
        });

    /// <summary>Estado de la API y de la conexión con Odoo (público, sin datos sensibles).</summary>
    public static IEndpointConventionBuilder MapPosHealthChecks(this IEndpointRouteBuilder endpoints, string pattern) =>
        endpoints.MapHealthChecks(pattern, new HealthCheckOptions
        {
            ResultStatusCodes =
            {
                [HealthStatus.Healthy] = StatusCodes.Status200OK,
                [HealthStatus.Degraded] = StatusCodes.Status200OK,
                [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
            },
            ResponseWriter = async (context, report) =>
            {
                context.Response.ContentType = "application/json; charset=utf-8";
                var body = new
                {
                    status = report.Status.ToString().ToLowerInvariant(),
                    checks = report.Entries.ToDictionary(
                        e => e.Key,
                        e => new { status = e.Value.Status.ToString().ToLowerInvariant(), description = e.Value.Description }),
                };
                await context.Response.WriteAsync(JsonSerializer.Serialize(body));
            },
        }).AllowAnonymous();
}
