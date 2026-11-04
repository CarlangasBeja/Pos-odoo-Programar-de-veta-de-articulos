using Pos.Application.Contracts.Persistence;
using Pos.Application.Features.Sales;
using Pos.Domain.Catalog;
using Pos.Domain.Common;
using Pos.Domain.Discounts;
using Pos.Domain.Payments;
using Pos.Domain.Sales;
using Pos.Domain.Sessions;
using Pos.UnitTests.Fakes;

namespace Pos.UnitTests.Application;

/// <summary>
/// Pruebas mínimas P01–P06 del enunciado sobre el Handler de registro de ventas, con Pure DI y dobles de prueba
/// (sin contenedor ni Mediator): prueban la lógica del caso de uso y las reglas del dominio.
/// </summary>
[TestFixture]
public sealed class RegisterSaleCommandHandlerTests
{
    [Test]
    public async Task RegistraLaVentaYDevuelveElResultadoRealDeOdoo()
    {
        var fixture = new SalesFixture();

        var result = await fixture.Register.Handle(SalesFixture.TwoProductsCash(), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True, () => string.Join(" | ", result.Errors.Select(e => e.Message)));
        var sale = result.Value;
        Assert.Multiple(() =>
        {
            Assert.That(sale.Status, Is.EqualTo(SaleRegistrationStatus.Registered));
            Assert.That(sale.OrderId, Is.Not.Null);
            Assert.That(sale.OrderName, Does.StartWith("POS Web .NET"));
            Assert.That(sale.OrderState, Is.EqualTo(OrderState.Paid));
            Assert.That(sale.Total, Is.EqualTo(29.75m));
            Assert.That(sale.Change, Is.EqualTo(20.25m));
            Assert.That(sale.Replayed, Is.False);
            Assert.That(fixture.Orders.SubmitCount, Is.EqualTo(1));
            Assert.That(fixture.Attempts.Attempts[sale.SaleKey].Status, Is.EqualTo(SaleAttemptStatus.Registered));
        });
    }

    [Test]
    public async Task P01_RechazaUnaVentaSinProductosYNoLaEnvia()
    {
        var fixture = new SalesFixture();

        var result = await fixture.Register.Handle(SalesFixture.WithLines(), CancellationToken.None);

        Assert.That(result.FirstError.Code, Is.EqualTo(SaleErrors.EmptyCode));
        Assert.That(fixture.Orders.SubmitCount, Is.Zero);
    }

    [Test]
    public async Task P02_RechazaCantidadCeroONegativaYNoLaEnvia()
    {
        var fixture = new SalesFixture();

        var result = await fixture.Register.Handle(
            SalesFixture.WithLines(new SaleLineRequest(14, 0m), new SaleLineRequest(16, -3m)),
            CancellationToken.None);

        Assert.That(result.Errors, Has.Count.EqualTo(2));
        Assert.That(result.Errors.Select(e => e.Code), Is.All.EqualTo(SaleErrors.InvalidQuantityCode));
        Assert.That(result.Errors.Select(e => e.Target), Is.EqualTo(new[] { "lines[0].quantity", "lines[1].quantity" }));
        Assert.That(fixture.Orders.SubmitCount, Is.Zero);
    }

    [Test]
    public async Task P03_RechazaUnaSesionCerradaSimulandoElContratoExterno()
    {
        var fixture = new SalesFixture(session: TestData.ClosedSession());

        var result = await fixture.Register.Handle(SalesFixture.TwoProductsCash(), CancellationToken.None);

        Assert.That(result.FirstError.Code, Is.EqualTo(SessionErrors.NotOpenCode));
        Assert.That(result.FirstError.Message, Does.Contain("cerrada"));
        Assert.That(fixture.Orders.SubmitCount, Is.Zero);
    }

    [Test]
    public async Task P03_RechazaCuandoNoHaySesionAbierta()
    {
        var fixture = new SalesFixture();
        fixture.Sessions.Session = null;

        var result = await fixture.Register.Handle(SalesFixture.TwoProductsCash(), CancellationToken.None);

        Assert.That(result.FirstError.Code, Is.EqualTo(SessionErrors.NoOpenSessionCode));
        Assert.That(fixture.Orders.SubmitCount, Is.Zero);
    }

    [Test]
    public async Task P03_RechazaUnaVentaPreparadaParaOtraSesion()
    {
        var fixture = new SalesFixture();

        var result = await fixture.Register.Handle(SalesFixture.TwoProductsCash(sessionId: 7), CancellationToken.None);

        Assert.That(result.FirstError.Code, Is.EqualTo(SessionErrors.MismatchCode));
        Assert.That(fixture.Orders.SubmitCount, Is.Zero);
    }

    [Test]
    public async Task P04_RechazaUnProductoNoHabilitadoYEvitaElEnvio()
    {
        var fixture = new SalesFixture();

        var result = await fixture.Register.Handle(
            SalesFixture.WithLines(new SaleLineRequest(14, 1m), new SaleLineRequest(24, 1m)),
            CancellationToken.None);

        Assert.That(result.FirstError.Code, Is.EqualTo(ProductErrors.NotSellableCode));
        Assert.That(result.FirstError.Message, Does.Contain("Producto descontinuado"));
        Assert.That(fixture.Orders.SubmitCount, Is.Zero);
    }

    [Test]
    public async Task P04_RechazaUnProductoQueNoExisteEnOdoo()
    {
        var fixture = new SalesFixture();

        var result = await fixture.Register.Handle(SalesFixture.WithLines(new SaleLineRequest(999, 1m)), CancellationToken.None);

        Assert.That(result.FirstError.Code, Is.EqualTo(ProductErrors.NotFoundCode));
        Assert.That(fixture.Orders.SubmitCount, Is.Zero);
    }

    [Test]
    public async Task P05_NoDeclaraExitoAnteUnTimeoutDeOdoo()
    {
        var fixture = new SalesFixture();
        fixture.Orders.TimesOutNext();

        var result = await fixture.Register.Handle(SalesFixture.TwoProductsCash(), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.Status, Is.EqualTo(SaleRegistrationStatus.Uncertain));
            Assert.That(result.Value.OrderId, Is.Null);
            Assert.That(result.Value.Message, Does.Contain("No se recibió confirmación").And.Contain("No vuelva a cobrar"));
            Assert.That(fixture.Attempts.Attempts[result.Value.SaleKey].Status, Is.EqualTo(SaleAttemptStatus.Uncertain));
        });
    }

    [Test]
    public async Task P05_NoDeclaraExitoSiOdooRechazaLaVenta()
    {
        var fixture = new SalesFixture();
        var odooError = new Error("SALE.REJECTED_BY_ODOO", "La venta no se registró. Odoo rechazó la operación: La orden no se pagó por completo.", ErrorKind.ExternalRejected);
        fixture.Orders.RejectsNext(odooError);

        var result = await fixture.Register.Handle(SalesFixture.TwoProductsCash(), CancellationToken.None);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.FirstError, Is.EqualTo(odooError));
    }

    [Test]
    public async Task P05_NoDeclaraExitoSiLaOrdenQuedaSinPagarEnOdoo()
    {
        var fixture = new SalesFixture();
        fixture.Orders.AcceptsNext(OrderState.Draft);

        var result = await fixture.Register.Handle(SalesFixture.TwoProductsCash(), CancellationToken.None);

        Assert.That(result.Value.Status, Is.EqualTo(SaleRegistrationStatus.Uncertain));
        Assert.That(result.Value.Message, Does.Contain("pago no está confirmado"));
    }

    [Test]
    public async Task SiOdooNoEstaDisponibleSePermiteReintentarConLaMismaClave()
    {
        var fixture = new SalesFixture();
        var command = SalesFixture.TwoProductsCash();
        fixture.Orders.RejectsNext(new Error("ODOO.UNAVAILABLE", "Odoo no responde", ErrorKind.ExternalUnavailable));

        var first = await fixture.Register.Handle(command, CancellationToken.None);
        var second = await fixture.Register.Handle(command, CancellationToken.None);

        Assert.That(first.FirstError.Kind, Is.EqualTo(ErrorKind.ExternalUnavailable));
        Assert.That(second.Value.Status, Is.EqualTo(SaleRegistrationStatus.Registered));
        Assert.That(fixture.Orders.SubmitCount, Is.EqualTo(2));
    }

    [Test]
    public async Task P06_UnaSolicitudRepetidaNoCreaOtraVenta()
    {
        var fixture = new SalesFixture();
        var command = SalesFixture.TwoProductsCash();

        var first = await fixture.Register.Handle(command, CancellationToken.None);
        var second = await fixture.Register.Handle(command, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(fixture.Orders.SubmitCount, Is.EqualTo(1));
            Assert.That(second.Value.OrderId, Is.EqualTo(first.Value.OrderId));
            Assert.That(second.Value.Replayed, Is.True);
        });
    }

    [Test]
    public async Task P06_DosSolicitudesSimultaneasConLaMismaClaveSoloSeProcesanUnaVez()
    {
        var fixture = new SalesFixture();
        var command = SalesFixture.TwoProductsCash();
        fixture.Orders.HoldSubmission = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = fixture.Register.Handle(command, CancellationToken.None);
        var second = await fixture.Register.Handle(command, CancellationToken.None);
        fixture.Orders.HoldSubmission.SetResult();
        var firstResult = await first;

        Assert.That(second.FirstError.Code, Is.EqualTo(IdempotencyErrors.InProgressCode));
        Assert.That(firstResult.Value.Status, Is.EqualTo(SaleRegistrationStatus.Registered));
        Assert.That(fixture.Orders.SubmitCount, Is.EqualTo(1));
    }

    [Test]
    public async Task P06_UnReintentoTrasUnResultadoInciertoConciliaSinReenviar()
    {
        var fixture = new SalesFixture();
        var command = SalesFixture.TwoProductsCash();
        fixture.Orders.TimesOutNext(butOdooRegisteredIt: true);

        var uncertain = await fixture.Register.Handle(command, CancellationToken.None);
        var retry = await fixture.Register.Handle(command, CancellationToken.None);

        Assert.That(uncertain.Value.Status, Is.EqualTo(SaleRegistrationStatus.Uncertain));
        Assert.That(retry.Value.Status, Is.EqualTo(SaleRegistrationStatus.Registered));
        Assert.That(fixture.Orders.SubmitCount, Is.EqualTo(1), "La venta ya existía en Odoo: no debe reenviarse.");
    }

    [Test]
    public async Task P06_SiOdooNoTieneLaVentaInciertaElReintentoUsaLaMismaClave()
    {
        var fixture = new SalesFixture();
        var command = SalesFixture.TwoProductsCash();
        fixture.Orders.TimesOutNext();

        await fixture.Register.Handle(command, CancellationToken.None);
        var retry = await fixture.Register.Handle(command, CancellationToken.None);

        Assert.That(retry.Value.Status, Is.EqualTo(SaleRegistrationStatus.Registered));
        Assert.That(fixture.Orders.Submitted.Select(s => s.Key), Is.All.EqualTo(command.IdempotencyKey));
    }

    [Test]
    public async Task P06_ReutilizarLaClaveConOtraVentaEsUnConflicto()
    {
        var fixture = new SalesFixture();
        var key = Guid.NewGuid();
        await fixture.Register.Handle(SalesFixture.TwoProductsCash(key), CancellationToken.None);

        var reused = await fixture.Register.Handle(SalesFixture.TwoProductsCash(key, tendered: 100m), CancellationToken.None);

        Assert.That(reused.FirstError.Code, Is.EqualTo(IdempotencyErrors.KeyReusedCode));
        Assert.That(fixture.Orders.SubmitCount, Is.EqualTo(1));
    }

    [Test]
    public async Task UnErrorDeValidacionLiberaLaClaveParaCorregirYReintentar()
    {
        var fixture = new SalesFixture();
        var key = Guid.NewGuid();

        var insufficient = await fixture.Register.Handle(SalesFixture.TwoProductsCash(key, tendered: 5m), CancellationToken.None);

        Assert.That(insufficient.FirstError.Code, Is.EqualTo(PaymentErrors.InsufficientCode));
        Assert.That(fixture.Attempts.IsLocked(key), Is.False);
        Assert.That(fixture.Attempts.Attempts.ContainsKey(key), Is.False);
    }

    [Test]
    public async Task RF08_RechazaUnDescuentoNoAutorizadoSinEnviarLaVenta()
    {
        var fixture = new SalesFixture();

        var result = await fixture.Register.Handle(
            SalesFixture.TwoProductsCash(discount: new DiscountRequest("MANUAL", 20m)),
            CancellationToken.None);

        Assert.That(result.FirstError.Code, Is.EqualTo(DiscountErrors.NotAuthorizedCode));
        Assert.That(result.FirstError.Kind, Is.EqualTo(ErrorKind.Forbidden));
        Assert.That(fixture.Orders.SubmitCount, Is.Zero);
    }

    [Test]
    public async Task ElDescuentoAutorizadoLlegaALasLineasEnviadasAOdoo()
    {
        var fixture = new SalesFixture();

        var result = await fixture.Register.Handle(
            SalesFixture.TwoProductsCash(discount: new DiscountRequest("MANUAL", 10m)),
            CancellationToken.None);

        var sent = fixture.Orders.Submitted.Single();
        Assert.That(sent.Lines.Select(l => l.Discount.Value), Is.All.EqualTo(10m));
        Assert.That(result.Value.Total, Is.EqualTo(sent.Totals.Total));
        Assert.That(sent.Totals.Total, Is.EqualTo(26.78m)); // 21.42 + 5.36 (5.355 → 5.36)
    }

    [Test]
    public async Task RechazaUnMetodoDePagoQueNoEstaEnLaSesion()
    {
        var fixture = new SalesFixture();

        var result = await fixture.Register.Handle(SalesFixture.TwoProductsCash(paymentMethodId: 99), CancellationToken.None);

        Assert.That(result.FirstError.Code, Is.EqualTo(PaymentErrors.MethodNotFoundCode));
        Assert.That(fixture.Orders.SubmitCount, Is.Zero);
    }

    [Test]
    public async Task UnCommandSinPagoNoLlegaAOdoo()
    {
        // El Validator ya lo rechaza; el Handler además no admite una venta sin pago aunque lo llamen directamente.
        var fixture = new SalesFixture();

        var result = await fixture.Register.Handle(SalesFixture.TwoProductsCash() with { Payment = null }, CancellationToken.None);

        Assert.That(result.FirstError.Code, Is.EqualTo(IdempotencyErrors.PaymentRequiredCode));
        Assert.That(fixture.Orders.SubmitCount, Is.Zero);
    }
}
