using Pos.Domain.Common;
using Pos.Domain.Discounts;
using Pos.Domain.Discounts.Policies;
using Pos.Domain.Operators;

namespace Pos.UnitTests.Domain;

/// <summary>RF-06, RF-08 y RN-04: políticas de descuento autorizadas por rol.</summary>
[TestFixture]
public sealed class DiscountPolicyTests
{
    private static readonly ManualDiscountPolicy Manual = new(new ManualDiscountOptions(
        new Dictionary<OperatorRole, decimal> { [OperatorRole.Cashier] = 10m, [OperatorRole.Supervisor] = 30m }));

    private static readonly HashSet<OperatorRole> Everyone = [OperatorRole.Cashier, OperatorRole.Supervisor];

    private static DiscountContext Context(OperatorRole role, decimal? percentage, params (decimal Quantity, decimal Gross)[] lines) =>
        new(role, lines.Select((l, i) => new DiscountableLine(i + 1, $"Producto {i + 1}", l.Quantity, l.Gross)).ToArray(), percentage);

    [Test]
    public void UnCajeroPuedeAplicarHastaSuLimite()
    {
        var result = Manual.Evaluate(Context(OperatorRole.Cashier, 10m, (1, 100m), (2, 50m)));

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.LinePercentages.Select(p => p.Value), Is.All.EqualTo(10m));
    }

    [Test]
    public void RF08_SeRechazaUnDescuentoQueSuperaElLimiteDelRolConMensajeComprensible()
    {
        var result = Manual.Evaluate(Context(OperatorRole.Cashier, 15m, (1, 100m)));

        Assert.That(result.IsFailure, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.FirstError.Code, Is.EqualTo(DiscountErrors.NotAuthorizedCode));
            Assert.That(result.FirstError.Kind, Is.EqualTo(ErrorKind.Forbidden));
            Assert.That(result.FirstError.Message, Does.Contain("Cajero").And.Contain("10").And.Contain("15"));
        });
    }

    [Test]
    public void UnSupervisorTieneUnLimiteMayor() =>
        Assert.That(Manual.Evaluate(Context(OperatorRole.Supervisor, 25m, (1, 100m))).IsSuccess, Is.True);

    [TestCase(null)]
    [TestCase(0)]
    public void ElDescuentoManualExigeUnPorcentaje(decimal? percentage) =>
        Assert.That(Manual.Evaluate(Context(OperatorRole.Supervisor, percentage, (1, 100m))).FirstError.Code,
            Is.EqualTo(DiscountErrors.InvalidPercentageCode));

    [Test]
    public void UnRolSinLimiteConfiguradoNoPuedeAplicarDescuentoManual()
    {
        var onlySupervisor = new ManualDiscountPolicy(new ManualDiscountOptions(
            new Dictionary<OperatorRole, decimal> { [OperatorRole.Supervisor] = 20m }));

        Assert.That(onlySupervisor.IsAvailableFor(OperatorRole.Cashier), Is.False);
        Assert.That(onlySupervisor.Evaluate(Context(OperatorRole.Cashier, 5m, (1, 10m))).FirstError.Code,
            Is.EqualTo(DiscountErrors.NotAuthorizedCode));
    }

    [Test]
    public void ElDescuentoPorVolumenSoloAplicaALasLineasQueAlcanzanElMinimo()
    {
        var policy = new VolumeDiscountPolicy(new VolumeDiscountOptions(10m, 5m, Everyone));

        var result = policy.Evaluate(Context(OperatorRole.Cashier, null, (12, 120m), (3, 30m)));

        Assert.That(result.Value.LinePercentages.Select(p => p.Value), Is.EqualTo(new[] { 5m, 0m }));
    }

    [Test]
    public void ElDescuentoPorVolumenNoAplicaSiNingunaLineaAlcanzaElMinimo()
    {
        var policy = new VolumeDiscountPolicy(new VolumeDiscountOptions(10m, 5m, Everyone));

        var result = policy.Evaluate(Context(OperatorRole.Cashier, null, (2, 20m)));

        Assert.That(result.FirstError.Code, Is.EqualTo(DiscountErrors.NotApplicableCode));
    }

    [Test]
    public void ElDescuentoPorMontoExigeElMontoMinimo()
    {
        var policy = new MinimumAmountDiscountPolicy(new MinimumAmountDiscountOptions(100m, 3m, Everyone));

        Assert.That(policy.Evaluate(Context(OperatorRole.Cashier, null, (1, 99.99m))).FirstError.Code, Is.EqualTo(DiscountErrors.NotApplicableCode));
        Assert.That(policy.Evaluate(Context(OperatorRole.Cashier, null, (1, 60m), (1, 40m))).IsSuccess, Is.True);
    }

    [Test]
    public void UnaPoliticaAutomaticaRespetaLosRolesPermitidos()
    {
        var policy = new MinimumAmountDiscountPolicy(new MinimumAmountDiscountOptions(10m, 3m, new HashSet<OperatorRole> { OperatorRole.Supervisor }));

        Assert.That(policy.Evaluate(Context(OperatorRole.Cashier, null, (1, 50m))).FirstError.Code, Is.EqualTo(DiscountErrors.NotAuthorizedCode));
    }

    [TestCase(-1)]
    [TestCase(100.01)]
    [TestCase(10.555)]
    public void ElPorcentajeDeDescuentoSeValida(decimal value) =>
        Assert.That(DiscountPercentage.Create(value).IsFailure, Is.True);
}
