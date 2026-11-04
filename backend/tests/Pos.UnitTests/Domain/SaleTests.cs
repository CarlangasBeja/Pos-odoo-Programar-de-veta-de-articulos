using Pos.Domain.Catalog;
using Pos.Domain.Discounts;
using Pos.Domain.Payments;
using Pos.Domain.Sales;
using Pos.Domain.Sessions;
using Pos.UnitTests.Fakes;

namespace Pos.UnitTests.Domain;

[TestFixture]
public sealed class SaleTests
{
    private static Sale NewSale() => Sale.Start(Guid.NewGuid(), TestData.OpenSession()).Value;

    private static Quantity Qty(decimal value) => Quantity.Create(value).Value;

    [Test]
    public void P01_NoSeConfirmaUnaVentaSinProductos()
    {
        var sale = NewSale();

        var result = sale.Confirm(TestData.Efectivo, 10m);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.FirstError.Code, Is.EqualTo(SaleErrors.EmptyCode));
        Assert.That(sale.Status, Is.EqualTo(SaleStatus.Draft));
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(-0.5)]
    public void P02_LaCantidadDebeSerMayorQueCero(decimal value)
    {
        var quantity = Quantity.Create(value);

        Assert.That(quantity.IsFailure, Is.True);
        Assert.That(quantity.FirstError.Code, Is.EqualTo(SaleErrors.InvalidQuantityCode));
    }

    [TestCase(10_001)]
    [TestCase(1.2345)]
    public void LaCantidadTieneLimitesDeTamanoYPrecision(decimal value) =>
        Assert.That(Quantity.Create(value).IsFailure, Is.True);

    [Test]
    public void P03_NoSeIniciaUnaVentaEnUnaSesionCerrada()
    {
        var result = Sale.Start(Guid.NewGuid(), TestData.ClosedSession());

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.FirstError.Code, Is.EqualTo(SessionErrors.NotOpenCode));
    }

    [Test]
    public void P04_NoSeAgregaUnProductoNoHabilitadoEnElPos()
    {
        var sale = NewSale();

        var result = sale.AddLine(TestData.NoDisponibleEnPos(), Qty(1));

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.FirstError.Code, Is.EqualTo(ProductErrors.NotSellableCode));
        Assert.That(sale.Lines, Is.Empty);
    }

    [Test]
    public void AgregarElMismoProductoSumaLaCantidadEnUnaSolaLinea()
    {
        var sale = NewSale();

        sale.AddLine(TestData.Cafe(), Qty(1));
        sale.AddLine(TestData.Cafe(), Qty(2));

        Assert.That(sale.Lines, Has.Count.EqualTo(1));
        Assert.That(sale.Lines[0].Quantity.Value, Is.EqualTo(3m));
    }

    [Test]
    public void LosTotalesUsanElPrecioDeOdooYLosImpuestos()
    {
        var sale = NewSale();

        sale.AddLine(TestData.Cafe(), Qty(2)); // 20.00 + 3.80
        sale.AddLine(TestData.Agua(), Qty(1)); // 5.95 incluido (5.00 + 0.95)

        Assert.Multiple(() =>
        {
            Assert.That(sale.Totals.Untaxed, Is.EqualTo(25.00m));
            Assert.That(sale.Totals.Tax, Is.EqualTo(4.75m));
            Assert.That(sale.Totals.Total, Is.EqualTo(29.75m));
            Assert.That(sale.Totals.DiscountAmount, Is.EqualTo(0m));
        });
    }

    [Test]
    public void ElDescuentoReduceElTotalYSeInformaComoDiferencia()
    {
        var sale = NewSale();
        sale.AddLine(TestData.Cafe(), Qty(2));
        var ten = DiscountPercentage.Create(10m).Value;

        var result = sale.ApplyDiscount(new DiscountDecision("TEST", "Prueba 10 %", [ten]));

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(sale.Totals.Total, Is.EqualTo(21.42m)); // 18.00 + 3.42
        Assert.That(sale.Totals.DiscountAmount, Is.EqualTo(2.38m));
    }

    [Test]
    public void RN06_UnaVentaConfirmadaNoSeEdita()
    {
        var sale = NewSale();
        sale.AddLine(TestData.Cafe(), Qty(1));
        Assert.That(sale.Confirm(TestData.Efectivo, 20m).IsSuccess, Is.True);

        var addLine = sale.AddLine(TestData.Pan(), Qty(1));
        var discount = sale.ApplyDiscount(DiscountDecision.None(1));

        Assert.Multiple(() =>
        {
            Assert.That(sale.Status, Is.EqualTo(SaleStatus.Confirmed));
            Assert.That(addLine.FirstError.Code, Is.EqualTo(SaleErrors.NotEditableCode));
            Assert.That(discount.FirstError.Code, Is.EqualTo(SaleErrors.NotEditableCode));
            Assert.That(sale.Lines, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void RN05_NoSeConfirmaSiElPagoNoCubreElTotal()
    {
        var sale = NewSale();
        sale.AddLine(TestData.Cafe(), Qty(1)); // 11.90

        var result = sale.Confirm(TestData.Efectivo, 10m);

        Assert.That(result.FirstError.Code, Is.EqualTo(PaymentErrors.InsufficientCode));
        Assert.That(sale.Status, Is.EqualTo(SaleStatus.Draft));
    }

    [Test]
    public void ElEfectivoCalculaElCambio()
    {
        var sale = NewSale();
        sale.AddLine(TestData.Cafe(), Qty(1));

        sale.Confirm(TestData.Efectivo, 20m);

        Assert.That(sale.Payment!.Change, Is.EqualTo(8.10m));
    }

    [Test]
    public void ConTarjetaElMontoDebeSerExacto()
    {
        var sale = NewSale();
        sale.AddLine(TestData.Cafe(), Qty(1));

        Assert.That(sale.Confirm(TestData.Tarjeta, 20m).FirstError.Code, Is.EqualTo(PaymentErrors.ExactAmountRequiredCode));
        Assert.That(sale.Confirm(TestData.Tarjeta, 11.90m).IsSuccess, Is.True);
    }

    [Test]
    public void UnMetodoConTerminalIntegradaNoSePuedeUsar()
    {
        var sale = NewSale();
        sale.AddLine(TestData.Cafe(), Qty(1));

        Assert.That(sale.Confirm(TestData.Datafono, 11.90m).FirstError.Code, Is.EqualTo(PaymentErrors.MethodNotSupportedCode));
    }

    [Test]
    public void UnaVentaRegistradaNoVuelveAEstadoIncierto()
    {
        var sale = NewSale();
        sale.AddLine(TestData.Cafe(), Qty(1));
        sale.Confirm(TestData.Efectivo, 11.90m);

        Assert.That(sale.MarkRegistered().IsSuccess, Is.True);
        Assert.That(sale.MarkUncertain("x").FirstError.Code, Is.EqualTo(SaleErrors.InvalidTransitionCode));
    }

    [Test]
    public void UnaVentaInciertaPuedeConciliarseComoRegistrada()
    {
        var sale = NewSale();
        sale.AddLine(TestData.Cafe(), Qty(1));
        sale.Confirm(TestData.Efectivo, 11.90m);

        sale.MarkUncertain("timeout");

        Assert.That(sale.MarkRegistered().IsSuccess, Is.True);
        Assert.That(sale.Status, Is.EqualTo(SaleStatus.Registered));
    }
}
