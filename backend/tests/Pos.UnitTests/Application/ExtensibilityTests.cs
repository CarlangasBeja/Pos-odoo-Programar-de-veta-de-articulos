using Pos.Application.Common;
using Pos.Application.Contracts.Gateways;
using Pos.Application.Features.History;
using Pos.Application.Features.Sales;
using Pos.Application.Features.Sales.Commands.RegisterSale;
using Pos.Domain.Common;
using Pos.Domain.Discounts;
using Pos.Domain.Operators;
using Pos.Domain.Sales;
using Pos.UnitTests.Fakes;

namespace Pos.UnitTests.Application;

/// <summary>
/// P10 / RNF-07: cambio controlado. Se incorpora una política de descuento nueva y se cambia el mecanismo de
/// integración sin modificar <see cref="RegisterSaleCommandHandler"/>, <see cref="SaleComposer"/> ni el dominio de la venta.
/// </summary>
[TestFixture]
public sealed class ExtensibilityTests
{
    /// <summary>Política nueva escrita solo para esta prueba: 20 % en toda la venta, solo para supervisores.</summary>
    private sealed class HappyHourPolicy : IDiscountPolicy
    {
        public string Code => "HAPPY_HOUR";

        public string Name => "Hora feliz";

        public string Description => "20 % en toda la venta, solo supervisores.";

        public DiscountPolicyKind Kind => DiscountPolicyKind.Automatic;

        public bool IsAvailableFor(OperatorRole role) => role == OperatorRole.Supervisor;

        public decimal? MaxPercentageFor(OperatorRole role) => null;

        public Result<DiscountDecision> Evaluate(DiscountContext context)
        {
            if (!IsAvailableFor(context.Role))
            {
                return DiscountErrors.RoleNotAllowed(Name, context.Role);
            }

            var twenty = DiscountPercentage.Create(20m).Value;
            return new DiscountDecision(Code, Name, context.Lines.Select(_ => twenty).ToArray());
        }
    }

    [Test]
    public async Task P10_UnaPoliticaNuevaSeIncorporaSinCambiarElHandler()
    {
        var fixture = new SalesFixture(policies: [.. SalesFixture.DefaultPolicies(), new HappyHourPolicy()]);
        var command = SalesFixture.TwoProductsCash(discount: new DiscountRequest("HAPPY_HOUR", null)) with { Operator = TestData.Supervisor };

        var result = await fixture.Register.Handle(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(fixture.Orders.Submitted.Single().Lines.Select(l => l.Discount.Value), Is.All.EqualTo(20m));
        Assert.That(fixture.Policies.AvailableFor(OperatorRole.Cashier).Select(p => p.Code), Does.Not.Contain("HAPPY_HOUR"));
    }

    [Test]
    public async Task P10_DeshabilitarUnaPoliticaSoloAfectaAEsaPolitica()
    {
        var fixture = new SalesFixture(policies: []); // configuración sin descuentos

        var withDiscount = await fixture.Register.Handle(SalesFixture.TwoProductsCash(discount: new DiscountRequest("MANUAL", 5m)), CancellationToken.None);
        var withoutDiscount = await fixture.Register.Handle(SalesFixture.TwoProductsCash(), CancellationToken.None);

        Assert.That(withDiscount.FirstError.Code, Is.EqualTo(DiscountErrors.UnknownPolicyCode));
        Assert.That(withoutDiscount.Value.Status, Is.EqualTo(SaleRegistrationStatus.Registered));
    }

    /// <summary>Otro mecanismo de integración (por ejemplo, otra versión de Odoo): solo implementa el contrato.</summary>
    private sealed class AlternativeOrderGateway : IPosOrderGateway
    {
        private readonly FakePosOrderGateway _inner = new();

        public int Calls { get; private set; }

        public Task<OrderSubmissionOutcome> SubmitAsync(Sale sale, OperatorIdentity registeredBy, CancellationToken cancellationToken)
        {
            Calls++;
            return _inner.SubmitAsync(sale, registeredBy, cancellationToken);
        }

        public Task<Result<RegisteredOrder?>> FindBySaleKeyAsync(Guid saleKey, CancellationToken cancellationToken) =>
            _inner.FindBySaleKeyAsync(saleKey, cancellationToken);

        public Task<Result<PagedResult<OrderSummary>>> SearchAsync(
            OrderSearchCriteria criteria,
            CancellationToken cancellationToken) => _inner.SearchAsync(criteria, cancellationToken);

        public Task<Result<OrderDetail?>> GetDetailAsync(int orderId, CancellationToken cancellationToken) =>
            _inner.GetDetailAsync(orderId, cancellationToken);
    }

    [Test]
    public async Task P10_CambiarElMecanismoDeIntegracionNoAlteraLasReglas()
    {
        var fixture = new SalesFixture();
        var alternative = new AlternativeOrderGateway();
        var handler = new RegisterSaleCommandHandler(fixture.Sessions, fixture.Composer, alternative, fixture.Attempts, fixture.Time);

        var registered = await handler.Handle(SalesFixture.TwoProductsCash(), CancellationToken.None);
        var invalid = await handler.Handle(SalesFixture.WithLines(), CancellationToken.None);

        Assert.That(registered.Value.Status, Is.EqualTo(SaleRegistrationStatus.Registered));
        Assert.That(invalid.FirstError.Code, Is.EqualTo(SaleErrors.EmptyCode));
        Assert.That(alternative.Calls, Is.EqualTo(1));
    }
}
