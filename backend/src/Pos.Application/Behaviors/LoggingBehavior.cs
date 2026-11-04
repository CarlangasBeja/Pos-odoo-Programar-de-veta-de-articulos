using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;
using Pos.Domain.Common;

namespace Pos.Application.Behaviors;

/// <summary>
/// Pipeline behavior transversal: registra cada solicitud con su nombre, su duración y cómo terminó.
/// Nunca registra el contenido de la solicitud (por ejemplo, la contraseña del inicio de sesión).
/// <para>
/// Se registra ANTES que <see cref="ValidationBehavior{TRequest,TResponse}"/>: al ser el más externo, también mide
/// y deja constancia de las solicitudes que la validación rechaza (pregunta (a) de la tarea del taller).
/// </para>
/// </summary>
public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        var requestName = typeof(TRequest).Name;
        var started = Stopwatch.GetTimestamp();
        try
        {
            var response = await next(cancellationToken);
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (response is Result { IsFailure: true } failure)
            {
                logger.LogInformation(
                    "{Request} rechazada en {ElapsedMs:F1} ms: {Codes}",
                    requestName,
                    elapsed,
                    string.Join(", ", failure.Errors.Select(e => e.Code).Distinct()));
            }
            else
            {
                logger.LogInformation("{Request} atendida en {ElapsedMs:F1} ms", requestName, elapsed);
            }

            return response;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                "{Request} terminó con una excepción después de {ElapsedMs:F1} ms",
                requestName,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            throw;
        }
    }
}
