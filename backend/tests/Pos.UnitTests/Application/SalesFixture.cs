using Pos.Application.Features.Discounts;
using Pos.Application.Features.Sales;
using Pos.Application.Features.Sales.Commands.RegisterSale;
using Pos.Application.Features.Sales.Commands.VerifySaleAttempt;
using Pos.Application.Features.Sales.Queries.PreviewSale;
using Pos.Domain.Catalog;
using Pos.Domain.Discounts;
using Pos.Domain.Discounts.Policies;
using Pos.Domain.Operators;
using Pos.Domain.Sessions;
using Pos.UnitTests.Fakes;

namespace Pos.UnitTests.Application;

/// <summary>
/// Arma los Handlers de ventas con Pure DI (sin contenedor) y un doble de prueba para cada contrato de Application.
/// Así se prueba la lógica de cada caso de uso; la validación del pipeline se prueba aparte (ValidatorTests y MediatorTests).
/// </summary>
public sealed class SalesFixture
{
    public SalesFixture(PosSession? session = null, IEnumerable<IDiscountPolicy>? policies = null, params Product[] products)
    {
        Catalog = new FakeProductCatalog(products.Length > 0 ? products : [TestData.Cafe(), TestData.Agua(), TestData.Pan(), TestData.NoDisponibleEnPos()]);
        Sessions = new FakePosSessionGateway(session ?? TestData.OpenSession());
        Orders = new FakePosOrderGateway();
        Attempts = new InMemoryIdempotencyStore();
        Time = new FixedTimeProvider(new DateTimeOffset(2026, 10, 29, 15, 0, 0, TimeSpan.Zero));
        Policies = new DiscountPolicyCatalog(policies ?? DefaultPolicies());
        Composer = new SaleComposer(Catalog, Policies);
    }

    public FakeProductCatalog Catalog { get; }

    public FakePosSessionGateway Sessions { get; }

    public FakePosOrderGateway Orders { get; }

    public InMemoryIdempotencyStore Attempts { get; }

    public FixedTimeProvider Time { get; }

    public DiscountPolicyCatalog Policies { get; }

    public SaleComposer Composer { get; }

    public RegisterSaleCommandHandler Register => new(Sessions, Composer, Orders, Attempts, Time);

    public PreviewSaleQueryHandler Preview => new(Sessions, Composer);

    public VerifySaleAttemptCommandHandler Verify => new(Orders, Attempts, Time);

    public static IDiscountPolicy[] DefaultPolicies() =>
    [
        new ManualDiscountPolicy(new ManualDiscountOptions(
            new Dictionary<OperatorRole, decimal> { [OperatorRole.Cashier] = 10m, [OperatorRole.Supervisor] = 30m })),
        new VolumeDiscountPolicy(new VolumeDiscountOptions(10m, 5m, new HashSet<OperatorRole> { OperatorRole.Cashier, OperatorRole.Supervisor })),
    ];

    /// <summary>Venta típica: 2 cafés (23.80) + 1 agua (5.95) = 29.75, pagada con 50 en efectivo.</summary>
    public static RegisterSaleCommand TwoProductsCash(
        Guid? key = null,
        DiscountRequest? discount = null,
        decimal tendered = 50m,
        int sessionId = 1,
        int paymentMethodId = 2) => new(
            key ?? Guid.NewGuid(),
            sessionId,
            [new SaleLineRequest(14, 2m), new SaleLineRequest(16, 1m)],
            discount,
            new PaymentRequest(paymentMethodId, tendered),
            TestData.Cajero);

    public static RegisterSaleCommand WithLines(params SaleLineRequest[] lines) => new(
        Guid.NewGuid(),
        1,
        lines,
        null,
        new PaymentRequest(2, 1000m),
        TestData.Cajero);
}
