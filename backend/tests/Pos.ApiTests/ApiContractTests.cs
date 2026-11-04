using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Pos.Domain.Common;
using Pos.UnitTests.Fakes;

namespace Pos.ApiTests;

/// <summary>Contrato HTTP de la API: autenticación, códigos de estado y formato único de errores (RNF-08).</summary>
[TestFixture]
[NonParallelizable]
public sealed class ApiContractTests
{
    private ApiTestServer _server = null!;
    private HttpClient _cashier = null!;

    private static readonly object TwoProducts = new
    {
        sessionId = 1,
        lines = new[] { new { productId = 14, quantity = 2 }, new { productId = 16, quantity = 1 } },
    };

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        _server = await ApiTestServer.StartAsync();
        _cashier = await _server.LoginAsync();
    }

    [OneTimeTearDown]
    public async Task StopAsync()
    {
        _cashier.Dispose();
        await _server.DisposeAsync();
    }

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private static HttpRequestMessage SaleRequest(Guid key, object body) => new(HttpMethod.Post, "/api/sales")
    {
        Headers = { { "Idempotency-Key", key.ToString() } },
        Content = JsonContent.Create(body),
    };

    private static object CashSale(decimal tendered = 50m, object? discount = null) => new
    {
        sessionId = 1,
        lines = new[] { new { productId = 14, quantity = 2 }, new { productId = 16, quantity = 1 } },
        discount,
        payment = new { paymentMethodId = 2, amountTendered = tendered },
    };

    [Test]
    public async Task SinTokenLaApiRespondeNoAutenticadoEnEspanol()
    {
        var response = await _server.Client.GetAsync("/api/products");
        var body = await BodyAsync(response);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(body.GetProperty("detail").GetString(), Does.Contain("Inicie sesión"));
        Assert.That(body.GetProperty("code").GetString(), Is.EqualTo("AUTH.UNAUTHENTICATED"));
    }

    [Test]
    public async Task CredencialesIncorrectasDevuelven401ConMensajeComprensible()
    {
        var response = await _server.Client.PostAsJsonAsync("/api/auth/login", new { username = "cajero", password = "mala" });
        var body = await BodyAsync(response);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(body.GetProperty("detail").GetString(), Is.EqualTo("Usuario o contraseña incorrectos."));
    }

    [Test]
    public async Task ElTokenIdentificaAlOperadorYSuRol()
    {
        var me = await BodyAsync(await _cashier.GetAsync("/api/auth/me"));

        Assert.That(me.GetProperty("displayName").GetString(), Is.EqualTo("Ana Cajera"));
        Assert.That(me.GetProperty("role").GetString(), Is.EqualTo("cashier"));
        Assert.That(me.GetProperty("roleLabel").GetString(), Is.EqualTo("Cajero"));
    }

    [Test]
    public async Task ElCatalogoDevuelvePrecioConImpuestos()
    {
        var page = await BodyAsync(await _cashier.GetAsync("/api/products?search=caf&pageSize=5"));

        var cafe = page.GetProperty("items")[0];
        Assert.That(cafe.GetProperty("name").GetString(), Is.EqualTo("Café americano"));
        Assert.That(cafe.GetProperty("priceWithTaxes").GetDecimal(), Is.EqualTo(11.90m));
        Assert.That(page.GetProperty("totalCount").GetInt32(), Is.EqualTo(1));
    }

    [Test]
    public async Task LaVistaPreviaCalculaElTotalVerificado()
    {
        var response = await _cashier.PostAsJsonAsync("/api/sales/preview", TwoProducts);
        var preview = await BodyAsync(response);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(preview.GetProperty("total").GetDecimal(), Is.EqualTo(29.75m));
    }

    [Test]
    public async Task UnaVentaRegistradaDevuelve201YLaRepeticion200SinDuplicar()
    {
        var key = Guid.NewGuid();
        var before = _server.Orders.SubmitCount;

        var created = await _cashier.SendAsync(SaleRequest(key, CashSale()));
        var replayed = await _cashier.SendAsync(SaleRequest(key, CashSale()));
        var first = await BodyAsync(created);
        var second = await BodyAsync(replayed);

        Assert.Multiple(() =>
        {
            Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(created.Headers.Location!.AbsolutePath, Is.EqualTo($"/api/sales/{first.GetProperty("orderId").GetInt32()}"));
            Assert.That(first.GetProperty("status").GetString(), Is.EqualTo("registered"));
            Assert.That(first.GetProperty("change").GetDecimal(), Is.EqualTo(20.25m));
            Assert.That(replayed.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(second.GetProperty("replayed").GetBoolean(), Is.True);
            Assert.That(second.GetProperty("orderId").GetInt32(), Is.EqualTo(first.GetProperty("orderId").GetInt32()));
            Assert.That(_server.Orders.SubmitCount - before, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task UnResultadoInciertoDevuelve202ConLaRutaParaVerificar()
    {
        var key = Guid.NewGuid();
        _server.Orders.TimesOutNext();

        var response = await _cashier.SendAsync(SaleRequest(key, CashSale()));
        var body = await BodyAsync(response);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
        Assert.That(body.GetProperty("status").GetString(), Is.EqualTo("uncertain"));
        Assert.That(response.Headers.Location!.AbsolutePath, Is.EqualTo($"/api/sales/attempts/{key}"));
    }

    [Test]
    public async Task SinClaveDeIdempotenciaLaVentaSeRechaza()
    {
        var response = await _cashier.PostAsJsonAsync("/api/sales", CashSale());
        var body = await BodyAsync(response);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(body.GetProperty("code").GetString(), Is.EqualTo("IDEMPOTENCY.KEY_REQUIRED"));
    }

    [Test]
    public async Task ElValidatorDelPipelineRechazaLaFormaConElCampoDelJson()
    {
        // Lo rechaza ValidationBehavior antes del Handler: mismo formato de error que el resto de la API.
        var response = await _cashier.SendAsync(SaleRequest(Guid.NewGuid(), new
        {
            sessionId = 1,
            lines = new[] { new { productId = 0, quantity = 1 } },
            payment = new { paymentMethodId = 0, amountTendered = 10 },
        }));
        var body = await BodyAsync(response);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var errors = body.GetProperty("errors").EnumerateArray()
            .Select(e => (e.GetProperty("code").GetString(), e.GetProperty("target").GetString()))
            .ToArray();
        Assert.That(errors, Is.EquivalentTo(new[]
        {
            ("SALE.INVALID_PRODUCT", "lines[0].productId"),
            ("PAYMENT.INVALID_METHOD", "payment.paymentMethodId"),
        }));
        Assert.That(_server.Orders.SubmitCount, Is.Zero);
    }

    [Test]
    public async Task UnaPaginaFueraDeRangoDevuelve400()
    {
        var response = await _cashier.GetAsync("/api/products?pageSize=500");
        var body = await BodyAsync(response);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(body.GetProperty("code").GetString(), Is.EqualTo("PAGING.INVALID_PAGE_SIZE"));
        Assert.That(body.GetProperty("errors")[0].GetProperty("target").GetString(), Is.EqualTo("pageSize"));
    }

    [Test]
    public async Task UnJsonMalFormadoDevuelve400ConElCampoProblematico()
    {
        using var content = new StringContent("""{"sessionId": "uno", "lines": []}""", Encoding.UTF8, "application/json");

        var response = await _cashier.PostAsync("/api/sales/preview", content);
        var body = await BodyAsync(response);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(body.GetProperty("code").GetString(), Is.EqualTo("REQUEST.INVALID"));
        Assert.That(body.GetProperty("detail").GetString(), Does.Contain("sessionId"));
    }

    [Test]
    public async Task UnDescuentoNoAutorizadoDevuelve403()
    {
        var response = await _cashier.SendAsync(SaleRequest(Guid.NewGuid(), CashSale(discount: new { policyCode = "MANUAL", percentage = 25 })));
        var body = await BodyAsync(response);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(body.GetProperty("code").GetString(), Is.EqualTo("DISCOUNT.NOT_AUTHORIZED"));
        Assert.That(body.GetProperty("errors")[0].GetProperty("target").GetString(), Is.EqualTo("discount"));
    }

    [Test]
    public async Task UnSupervisorPuedeAutorizarUnDescuentoMayor()
    {
        using var supervisor = await _server.LoginAsync("supervisor", ApiTestServer.SupervisorPassword);

        var response = await supervisor.SendAsync(SaleRequest(Guid.NewGuid(), CashSale(discount: new { policyCode = "MANUAL", percentage = 25 })));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
    }

    [Test]
    public async Task VariosErroresDeValidacionSeInformanJuntos()
    {
        var response = await _cashier.PostAsJsonAsync("/api/sales/preview", new
        {
            sessionId = 1,
            lines = new[] { new { productId = 14, quantity = 0 }, new { productId = 16, quantity = -1 } },
        });
        var body = await BodyAsync(response);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(body.GetProperty("errors").GetArrayLength(), Is.EqualTo(2));
    }

    [Test]
    public async Task SiOdooNoEstaDisponibleSeResponde503()
    {
        _server.Sessions.FailWith = new Error("ODOO.UNAVAILABLE", "No fue posible comunicarse con Odoo.", ErrorKind.ExternalUnavailable);
        try
        {
            var response = await _cashier.GetAsync("/api/pos/sessions/active");
            var body = await BodyAsync(response);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
            Assert.That(body.GetProperty("title").GetString(), Is.EqualTo("Odoo no está disponible"));
        }
        finally
        {
            _server.Sessions.FailWith = null;
        }
    }

    [Test]
    public async Task UnaSesionCerradaImpideVenderCon422()
    {
        _server.Sessions.Session = TestData.ClosedSession();
        try
        {
            var response = await _cashier.SendAsync(SaleRequest(Guid.NewGuid(), CashSale()));
            var body = await BodyAsync(response);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
            Assert.That(body.GetProperty("code").GetString(), Is.EqualTo("SESSION.NOT_OPEN"));
        }
        finally
        {
            _server.Sessions.Session = TestData.OpenSession();
        }
    }

    [Test]
    public async Task ElEstadoDeSaludInformaQueOdooNoResponde()
    {
        var response = await _server.Client.GetAsync("/api/health");
        var body = await BodyAsync(response);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        Assert.That(body.GetProperty("checks").GetProperty("odoo").GetProperty("description").GetString(), Does.Contain("No fue posible"));
    }

    [Test]
    public async Task LasRespuestasIncluyenEncabezadosDeSeguridad()
    {
        var response = await _server.Client.GetAsync("/api/health");

        Assert.That(response.Headers.GetValues("X-Content-Type-Options"), Has.Member("nosniff"));
        Assert.That(response.Headers.GetValues("X-Frame-Options"), Has.Member("DENY"));
    }
}
