using System.Net;
using System.Text.Json;
using Pos.Application.Contracts.Gateways;
using Pos.Application.Features.Sales;
using Pos.Domain.Common;
using Pos.Domain.Sales;
using Pos.Infrastructure.Odoo;
using Pos.UnitTests.Fakes;

namespace Pos.UnitTests.Infrastructure;

/// <summary>El adaptador nunca concluye éxito sin leer la orden en Odoo (RN-07, P05) y su payload es idempotente (RN-08).</summary>
[TestFixture]
public sealed class OdooPosOrderGatewayTests
{
    private static readonly JsonSerializerOptions ReadableJson = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static Sale ConfirmedSale(Guid? key = null)
    {
        var sale = Sale.Start(key ?? Guid.NewGuid(), TestData.OpenSession()).Value;
        sale.AddLine(TestData.Cafe(), Quantity.Create(2m).Value);
        sale.AddLine(TestData.Agua(), Quantity.Create(1m).Value);
        sale.Confirm(TestData.Efectivo, 50m);
        return sale;
    }

    private static object OrderRecord(string state = "paid") => new object[]
    {
        new
        {
            id = 7,
            name = "POS Web .NET - 000007",
            pos_reference = "260-1-000007",
            date_order = "2026-10-29 21:40:08",
            state,
            amount_total = 29.75,
            amount_paid = 29.75,
            amount_return = 20.25,
            session_id = new object[] { 1, "POS/00001" },
            internal_note = false,
        },
    };

    [Test]
    public async Task LaVentaAceptadaSeVerificaLeyendoLaOrdenEnOdoo()
    {
        var host = new OdooTestHost();
        host.Handler
            .On("pos.order/sync_from_ui", _ => FakeHttpHandler.Json(new Dictionary<string, object> { ["pos.order"] = Array.Empty<object>() }))
            .On("pos.order/search_read", _ => FakeHttpHandler.Json(OrderRecord()));

        var outcome = await host.Orders.SubmitAsync(ConfirmedSale(), TestData.Cajero, CancellationToken.None);

        var accepted = (OrderSubmissionOutcome.Accepted)outcome;
        Assert.Multiple(() =>
        {
            Assert.That(accepted.Order.Id, Is.EqualTo(7));
            Assert.That(accepted.Order.State, Is.EqualTo(OrderState.Paid));
            Assert.That(accepted.Order.DateOrder, Is.EqualTo(new DateTimeOffset(2026, 10, 29, 21, 40, 8, TimeSpan.Zero)));
            Assert.That(host.Handler.Requests.Select(r => r.Uri.AbsolutePath),
                Is.EqualTo(new[] { "/json/2/pos.order/sync_from_ui", "/json/2/pos.order/search_read" }));
        });
    }

    [Test]
    public async Task P05_SiOdooRechazaYLaOrdenNoExisteElResultadoEsRechazo()
    {
        var host = new OdooTestHost();
        host.Handler
            .On("pos.order/sync_from_ui", _ => FakeHttpHandler.OdooError(HttpStatusCode.UnprocessableEntity, "odoo.exceptions.UserError", "La orden / no se pagó por completo."))
            .On("pos.order/search_read", _ => FakeHttpHandler.Json(Array.Empty<object>()));

        var outcome = await host.Orders.SubmitAsync(ConfirmedSale(), TestData.Cajero, CancellationToken.None);

        var rejected = (OrderSubmissionOutcome.Rejected)outcome;
        Assert.That(rejected.Error.Kind, Is.EqualTo(ErrorKind.ExternalRejected));
        Assert.That(rejected.Error.Message, Does.Contain("no se pagó por completo"));
    }

    [Test]
    public async Task P05_UnTimeoutSinOrdenEnOdooEsIncierto()
    {
        var host = new OdooTestHost(submitTimeoutSeconds: 0); // el registro agota su tiempo de espera de inmediato
        host.Handler
            .OnAsync("pos.order/sync_from_ui", async (_, token) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), token);
                return FakeHttpHandler.Json(true);
            })
            .On("pos.order/search_read", _ => FakeHttpHandler.Json(Array.Empty<object>()));

        var outcome = await host.Orders.SubmitAsync(ConfirmedSale(), TestData.Cajero, CancellationToken.None);

        Assert.That(outcome, Is.InstanceOf<OrderSubmissionOutcome.Unknown>());
    }

    [Test]
    public async Task UnTimeoutConLaOrdenYaCreadaEnOdooSeReconoceComoAceptada()
    {
        var host = new OdooTestHost(submitTimeoutSeconds: 0);
        host.Handler
            .OnAsync("pos.order/sync_from_ui", async (_, token) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), token);
                return FakeHttpHandler.Json(true);
            })
            .On("pos.order/search_read", _ => FakeHttpHandler.Json(OrderRecord()));

        var outcome = await host.Orders.SubmitAsync(ConfirmedSale(), TestData.Cajero, CancellationToken.None);

        Assert.That(outcome, Is.InstanceOf<OrderSubmissionOutcome.Accepted>());
    }

    [Test]
    public async Task SinConexionLaVentaNoLlegoAOdooYNoSeConsulta()
    {
        var host = new OdooTestHost();
        host.Handler.OnAsync("pos.order/sync_from_ui", (_, _) => throw new HttpRequestException(HttpRequestError.ConnectionError, "refused"));

        var outcome = await host.Orders.SubmitAsync(ConfirmedSale(), TestData.Cajero, CancellationToken.None);

        var rejected = (OrderSubmissionOutcome.Rejected)outcome;
        Assert.That(rejected.Error.Kind, Is.EqualTo(ErrorKind.ExternalUnavailable));
        Assert.That(host.Handler.Requests, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task UnaApiKeyInvalidaSeInformaComoProblemaDeConfiguracion()
    {
        var host = new OdooTestHost();
        host.Handler.On("pos.order/sync_from_ui", _ => FakeHttpHandler.OdooError(HttpStatusCode.Unauthorized, "werkzeug.exceptions.Unauthorized", "Invalid apikey"));

        var outcome = await host.Orders.SubmitAsync(ConfirmedSale(), TestData.Cajero, CancellationToken.None);

        Assert.That(((OrderSubmissionOutcome.Rejected)outcome).Error.Code, Is.EqualTo("ODOO.UNAUTHORIZED"));
    }

    [Test]
    public async Task ElPayloadSigueElContratoDeSyncFromUi()
    {
        var host = new OdooTestHost();
        var key = Guid.Parse("c33ed78e-aef6-4166-9326-c9cc85b45970");
        host.Handler
            .On("pos.order/sync_from_ui", _ => FakeHttpHandler.Json(new { }))
            .On("pos.order/search_read", _ => FakeHttpHandler.Json(OrderRecord()));

        await host.Orders.SubmitAsync(ConfirmedSale(key), TestData.Cajero, CancellationToken.None);

        var order = host.Handler.Requests[0].Json.GetProperty("orders")[0];
        var firstLine = order.GetProperty("lines")[0];
        var payment = order.GetProperty("payment_ids")[0];
        Assert.Multiple(() =>
        {
            Assert.That(order.GetProperty("uuid").GetString(), Is.EqualTo(key.ToString()), "La clave de idempotencia es el uuid de la orden");
            Assert.That(order.GetProperty("state").GetString(), Is.EqualTo("paid"));
            Assert.That(order.GetProperty("session_id").GetInt32(), Is.EqualTo(1));
            Assert.That(order.GetProperty("amount_total").GetDecimal(), Is.EqualTo(29.75m));
            Assert.That(order.GetProperty("amount_return").GetDecimal(), Is.EqualTo(-20.25m), "Odoo espera el cambio en negativo");
            Assert.That(firstLine[0].GetInt32(), Is.EqualTo(0), "Comando de creación (0, 0, valores)");
            Assert.That(firstLine[2].GetProperty("price_unit").GetDecimal(), Is.EqualTo(10m), "Precio leído de Odoo, no del cliente");
            Assert.That(firstLine[2].GetProperty("tax_ids")[0][2][0].GetInt32(), Is.EqualTo(TestData.Iva19.Id));
            Assert.That(payment[2].GetProperty("amount").GetDecimal(), Is.EqualTo(50m));
            Assert.That(payment[2].GetProperty("payment_method_id").GetInt32(), Is.EqualTo(TestData.Efectivo.Id));
        });
    }

    [Test]
    public void RN08_LosUuidDeLineasYPagosSonDeterministas()
    {
        var key = Guid.NewGuid();

        var first = PosOrderPayloadMapper.Map(ConfirmedSale(key), TestData.Cajero);
        var second = PosOrderPayloadMapper.Map(ConfirmedSale(key), TestData.Cajero);

        var firstJson = JsonSerializer.Serialize(first);
        var secondJson = JsonSerializer.Serialize(second);
        Assert.That(firstJson, Is.EqualTo(secondJson), "Un reenvío actualiza las mismas líneas en Odoo en vez de duplicarlas");
        Assert.That(PosOrderPayloadMapper.LineUuid(key, 0), Is.Not.EqualTo(PosOrderPayloadMapper.LineUuid(key, 1)));
        Assert.That(Guid.Parse(PosOrderPayloadMapper.LineUuid(key, 0)).Version, Is.EqualTo(5));
    }

    [Test]
    public void ElHistorialFiltraPorPuntoDeVentaTextoEstadoYFechaLocal()
    {
        var host = new OdooTestHost();
        var criteria = new Pos.Application.Features.History.OrderSearchCriteria(
            "agua",
            Pos.Application.Features.History.OrderStateFilter.Paid,
            new DateOnly(2026, 10, 29),
            new DateOnly(2026, 10, 29),
            Pos.Application.Common.PageRequest.Create(1, 20, 20).Value);

        var domain = JsonSerializer.Serialize(host.Orders.BuildHistoryDomain(criteria), ReadableJson);

        Assert.That(domain, Does.Contain("""["config_id","=",1]"""));
        Assert.That(domain, Does.Contain("""["lines.full_product_name","ilike","agua"]"""));
        Assert.That(domain, Does.Contain("""["state","=","paid"]"""));
        // Medianoche en Bogotá (UTC-5) = 05:00 UTC
        Assert.That(domain, Does.Contain("""["date_order",">=","2026-10-29 05:00:00"]"""));
        Assert.That(domain, Does.Contain("""["date_order","<","2026-10-30 05:00:00"]"""));
    }
}
