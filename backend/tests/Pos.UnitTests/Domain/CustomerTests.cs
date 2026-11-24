using System.Net;
using Pos.Application.Contracts.Gateways;
using Pos.Application.Features.Sales;
using Pos.Application.Features.Sales.Commands.RegisterSale;
using Pos.Domain.Customers;
using Pos.Domain.Sales;
using Pos.Infrastructure.Odoo;
using Pos.UnitTests.Application;
using Pos.UnitTests.Fakes;
using Pos.UnitTests.Infrastructure;

namespace Pos.UnitTests.Domain;

/// <summary>Cliente de la venta: consumidor final o nombre + cédula, guardado en Odoo como partner_id.</summary>
[TestFixture]
public sealed class CustomerTests
{
    [TestCase("1710034065")]
    [TestCase("0926687856")]
    [TestCase("3050000003")]
    public void UnaCedulaConDigitoVerificadorCorrectoEsValida(string cedula) =>
        Assert.That(Cedula.IsValid(cedula), Is.True);

    [TestCase("1710034064", Description = "dígito verificador incorrecto")]
    [TestCase("171003406", Description = "9 dígitos")]
    [TestCase("17100340655", Description = "11 dígitos")]
    [TestCase("2510034065", Description = "provincia 25 no existe")]
    [TestCase("1770034065", Description = "tercer dígito 7")]
    [TestCase("17100340a5", Description = "letras")]
    [TestCase("", Description = "vacía")]
    [TestCase("2222222222", Description = "todos los dígitos iguales")]
    public void UnaCedulaMalFormadaOConVerificadorIncorrectoSeRechaza(string cedula) =>
        Assert.That(Cedula.IsValid(cedula), Is.False);

    [Test]
    public void ElClienteIdentificadoNormalizaElNombreYGuardaLaCedula()
    {
        var customer = Customer.Identified("  Ana   María  Pérez ", " 1710034065 ").Value;

        Assert.Multiple(() =>
        {
            Assert.That(customer.Name, Is.EqualTo("Ana María Pérez"));
            Assert.That(customer.Identification, Is.EqualTo("1710034065"));
            Assert.That(customer.IsFinalConsumer, Is.False);
            Assert.That(Customer.FinalConsumer.IsFinalConsumer, Is.True);
        });
    }

    [TestCase("Ana", CustomerErrors.NameRequiredCode, Description = "falta el apellido")]
    [TestCase("Ana 123", CustomerErrors.NameInvalidCharactersCode, Description = "números")]
    [TestCase("Ana <Pérez>", CustomerErrors.NameInvalidCharactersCode, Description = "símbolos")]
    public void ElNombreDebeTenerNombreYApellidoSoloConLetras(string name, string code) =>
        Assert.That(Customer.Identified(name, "1710034065").FirstError.Code, Is.EqualTo(code));

    [TestCase("María José O'Neil")]
    [TestCase("Juan Pérez-Ríos")]
    [TestCase("José de la Cruz")]
    public void AceptaNombresConTildesGuionYApostrofo(string name) =>
        Assert.That(Customer.Identified(name, "1710034065").IsSuccess, Is.True);

    [Test]
    public void SinNombreOConCedulaInvalidaNoSeCreaElCliente()
    {
        var result = Customer.Identified("A", "1710034064");

        Assert.That(result.Errors.Select(e => e.Code),
            Is.EquivalentTo(new[] { CustomerErrors.NameRequiredCode, CustomerErrors.InvalidCedulaCode }));
    }

    [Test]
    public void ElValidatorRevisaLaFormaDelClienteSoloCuandoViene()
    {
        var validator = new RegisterSaleCommandValidator();
        var finalConsumer = SalesFixture.TwoProductsCash();
        var badShape = finalConsumer with { Customer = new CustomerRequest("", "12ab") };

        Assert.Multiple(() =>
        {
            Assert.That(validator.Validate(finalConsumer).IsValid, Is.True, "Sin cliente = consumidor final");
            Assert.That(validator.Validate(badShape).Errors.Select(e => e.ErrorCode),
                Is.EquivalentTo(new[] { CustomerErrors.NameRequiredCode, CustomerErrors.InvalidCedulaCode }));
        });
    }

    [Test]
    public async Task LaVentaConClienteLlegaAOdooConEseCliente()
    {
        var fixture = new SalesFixture();
        var command = SalesFixture.TwoProductsCash() with { Customer = new CustomerRequest("Ana Pérez", "1710034065") };

        var result = await fixture.Register.Handle(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(fixture.Orders.Submitted.Single().Customer, Is.EqualTo(Customer.Identified("Ana Pérez", "1710034065").Value));
    }

    [Test]
    public async Task ConUnaCedulaInvalidaNoSeEnviaNadaAOdoo()
    {
        var fixture = new SalesFixture();
        var command = SalesFixture.TwoProductsCash() with { Customer = new CustomerRequest("Ana Pérez", "1710034064") };

        var result = await fixture.Register.Handle(command, CancellationToken.None);

        Assert.That(result.FirstError.Code, Is.EqualTo(CustomerErrors.InvalidCedulaCode));
        Assert.That(fixture.Orders.SubmitCount, Is.Zero);
    }

    [Test]
    public async Task SinClienteLaVentaEsAConsumidorFinal()
    {
        var fixture = new SalesFixture();

        await fixture.Register.Handle(SalesFixture.TwoProductsCash(), CancellationToken.None);

        Assert.That(fixture.Orders.Submitted.Single().Customer.IsFinalConsumer, Is.True);
    }

    [Test]
    public async Task ElClienteSeBuscaPorCedulaEnOdooYSeEnviaComoPartnerId()
    {
        var host = new OdooTestHost();
        host.Handler
            .On("res.partner/search_read", _ => FakeHttpHandler.Json(new object[] { new { id = 42 } }))
            .On("pos.order/sync_from_ui", _ => FakeHttpHandler.Json(new { }))
            .On("pos.order/search_read", _ => FakeHttpHandler.Json(OrderRecords.Paid()));

        await host.Orders.SubmitAsync(SaleFor(Customer.Identified("Ana Pérez", "1710034065").Value), TestData.Cajero, CancellationToken.None);

        var search = host.Handler.Requests[0].Json;
        var order = host.Handler.Requests.Single(r => r.Uri.AbsolutePath.EndsWith("sync_from_ui")).Json.GetProperty("orders")[0];
        Assert.Multiple(() =>
        {
            Assert.That(search.GetProperty("domain")[0][2].GetString(), Is.EqualTo("1710034065"));
            Assert.That(order.GetProperty("partner_id").GetInt32(), Is.EqualTo(42));
            Assert.That(host.Handler.Requests.Any(r => r.Uri.AbsolutePath.EndsWith("res.partner/create")), Is.False, "Ya existía: no se crea otro");
        });
    }

    [Test]
    public async Task UnClienteNuevoSeCreaEnOdooConNombreYCedula()
    {
        var host = new OdooTestHost();
        host.Handler
            .On("res.partner/search_read", _ => FakeHttpHandler.Json(Array.Empty<object>()))
            .On("res.partner/create", _ => FakeHttpHandler.Json(new[] { 51 }))
            .On("pos.order/sync_from_ui", _ => FakeHttpHandler.Json(new { }))
            .On("pos.order/search_read", _ => FakeHttpHandler.Json(OrderRecords.Paid()));

        await host.Orders.SubmitAsync(SaleFor(Customer.Identified("Ana Pérez", "1710034065").Value), TestData.Cajero, CancellationToken.None);

        var created = host.Handler.Requests.Single(r => r.Uri.AbsolutePath.EndsWith("res.partner/create")).Json.GetProperty("vals_list")[0];
        var order = host.Handler.Requests.Single(r => r.Uri.AbsolutePath.EndsWith("sync_from_ui")).Json.GetProperty("orders")[0];
        Assert.Multiple(() =>
        {
            Assert.That(created.GetProperty("name").GetString(), Is.EqualTo("Ana Pérez"));
            Assert.That(created.GetProperty("vat").GetString(), Is.EqualTo("1710034065"));
            Assert.That(order.GetProperty("partner_id").GetInt32(), Is.EqualTo(51));
        });
    }

    [Test]
    public async Task SinPermisoParaCrearClientesLaVentaNoSeEnviaYSePuedeReintentar()
    {
        var host = new OdooTestHost();
        host.Handler
            .On("res.partner/search_read", _ => FakeHttpHandler.Json(Array.Empty<object>()))
            .On("res.partner/create", _ => FakeHttpHandler.OdooError(HttpStatusCode.Forbidden, "odoo.exceptions.AccessError", "You are not allowed to create 'Contact'"));

        var outcome = await host.Orders.SubmitAsync(SaleFor(Customer.Identified("Ana Pérez", "1710034065").Value), TestData.Cajero, CancellationToken.None);

        var rejected = (OrderSubmissionOutcome.Rejected)outcome;
        Assert.Multiple(() =>
        {
            Assert.That(rejected.Error.Code, Is.EqualTo(OdooErrors.CustomerForbiddenCode));
            Assert.That(rejected.Error.Kind, Is.EqualTo(Pos.Domain.Common.ErrorKind.ExternalUnavailable), "La clave no se consume");
            Assert.That(host.Handler.Requests.Any(r => r.Uri.AbsolutePath.EndsWith("sync_from_ui")), Is.False);
        });
    }

    [Test]
    public async Task ConsumidorFinalNoConsultaClientesNiEnviaPartnerId()
    {
        var host = new OdooTestHost();
        host.Handler
            .On("pos.order/sync_from_ui", _ => FakeHttpHandler.Json(new { }))
            .On("pos.order/search_read", _ => FakeHttpHandler.Json(OrderRecords.Paid()));

        await host.Orders.SubmitAsync(SaleFor(Customer.FinalConsumer), TestData.Cajero, CancellationToken.None);

        var order = host.Handler.Requests[0].Json.GetProperty("orders")[0];
        Assert.That(order.TryGetProperty("partner_id", out _), Is.False);
    }

    [Test]
    public async Task ElDetalleDeLaOrdenTraeElClienteConSuCedula()
    {
        var host = new OdooTestHost();
        host.Handler
            .On("pos.order/search_read", _ => FakeHttpHandler.Json(OrderRecords.Paid(partner: new object[] { 42, "Ana Pérez" })))
            .On("pos.order.line/search_read", _ => FakeHttpHandler.Json(Array.Empty<object>()))
            .On("pos.payment/search_read", _ => FakeHttpHandler.Json(Array.Empty<object>()))
            .On("res.partner/search_read", _ => FakeHttpHandler.Json(new object[] { new { id = 42, vat = "1710034065" } }));

        var detail = (await host.Orders.GetDetailAsync(7, CancellationToken.None)).Value!;

        Assert.That(detail.Customer, Is.EqualTo(new Pos.Application.Features.History.OrderCustomer("Ana Pérez", "1710034065")));
    }

    private static Sale SaleFor(Customer customer)
    {
        var sale = Sale.Start(Guid.NewGuid(), TestData.OpenSession()).Value;
        sale.AddLine(TestData.Cafe(), Quantity.Create(1m).Value);
        sale.AssignCustomer(customer);
        sale.Confirm(TestData.Efectivo, 50m);
        return sale;
    }

    private static class OrderRecords
    {
        public static object Paid(object? partner = null) => new object[]
        {
            new Dictionary<string, object?>
            {
                ["id"] = 7, ["name"] = "POS Web .NET - 000007", ["pos_reference"] = "260-1-000007", ["date_order"] = "2026-11-24 13:40:08",
                ["state"] = "paid", ["amount_total"] = 11.9, ["amount_paid"] = 11.9, ["amount_return"] = 38.1, ["amount_tax"] = 1.9,
                ["session_id"] = new object[] { 1, "POS/00001" }, ["internal_note"] = false, ["uuid"] = Guid.NewGuid().ToString(),
                ["partner_id"] = partner ?? false,
            },
        };
    }
}
