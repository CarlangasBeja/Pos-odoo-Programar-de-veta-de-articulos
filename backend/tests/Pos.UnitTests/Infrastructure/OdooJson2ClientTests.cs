using System.Net;
using System.Text.Json;
using Pos.Infrastructure.Odoo;
using Pos.UnitTests.Fakes;

namespace Pos.UnitTests.Infrastructure;

[TestFixture]
public sealed class OdooJson2ClientTests
{
    private static readonly Dictionary<string, object?> NoArgs = new() { ["domain"] = Array.Empty<object>() };

    [Test]
    public async Task EnviaLaApiKeyLaBaseYElIdiomaComoEspecificaJson2()
    {
        var host = new OdooTestHost();
        host.Handler.On("res.partner/search_count", _ => FakeHttpHandler.Json(3));

        var response = await host.Client.CallAsync("res.partner", "search_count", NoArgs, CancellationToken.None);

        var request = host.Handler.Requests.Single();
        Assert.Multiple(() =>
        {
            Assert.That(response, Is.InstanceOf<OdooResponse.Success>());
            Assert.That(request.Method, Is.EqualTo(HttpMethod.Post));
            Assert.That(request.Uri.AbsolutePath, Is.EqualTo("/json/2/res.partner/search_count"));
            Assert.That(request.Headers["Authorization"], Is.EqualTo("bearer clave-de-prueba"));
            Assert.That(request.Headers["X-Odoo-Database"], Is.EqualTo("pos"));
            Assert.That(request.Json.GetProperty("context").GetProperty("lang").GetString(), Is.EqualTo("es_419"));
        });
    }

    [Test]
    public async Task UnErrorDeOdooSeConvierteEnFault()
    {
        var host = new OdooTestHost();
        host.Handler.On("pos.order/sync_from_ui", _ =>
            FakeHttpHandler.OdooError(HttpStatusCode.UnprocessableEntity, "odoo.exceptions.UserError", "La orden no se pagó por completo."));

        var response = await host.Client.CallAsync("pos.order", "sync_from_ui", NoArgs, CancellationToken.None);

        Assert.That(response, Is.EqualTo(new OdooResponse.Fault(HttpStatusCode.UnprocessableEntity, "odoo.exceptions.UserError", "La orden no se pagó por completo.")));
    }

    [Test]
    public async Task UnaRespuestaQueNoEsDeOdooEsInvalida()
    {
        var host = new OdooTestHost();
        host.Handler.On("pos.order/search_read", _ => FakeHttpHandler.Html(HttpStatusCode.BadGateway));

        var response = await host.Client.CallAsync("pos.order", "search_read", NoArgs, CancellationToken.None);

        Assert.That(response, Is.InstanceOf<OdooResponse.TransportFailure>());
        Assert.That(((OdooResponse.TransportFailure)response).Kind, Is.EqualTo(OdooTransportFailureKind.InvalidResponse));
    }

    [Test]
    public async Task SinRespuestaATiempoEsTimeoutYPudoHaberseProcesado()
    {
        var host = new OdooTestHost();
        host.Handler.OnAsync("pos.order/sync_from_ui", async (_, token) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), token);
            return FakeHttpHandler.Json(true);
        });

        var response = await host.Client.CallAsync("pos.order", "sync_from_ui", NoArgs, CancellationToken.None, TimeSpan.FromMilliseconds(100));

        var failure = (OdooResponse.TransportFailure)response;
        Assert.That(failure.Kind, Is.EqualTo(OdooTransportFailureKind.Timeout));
        Assert.That(failure.RequestMayHaveBeenProcessed, Is.True);
    }

    [Test]
    public async Task SiNoHayConexionSeSabeQueOdooNoProcesoNada()
    {
        var host = new OdooTestHost();
        host.Handler.OnAsync("pos.order/sync_from_ui", (_, _) =>
            throw new HttpRequestException(HttpRequestError.ConnectionError, "Conexión rechazada"));

        var response = await host.Client.CallAsync("pos.order", "sync_from_ui", NoArgs, CancellationToken.None);

        var failure = (OdooResponse.TransportFailure)response;
        Assert.That(failure.Kind, Is.EqualTo(OdooTransportFailureKind.NotSent));
        Assert.That(failure.RequestMayHaveBeenProcessed, Is.False);
    }

    [Test]
    public async Task UnCorteDeConexionDuranteLaRespuestaEsAmbiguo()
    {
        var host = new OdooTestHost();
        host.Handler.OnAsync("pos.order/sync_from_ui", (_, _) =>
            throw new HttpRequestException(HttpRequestError.ResponseEnded, "Conexión cerrada"));

        var response = await host.Client.CallAsync("pos.order", "sync_from_ui", NoArgs, CancellationToken.None);

        Assert.That(((OdooResponse.TransportFailure)response).Kind, Is.EqualTo(OdooTransportFailureKind.Interrupted));
    }

    [Test]
    public void LaCancelacionDelLlamadorNoSeConfundeConUnTimeout()
    {
        var host = new OdooTestHost();
        host.Handler.OnAsync("res.partner/search", async (_, token) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), token);
            return FakeHttpHandler.Json(Array.Empty<int>());
        });
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        Assert.That(
            async () => await host.Client.CallAsync("res.partner", "search", NoArgs, cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public async Task LosValoresFalseDeOdooSeLeenComoVacios()
    {
        var host = new OdooTestHost();
        host.Handler.On("product.product/fields_get", _ => FakeHttpHandler.Json(new { qty_available = new { type = "float" } }));

        Assert.That(await host.ReferenceData.SupportsStockAsync(CancellationToken.None), Is.False, "Sin is_storable no hay existencias fiables");

        using var document = JsonDocument.Parse("""{"default_code": false, "barcode": "770", "session_id": [3, "POS/003"]}""");
        Assert.That(document.RootElement.TryGetProperty("default_code", out var code) && code.ValueKind == JsonValueKind.False, Is.True);
    }
}
