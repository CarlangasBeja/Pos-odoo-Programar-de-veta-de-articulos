using System.Net;
using System.Text;
using System.Text.Json;

namespace Pos.UnitTests.Fakes;

/// <summary>Servidor HTTP simulado: responde según el método JSON-2 invocado (/json/2/modelo/método).</summary>
public sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly List<Func<HttpRequestMessage, string, CancellationToken, Task<HttpResponseMessage>?>> _routes = [];

    public List<RecordedRequest> Requests { get; } = [];

    /// <param name="modelMethod">Modelo y método separados por barra, por ejemplo "pos.order/sync_from_ui".</param>
    public FakeHttpHandler On(string modelMethod, Func<JsonElement, HttpResponseMessage> respond) =>
        OnAsync(modelMethod, (body, _) => Task.FromResult(respond(body)));

    /// <summary>Respuesta asíncrona: permite simular demoras (timeouts) o cortes de conexión lanzando excepciones.</summary>
    public FakeHttpHandler OnAsync(string modelMethod, Func<JsonElement, CancellationToken, Task<HttpResponseMessage>> respond)
    {
        var path = PathFor(modelMethod);
        _routes.Add((request, body, cancellationToken) =>
        {
            if (!request.RequestUri!.AbsolutePath.EndsWith(path, StringComparison.Ordinal))
            {
                return null;
            }

            using var document = JsonDocument.Parse(body);
            return respond(document.RootElement.Clone(), cancellationToken);
        });
        return this;
    }

    public static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };

    public static HttpResponseMessage OdooError(HttpStatusCode status, string name, string message) =>
        Json(new { name, message, arguments = new[] { message }, context = new { }, debug = "" }, status);

    public static HttpResponseMessage Html(HttpStatusCode status) =>
        new(status) { Content = new StringContent("<html><body>Bad gateway</body></html>", Encoding.UTF8, "text/html") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "{}" : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value)), body));

        foreach (var route in _routes)
        {
            var response = route(request, body, cancellationToken);
            if (response is not null)
            {
                return await response.WaitAsync(cancellationToken);
            }
        }

        return OdooError(HttpStatusCode.NotFound, "werkzeug.exceptions.NotFound", $"Ruta no simulada: {request.RequestUri}");
    }

    private static string PathFor(string modelMethod)
    {
        var separator = modelMethod.LastIndexOf('/');
        return $"/json/2/{modelMethod[..separator]}/{modelMethod[(separator + 1)..]}";
    }

    public sealed record RecordedRequest(HttpMethod Method, Uri Uri, IReadOnlyDictionary<string, string> Headers, string Body)
    {
        public JsonElement Json => JsonDocument.Parse(Body).RootElement.Clone();
    }
}
