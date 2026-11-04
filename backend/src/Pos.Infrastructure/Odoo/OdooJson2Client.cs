using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Pos.Infrastructure.Odoo;

/// <summary>
/// Cliente mínimo de la API externa JSON-2 de Odoo 20: <c>POST /json/2/&lt;modelo&gt;/&lt;método&gt;</c>
/// con la API key en <c>Authorization: bearer</c> y la base en <c>X-Odoo-Database</c>.
/// Traduce HTTP a <see cref="OdooResponse"/> para que los adaptadores no manejen excepciones de red.
/// </summary>
public sealed class OdooJson2Client
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.General);

    private readonly HttpClient _http;
    private readonly OdooOptions _options;
    private readonly ILogger<OdooJson2Client> _logger;

    public OdooJson2Client(HttpClient http, IOptions<OdooOptions> options, ILogger<OdooJson2Client> logger)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);
        _http = http;
        _options = options.Value;
        _logger = logger;

        _http.BaseAddress ??= new Uri(_options.BaseUrl.TrimEnd('/') + "/");
    }

    public TimeSpan DefaultTimeout => TimeSpan.FromSeconds(_options.RequestTimeoutSeconds);

    public TimeSpan SubmitTimeout => TimeSpan.FromSeconds(_options.SubmitTimeoutSeconds);

    public async Task<OdooResponse> CallAsync(
        string model,
        string method,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null,
        IReadOnlyDictionary<string, object?>? context = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentNullException.ThrowIfNull(arguments);

        var body = new Dictionary<string, object?>(arguments)
        {
            ["context"] = BuildContext(context),
        };

        var json = JsonSerializer.Serialize(body, SerializerOptions);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"json/2/{model}/{method}")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

        // Evidencia de integración (P09): activar con Logging:LogLevel:Pos.Infrastructure.Odoo = Debug.
        // La API key viaja en el encabezado y nunca se registra.
        _logger.LogDebug("Odoo {Model}.{Method} solicitud: {Body}", model, method, json);
        request.Headers.Authorization = new AuthenticationHeaderValue("bearer", _options.ApiKey);
        request.Headers.Add("X-Odoo-Database", _options.Database);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout ?? DefaultTimeout);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeoutSource.Token);
            var payload = await response.Content.ReadAsStringAsync(timeoutSource.Token);
            _logger.LogDebug("Odoo {Model}.{Method} respuesta {StatusCode}: {Body}", model, method, (int)response.StatusCode, payload);
            var result = Interpret(response.StatusCode, payload);
            _logger.LogInformation(
                "Odoo {Model}.{Method} -> {StatusCode} en {ElapsedMs} ms",
                model,
                method,
                (int)response.StatusCode,
                stopwatch.ElapsedMilliseconds);
            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Odoo {Model}.{Method} sin respuesta tras {ElapsedMs} ms", model, method, stopwatch.ElapsedMilliseconds);
            return new OdooResponse.TransportFailure(OdooTransportFailureKind.Timeout, "Odoo no respondió dentro del tiempo de espera.");
        }
        catch (HttpRequestException exception)
        {
            var kind = exception.HttpRequestError is HttpRequestError.ConnectionError
                or HttpRequestError.NameResolutionError
                or HttpRequestError.SecureConnectionError
                ? OdooTransportFailureKind.NotSent
                : OdooTransportFailureKind.Interrupted;
            _logger.LogWarning(exception, "Odoo {Model}.{Method} falló a nivel de red ({Kind})", model, method, kind);
            return new OdooResponse.TransportFailure(kind, exception.Message);
        }
    }

    private Dictionary<string, object?> BuildContext(IReadOnlyDictionary<string, object?>? extra)
    {
        var context = new Dictionary<string, object?> { ["lang"] = _options.Language };
        if (extra is not null)
        {
            foreach (var (key, value) in extra)
            {
                context[key] = value;
            }
        }

        return context;
    }

    private static OdooResponse Interpret(HttpStatusCode status, string payload)
    {
        JsonDocument? document = null;
        try
        {
            document = string.IsNullOrWhiteSpace(payload) ? null : JsonDocument.Parse(payload);
        }
        catch (JsonException)
        {
            document = null;
        }

        using (document)
        {
            if ((int)status is >= 200 and < 300)
            {
                return document is null
                    ? new OdooResponse.TransportFailure(OdooTransportFailureKind.InvalidResponse, "Odoo devolvió una respuesta vacía o no JSON.")
                    : new OdooResponse.Success(document.RootElement.Clone());
            }

            if (document?.RootElement is { ValueKind: JsonValueKind.Object } error
                && error.TryGetProperty("name", out var name)
                && error.TryGetProperty("message", out var message))
            {
                return new OdooResponse.Fault(status, name.GetString() ?? "error", message.GetString() ?? string.Empty);
            }

            return new OdooResponse.TransportFailure(
                OdooTransportFailureKind.InvalidResponse,
                $"Respuesta HTTP {(int)status} sin el formato de error de Odoo.");
        }
    }
}
