using System.Text.Json;
using Pos.Domain.Money;
using Pos.Domain.Taxes;

namespace Pos.UnitTests.Domain;

/// <summary>
/// RF-05 "totales coherentes con las reglas de cálculo verificadas": los vectores fueron generados con el motor
/// real de impuestos de Odoo 20 (odoo/tools/generate_tax_vectors.py). Si Odoo calcula otro total, rechaza el pago
/// por "no estar completamente pagado", así que el backend debe obtener exactamente el mismo valor.
/// </summary>
[TestFixture]
public sealed class TaxCalculatorOdooParityTests
{
    private static readonly Lazy<VectorFile> Vectors = new(LoadVectors);

    public static IEnumerable<TestCaseData> Cases() =>
        Vectors.Value.Vectors.Select((vector, index) =>
            new TestCaseData(vector).SetName($"Odoo20_{vector.Currency}_{vector.RoundingMethod}_{index:000}"));

    [Test]
    public void LosVectoresProvienenDeOdoo20()
    {
        Assert.That(Vectors.Value.OdooVersion, Does.StartWith("20."));
        Assert.That(Vectors.Value.Vectors, Has.Count.GreaterThanOrEqualTo(600));
    }

    [TestCaseSource(nameof(Cases))]
    public void ElTotalCoincideConElMotorDeOdoo(TaxVector vector)
    {
        var currency = new Currency(vector.Currency, vector.CurrencyRounding);
        var mode = vector.RoundingMethod == "round_per_line" ? TaxRoundingMode.PerLine : TaxRoundingMode.Global;
        var lines = vector.Lines
            .Select(l => new TaxableLine(l.PriceUnit, l.Quantity, l.Discount, l.Taxes.Select(ToTax).ToArray()))
            .ToArray();

        var result = TaxCalculator.Calculate(lines, currency, mode);

        Assert.Multiple(() =>
        {
            Assert.That(result.Total, Is.EqualTo(vector.Expected.Total), "total");
            Assert.That(result.TaxAmount, Is.EqualTo(vector.Expected.Tax), "impuestos");
            Assert.That(result.Untaxed, Is.EqualTo(vector.Expected.Untaxed), "base");
        });
    }

    private static Tax ToTax(string key)
    {
        var definition = Vectors.Value.Taxes[key];
        return new Tax(
            Id: key.GetHashCode(StringComparison.Ordinal),
            Name: key,
            AmountType: TaxAmountType.Percent,
            Amount: definition.Amount,
            PriceIncluded: definition.PriceIncluded,
            IncludeBaseAmount: false);
    }

    private static VectorFile LoadVectors()
    {
        using var stream = typeof(TaxCalculatorOdooParityTests).Assembly.GetManifestResourceStream("odoo20-tax-vectors.json")
            ?? throw new InvalidOperationException("No se encontró el recurso odoo20-tax-vectors.json.");
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        return JsonSerializer.Deserialize<VectorFile>(stream, options)
            ?? throw new InvalidOperationException("Vectores vacíos.");
    }

    public sealed record VectorFile(string OdooVersion, Dictionary<string, TaxDefinition> Taxes, List<TaxVector> Vectors);

    public sealed record TaxDefinition(decimal Amount, bool PriceIncluded);

    public sealed record TaxVector(
        string Currency,
        decimal CurrencyRounding,
        string RoundingMethod,
        List<VectorLine> Lines,
        ExpectedTotals Expected)
    {
        public override string ToString() => $"{Currency} {RoundingMethod} ({Lines.Count} líneas)";
    }

    public sealed record VectorLine(decimal PriceUnit, decimal Quantity, decimal Discount, List<string> Taxes);

    public sealed record ExpectedTotals(decimal Untaxed, decimal Tax, decimal Total);
}
