using Pos.Domain.Catalog;
using Pos.Domain.Money;
using Pos.Domain.Sessions;
using Pos.Domain.Taxes;
using Pos.UnitTests.Fakes;

namespace Pos.UnitTests.Domain;

[TestFixture]
public sealed class CatalogAndTaxTests
{
    [Test]
    public void UnProductoPuedeTenerVariosMotivosParaNoVenderse()
    {
        var product = TestData.Cafe() with { Active = false, SaleOk = false, AvailableInPos = false };

        var result = product.EnsureSellable();

        Assert.That(result.Errors, Has.Count.EqualTo(3));
        Assert.That(result.Errors.Select(e => e.Code), Is.All.EqualTo(ProductErrors.NotSellableCode));
    }

    [Test]
    public void LosImpuestosQueNoSePuedenReplicarDeshabilitanElProducto()
    {
        var fixedTax = TestData.Product(40, "Con impuesto fijo", 10m, [TestData.ImpuestoFijo]);
        var mixed = TestData.Product(41, "Incluido y excluido", 10m, [TestData.Iva19, TestData.Iva19Incluido]);

        Assert.That(fixedTax.IsSellable, Is.False);
        Assert.That(mixed.IsSellable, Is.False);
        Assert.That(TestData.Cafe().IsSellable, Is.True);
    }

    [Test]
    public void ElRedondeoDeLaMonedaEsMitadHaciaArriba()
    {
        Assert.That(TestData.Usd.Round(2.675m), Is.EqualTo(2.68m));
        Assert.That(TestData.Usd.Round(2.674m), Is.EqualTo(2.67m));
        Assert.That(TestData.Cop.Round(1500.5m), Is.EqualTo(1501m));
        Assert.That(new Currency("XXX", 0.05m).Round(1.02m), Is.EqualTo(1.00m));
        Assert.That(TestData.Usd.DecimalPlaces, Is.EqualTo(2));
        Assert.That(TestData.Cop.DecimalPlaces, Is.EqualTo(0));
    }

    [Test]
    public void ConImpuestoIncluidoElTotalEsElPrecioYSeSeparaLaBase()
    {
        var result = TaxCalculator.Calculate([new TaxableLine(5.95m, 1m, 0m, [TestData.Iva19Incluido])], TestData.Usd, TaxRoundingMode.Global);

        Assert.That((result.Untaxed, result.TaxAmount, result.Total), Is.EqualTo((5.00m, 0.95m, 5.95m)));
    }

    [Test]
    public void ElRedondeoGlobalYPorLineaPuedenDiferir()
    {
        // Tres líneas de 0.33 + 19 %: por línea 0.06 x 3 = 0.18; global round(0.1881) = 0.19.
        var lines = Enumerable.Range(0, 3).Select(_ => new TaxableLine(0.33m, 1m, 0m, [TestData.Iva19])).ToArray();

        var perLine = TaxCalculator.Calculate(lines, TestData.Usd, TaxRoundingMode.PerLine);
        var global = TaxCalculator.Calculate(lines, TestData.Usd, TaxRoundingMode.Global);

        Assert.That(perLine.TaxAmount, Is.EqualTo(0.18m));
        Assert.That(global.TaxAmount, Is.EqualTo(0.19m));
    }

    [Test]
    public void LaSesionConConfiguracionNoSoportadaNoAceptaVentas()
    {
        var session = TestData.OpenSession() with { UnsupportedSettings = ["Redondeo de efectivo activo"] };

        var result = session.EnsureAcceptsSales();

        Assert.That(result.FirstError.Code, Is.EqualTo(SessionErrors.UnsupportedSettingCode));
        Assert.That(result.FirstError.Message, Is.EqualTo("Redondeo de efectivo activo"));
    }
}
