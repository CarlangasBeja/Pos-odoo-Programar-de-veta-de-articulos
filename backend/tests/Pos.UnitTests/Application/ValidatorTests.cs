using FluentValidation;
using Pos.Application.Behaviors;
using Pos.Application.Features.Auth.Commands.Login;
using Pos.Application.Features.Catalog.Queries.SearchProducts;
using Pos.Application.Features.History.Queries.GetSaleDetail;
using Pos.Application.Features.History.Queries.GetSalesHistory;
using Pos.Application.Features.Sales;
using Pos.Application.Features.Sales.Commands.RegisterSale;
using Pos.Application.Features.Sales.Commands.VerifySaleAttempt;
using Pos.Application.Features.Sales.Queries.PreviewSale;
using Pos.UnitTests.Fakes;

namespace Pos.UnitTests.Application;

/// <summary>
/// Los Validators revisan la FORMA de cada solicitud (campos presentes, identificadores, longitudes, paginación).
/// Se prueban solos, sin contenedor: son clases comunes.
/// </summary>
[TestFixture]
public sealed class ValidatorTests
{
    private static string[] Codes<T>(IValidator<T> validator, T request) =>
        validator.Validate(request).Errors.Select(e => e.ErrorCode).ToArray();

    [Test]
    public void Login_ExigeUsuarioYContrasena()
    {
        var errors = new LoginCommandValidator().Validate(new LoginCommand("", null)).Errors;

        Assert.That(errors.Select(e => e.ErrorCode), Is.EquivalentTo(new[] { "AUTH.USERNAME_REQUIRED", "AUTH.PASSWORD_REQUIRED" }));
        Assert.That(errors.Select(e => e.ErrorMessage), Is.EquivalentTo(new[] { "Ingrese su usuario.", "Ingrese su contraseña." }));
    }

    [Test]
    public void Login_RechazaCamposDemasiadoLargos() =>
        Assert.That(
            Codes(new LoginCommandValidator(), new LoginCommand(new string('a', LoginCommand.MaxFieldLength + 1), "x")),
            Is.EqualTo(new[] { "AUTH.FIELD_TOO_LONG" }));

    [Test]
    public void Login_AceptaCredencialesConForma() =>
        Assert.That(new LoginCommandValidator().Validate(new LoginCommand("cajero", "Cajero2026!")).IsValid, Is.True);

    [Test]
    public void BuscarProductos_ValidaTextoCategoriaYPaginacion()
    {
        var validator = new SearchProductsQueryValidator();

        Assert.Multiple(() =>
        {
            Assert.That(Codes(validator, new SearchProductsQuery(new string('x', 81), null)), Is.EqualTo(new[] { "CATALOG.SEARCH_TOO_LONG" }));
            Assert.That(Codes(validator, new SearchProductsQuery(null, 0)), Is.EqualTo(new[] { "CATALOG.INVALID_CATEGORY" }));
            Assert.That(Codes(validator, new SearchProductsQuery(null, null, Page: 0)), Is.EqualTo(new[] { "PAGING.INVALID_PAGE" }));
            Assert.That(Codes(validator, new SearchProductsQuery(null, null, PageSize: 500)), Is.EqualTo(new[] { "PAGING.INVALID_PAGE_SIZE" }));
            Assert.That(validator.Validate(new SearchProductsQuery($"  {new string('x', 80)}  ", 3, Page: 2, PageSize: 100)).IsValid, Is.True);
        });
    }

    [Test]
    public void BuscarProductos_ElCampoAfectadoEsElParametroDeLaUrl() =>
        Assert.That(
            new SearchProductsQueryValidator().Validate(new SearchProductsQuery(new string('x', 81), null)).Errors.Single().PropertyName,
            Is.EqualTo("search"));

    [Test]
    public void Historial_ValidaElRangoDeFechas()
    {
        var validator = new GetSalesHistoryQueryValidator();

        Assert.Multiple(() =>
        {
            Assert.That(
                Codes(validator, new GetSalesHistoryQuery(null, From: new DateOnly(2026, 10, 30), To: new DateOnly(2026, 10, 1))),
                Is.EqualTo(new[] { "HISTORY.INVALID_RANGE" }));
            Assert.That(
                Codes(validator, new GetSalesHistoryQuery(null, From: new DateOnly(2025, 1, 1), To: new DateOnly(2026, 10, 1))),
                Is.EqualTo(new[] { "HISTORY.RANGE_TOO_LONG" }));
            Assert.That(validator.Validate(new GetSalesHistoryQuery(null, From: new DateOnly(2026, 10, 1), To: new DateOnly(2026, 10, 1))).IsValid, Is.True);
            Assert.That(validator.Validate(new GetSalesHistoryQuery(null, From: new DateOnly(2026, 10, 1))).IsValid, Is.True);
        });
    }

    [Test]
    public void DetalleDeVenta_ElIdentificadorDebeSerPositivo() =>
        Assert.That(Codes(new GetSaleDetailQueryValidator(), new GetSaleDetailQuery(0)), Is.EqualTo(new[] { "HISTORY.INVALID_ID" }));

    [Test]
    public void RegistrarVenta_ExigeClaveDeIdempotenciaYPago()
    {
        var command = SalesFixture.TwoProductsCash() with { IdempotencyKey = Guid.Empty, Payment = null };

        var errors = new RegisterSaleCommandValidator().Validate(command).Errors;

        Assert.That(errors.Select(e => e.ErrorCode), Is.EquivalentTo(new[] { IdempotencyErrors.KeyRequiredCode, IdempotencyErrors.PaymentRequiredCode }));
        Assert.That(errors.Single(e => e.ErrorCode == IdempotencyErrors.KeyRequiredCode).PropertyName, Is.EqualTo("Idempotency-Key"));
    }

    [Test]
    public void RegistrarVenta_RevisaLaFormaDeLineasDescuentoPagoYSesion()
    {
        var command = SalesFixture.TwoProductsCash(
            discount: new DiscountRequest(new string('X', 41), null),
            sessionId: 0,
            paymentMethodId: 0) with
        {
            Lines = [new SaleLineRequest(14, 1m), new SaleLineRequest(0, 1m)],
        };

        var errors = new RegisterSaleCommandValidator().Validate(command).Errors.ToDictionary(e => e.ErrorCode, e => e.PropertyName);

        Assert.That(errors, Is.EquivalentTo(new Dictionary<string, string>
        {
            ["SALE.INVALID_SESSION"] = "SessionId",
            ["SALE.INVALID_PRODUCT"] = "Lines[1].ProductId",
            ["DISCOUNT.INVALID_POLICY"] = "Discount.PolicyCode",
            ["PAYMENT.INVALID_METHOD"] = "Payment.PaymentMethodId",
        }));
    }

    [Test]
    public void RegistrarVenta_LasReglasDelNegocioNoSeDuplicanEnElValidator()
    {
        // RN-01 (al menos un producto) y RN-02 (cantidad mayor que cero) son del dominio: el Validator las deja pasar
        // y las rechaza Sale/Quantity, con una sola fuente de verdad (ver RegisterSaleCommandHandlerTests.P01 y P02).
        var validator = new RegisterSaleCommandValidator();

        Assert.That(validator.Validate(SalesFixture.WithLines()).IsValid, Is.True);
        Assert.That(validator.Validate(SalesFixture.WithLines(new SaleLineRequest(14, 0m))).IsValid, Is.True);
    }

    [Test]
    public void VistaPrevia_ComparteLasReglasDeFormaDeLaVenta() =>
        Assert.That(
            Codes(new PreviewSaleQueryValidator(), new PreviewSaleQuery(1, [new SaleLineRequest(-5, 1m)], null, TestData.Cajero)),
            Is.EqualTo(new[] { "SALE.INVALID_PRODUCT" }));

    [Test]
    public void VerificarIntento_ExigeLaClave() =>
        Assert.That(
            new VerifySaleAttemptCommandValidator().Validate(new VerifySaleAttemptCommand(Guid.Empty)).Errors.Single().PropertyName,
            Is.EqualTo("key"));

    [TestCase("Lines[0].ProductId", "lines[0].productId")]
    [TestCase("Payment.PaymentMethodId", "payment.paymentMethodId")]
    [TestCase("SessionId", "sessionId")]
    [TestCase("Idempotency-Key", "Idempotency-Key")]
    [TestCase("search", "search")]
    public void ElCampoSeInformaConElNombreDelJson(string propertyName, string expected) =>
        Assert.That(ValidationTargets.FromPropertyName(propertyName), Is.EqualTo(expected));
}
