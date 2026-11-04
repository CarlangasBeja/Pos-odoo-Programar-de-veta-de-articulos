using Pos.Application.Contracts.Persistence;
using Pos.Application.Contracts.Security;
using Pos.Application.Features.Auth.Commands.Login;
using Pos.Application.Features.Catalog.Queries.GetProductCategories;
using Pos.Application.Features.Catalog.Queries.SearchProducts;
using Pos.Application.Features.Discounts.Queries.GetDiscountPolicies;
using Pos.Application.Features.History.Queries.GetSaleDetail;
using Pos.Application.Features.History.Queries.GetSalesHistory;
using Pos.Application.Features.Sales;
using Pos.Application.Features.Sales.Commands.VerifySaleAttempt;
using Pos.Application.Features.Sales.Queries.PreviewSale;
using Pos.Application.Features.Sessions.Queries.GetActiveSession;
using Pos.Domain.Common;
using Pos.Domain.Operators;
using Pos.Infrastructure.Security;
using Pos.UnitTests.Fakes;

namespace Pos.UnitTests.Application;

// Pruebas de los Handlers con Pure DI: se crean con `new` y dobles de prueba, sin contenedor ni Mediator.

[TestFixture]
public sealed class PreviewSaleQueryHandlerTests
{
    [Test]
    public async Task CalculaLaVentaConPreciosDeOdooSinRegistrarla()
    {
        var fixture = new SalesFixture();
        var query = new PreviewSaleQuery(
            1,
            [new SaleLineRequest(14, 2m), new SaleLineRequest(16, 1m)],
            new DiscountRequest("MANUAL", 10m),
            TestData.Cajero);

        var result = await fixture.Preview.Handle(query, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        var preview = result.Value;
        Assert.Multiple(() =>
        {
            Assert.That(preview.GrossBeforeDiscount, Is.EqualTo(29.75m));
            Assert.That(preview.Total, Is.EqualTo(26.78m));
            Assert.That(preview.Discount!.Amount, Is.EqualTo(2.97m));
            Assert.That(preview.Lines.Select(l => l.UnitPrice), Is.EqualTo(new[] { 10m, 5.95m }), "Los precios salen del catálogo de Odoo (RN-03)");
            Assert.That(fixture.Orders.SubmitCount, Is.Zero);
        });
    }

    [Test]
    public async Task ElDescuentoPorVolumenSeAplicaSoloALaLineaQueCalifica()
    {
        var fixture = new SalesFixture();
        var query = new PreviewSaleQuery(
            1,
            [new SaleLineRequest(21, 12m), new SaleLineRequest(14, 1m)],
            new DiscountRequest("VOLUMEN", null),
            TestData.Cajero);

        var preview = (await fixture.Preview.Handle(query, CancellationToken.None)).Value;

        Assert.That(preview.Lines.Select(l => l.DiscountPercent), Is.EqualTo(new[] { 5m, 0m }));
    }
}

[TestFixture]
public sealed class VerifySaleAttemptCommandHandlerTests
{
    [Test]
    public async Task RF17_ConciliaUnaVentaInciertaQueOdooSiRegistro()
    {
        var fixture = new SalesFixture();
        var command = SalesFixture.TwoProductsCash();
        fixture.Orders.TimesOutNext(butOdooRegisteredIt: true);
        await fixture.Register.Handle(command, CancellationToken.None);

        var checkedResult = await fixture.Verify.Handle(new VerifySaleAttemptCommand(command.IdempotencyKey), CancellationToken.None);

        Assert.That(checkedResult.Value.Status, Is.EqualTo(SaleRegistrationStatus.Registered));
        Assert.That(fixture.Attempts.Attempts[command.IdempotencyKey].Status, Is.EqualTo(SaleAttemptStatus.Registered));
        Assert.That(fixture.Orders.SubmitCount, Is.EqualTo(1));
    }

    [Test]
    public async Task SiOdooNoTieneLaVentaSigueInciertaYLoExplica()
    {
        var fixture = new SalesFixture();
        var command = SalesFixture.TwoProductsCash();
        fixture.Orders.TimesOutNext();
        await fixture.Register.Handle(command, CancellationToken.None);

        var checkedResult = await fixture.Verify.Handle(new VerifySaleAttemptCommand(command.IdempotencyKey), CancellationToken.None);

        Assert.That(checkedResult.Value.Status, Is.EqualTo(SaleRegistrationStatus.Uncertain));
        Assert.That(checkedResult.Value.Message, Is.EqualTo(VerifySaleAttemptCommandHandler.StillUnknownMessage));
    }

    [Test]
    public async Task SiOdooTampocoRespondeALaConsultaSeInformaElError()
    {
        var fixture = new SalesFixture();
        var command = SalesFixture.TwoProductsCash();
        fixture.Orders.TimesOutNext();
        await fixture.Register.Handle(command, CancellationToken.None);
        fixture.Orders.LookupFailsWith = new Error("ODOO.UNAVAILABLE", "Odoo no responde", ErrorKind.ExternalUnavailable);

        var checkedResult = await fixture.Verify.Handle(new VerifySaleAttemptCommand(command.IdempotencyKey), CancellationToken.None);

        Assert.That(checkedResult.FirstError.Code, Is.EqualTo("ODOO.UNAVAILABLE"));
        Assert.That(fixture.Attempts.IsLocked(command.IdempotencyKey), Is.False);
    }

    [Test]
    public async Task UnaClaveDesconocidaNoExiste()
    {
        var fixture = new SalesFixture();

        var result = await fixture.Verify.Handle(new VerifySaleAttemptCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.That(result.FirstError.Code, Is.EqualTo(IdempotencyErrors.AttemptNotFoundCode));
    }
}

[TestFixture]
public sealed class CatalogQueryHandlerTests
{
    [Test]
    public async Task DevuelveElPrecioConImpuestosYLosMotivosDeNoVenta()
    {
        var catalog = new FakeProductCatalog(TestData.Cafe(), TestData.NoDisponibleEnPos());
        var handler = new SearchProductsQueryHandler(catalog, new FakePosSessionGateway(TestData.OpenSession()));

        var result = await handler.Handle(new SearchProductsQuery(null, null), CancellationToken.None);

        var cafe = result.Value.Items.Single(p => p.Id == 14);
        var discontinued = result.Value.Items.Single(p => p.Id == 24);
        Assert.That(cafe.PriceWithTaxes, Is.EqualTo(11.90m));
        Assert.That(cafe.Sellable, Is.True);
        Assert.That(discontinued.Sellable, Is.False);
        Assert.That(discontinued.NotSellableReasons, Has.Some.Contains("no está habilitado"));
    }

    [Test]
    public async Task EnviaElTextoSinEspaciosYLaPaginaPorDefecto()
    {
        var catalog = new FakeProductCatalog(TestData.Cafe());
        var handler = new SearchProductsQueryHandler(catalog, new FakePosSessionGateway(TestData.OpenSession()));

        await handler.Handle(new SearchProductsQuery("  café  ", null), CancellationToken.None);

        Assert.That(catalog.LastCriteria!.Text, Is.EqualTo("café"));
        Assert.That(catalog.LastCriteria.Page.PageSize, Is.EqualTo(SearchProductsQuery.DefaultPageSize));
    }

    [Test]
    public async Task PropagaElErrorDeOdooSinOcultarlo()
    {
        var catalog = new FakeProductCatalog { FailWith = new Error("ODOO.UNAVAILABLE", "Odoo no responde", ErrorKind.ExternalUnavailable) };
        var handler = new SearchProductsQueryHandler(catalog, new FakePosSessionGateway(TestData.OpenSession()));

        var result = await handler.Handle(new SearchProductsQuery("café", null), CancellationToken.None);

        Assert.That(result.FirstError.Kind, Is.EqualTo(ErrorKind.ExternalUnavailable));
    }

    [Test]
    public async Task LasCategoriasSalenDelCatalogo()
    {
        var handler = new GetProductCategoriesQueryHandler(new FakeProductCatalog(TestData.Cafe()));

        var result = await handler.Handle(new GetProductCategoriesQuery(), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.Not.Empty);
    }
}

[TestFixture]
public sealed class SessionAndDiscountQueryHandlerTests
{
    [Test]
    public async Task UnaSesionCerradaSeInformaComoNoAptaParaVender()
    {
        var handler = new GetActiveSessionQueryHandler(new FakePosSessionGateway(TestData.ClosedSession()));

        var result = await handler.Handle(new GetActiveSessionQuery(), CancellationToken.None);

        Assert.That(result.Value.AcceptsSales, Is.False);
        Assert.That(result.Value.Issues, Is.Not.Empty);
    }

    [Test]
    public async Task ElCajeroVeSuTopeDeDescuentoManual()
    {
        var fixture = new SalesFixture();
        var handler = new GetDiscountPoliciesQueryHandler(fixture.Policies);

        var result = await handler.Handle(new GetDiscountPoliciesQuery(TestData.Cajero), CancellationToken.None);

        Assert.That(result.Value.Single(p => p.Code == "MANUAL").MaxPercentage, Is.EqualTo(10m));
    }
}

[TestFixture]
public sealed class HistoryQueryHandlerTests
{
    [Test]
    public async Task ElHistorialUsaLaPaginaPorDefecto()
    {
        var orders = new FakePosOrderGateway();
        var handler = new GetSalesHistoryQueryHandler(orders);

        var result = await handler.Handle(new GetSalesHistoryQuery(" POS "), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(orders.LastSearch!.Text, Is.EqualTo("POS"));
        Assert.That(orders.LastSearch.Page.PageSize, Is.EqualTo(GetSalesHistoryQuery.DefaultPageSize));
    }

    [Test]
    public async Task ElDetalleDeUnaVentaInexistenteEsNoEncontrado()
    {
        var handler = new GetSaleDetailQueryHandler(new FakePosOrderGateway());

        var result = await handler.Handle(new GetSaleDetailQuery(12345), CancellationToken.None);

        Assert.That(result.FirstError.Kind, Is.EqualTo(ErrorKind.NotFound));
    }
}

[TestFixture]
public sealed class LoginCommandHandlerTests
{
    private static readonly Pbkdf2PasswordHasher Hasher = new();
    private static readonly string CashierHash = Hasher.Hash("Cajero2026!");

    private sealed class Directory(params OperatorAccount[] accounts) : IOperatorDirectory
    {
        public Task<OperatorAccount?> FindByUsernameAsync(string username, CancellationToken cancellationToken) =>
            Task.FromResult(accounts.FirstOrDefault(a => a.Username.Equals(username, StringComparison.OrdinalIgnoreCase)));
    }

    private static LoginCommandHandler Handler(bool active = true) =>
        new(new Directory(new OperatorAccount("cajero", "Ana", OperatorRole.Cashier, CashierHash, active)), Hasher);

    [Test]
    public async Task IdentificaAlOperadorConSuRol()
    {
        var result = await Handler().Handle(new LoginCommand(" CAJERO ", "Cajero2026!"), CancellationToken.None);

        Assert.That(result.Value.Role, Is.EqualTo(OperatorRole.Cashier));
        Assert.That(result.Value.DisplayName, Is.EqualTo("Ana"));
    }

    [TestCase("cajero", "incorrecta")]
    [TestCase("noexiste", "Cajero2026!")]
    public async Task ElMensajeEsElMismoParaUsuarioInexistenteOContrasenaIncorrecta(string user, string password)
    {
        var result = await Handler().Handle(new LoginCommand(user, password), CancellationToken.None);

        Assert.That(result.FirstError.Code, Is.EqualTo(LoginCommandHandler.InvalidCredentialsCode));
        Assert.That(result.FirstError.Message, Is.EqualTo("Usuario o contraseña incorrectos."));
    }

    [Test]
    public async Task UnOperadorInactivoNoPuedeIngresar() =>
        Assert.That((await Handler(active: false).Handle(new LoginCommand("cajero", "Cajero2026!"), CancellationToken.None)).IsFailure, Is.True);

    [Test]
    public void ElComandoNoMuestraLaContrasenaAlConvertirseEnTexto() =>
        Assert.That(new LoginCommand("cajero", "Cajero2026!").ToString(), Does.Not.Contain("Cajero2026!"));
}
