using Pos.Application.Common;
using Pos.Domain.Catalog;
using Pos.Domain.Money;
using Pos.Domain.Operators;
using Pos.Domain.Payments;
using Pos.Domain.Sessions;
using Pos.Domain.Taxes;

namespace Pos.UnitTests.Fakes;

/// <summary>Datos de prueba reutilizables (Object Mother). Valores pensados para que los totales se calculen a mano.</summary>
public static class TestData
{
    public static readonly Currency Usd = new("USD", 0.01m);
    public static readonly Currency Cop = new("COP", 1m);

    public static readonly Tax Iva19 = new(18, "IVA 19 %", TaxAmountType.Percent, 19m, PriceIncluded: false, IncludeBaseAmount: false);
    public static readonly Tax Iva19Incluido = new(20, "IVA 19 % incluido", TaxAmountType.Percent, 19m, PriceIncluded: true, IncludeBaseAmount: false);
    public static readonly Tax ImpuestoFijo = new(30, "Bolsa plástica", TaxAmountType.Fixed, 50m, PriceIncluded: false, IncludeBaseAmount: false);

    public static readonly PaymentMethod Efectivo = new(2, "Efectivo", PaymentMethodType.Cash, UsesTerminal: false);
    public static readonly PaymentMethod Tarjeta = new(3, "Tarjeta", PaymentMethodType.Bank, UsesTerminal: false);
    public static readonly PaymentMethod Datafono = new(4, "Datáfono integrado", PaymentMethodType.Bank, UsesTerminal: true);

    public static readonly OperatorIdentity Cajero = new("cajero", "Ana Cajera", OperatorRole.Cashier);
    public static readonly OperatorIdentity Supervisor = new("supervisor", "Carlos Supervisor", OperatorRole.Supervisor);

    /// <summary>10.00 + IVA 19 % = 11.90</summary>
    public static Product Cafe(int id = 14) => Product(id, "Café americano", 10m, [Iva19]);

    /// <summary>5.95 con IVA incluido (base 5.00)</summary>
    public static Product Agua(int id = 16) => Product(id, "Agua mineral", 5.95m, [Iva19Incluido]);

    /// <summary>2.00 sin impuestos</summary>
    public static Product Pan(int id = 21) => Product(id, "Pan de bono", 2m, []);

    public static Product NoDisponibleEnPos(int id = 24) =>
        Product(id, "Producto descontinuado", 1m, [Iva19]) with { AvailableInPos = false };

    public static Product Product(int id, string name, decimal price, IReadOnlyList<Tax> taxes) => new(
        id,
        name,
        $"P-{id:000}",
        null,
        price,
        taxes,
        [new ProductCategory(5, "General")],
        SaleOk: true,
        AvailableInPos: true,
        Active: true,
        Availability.NotTracked);

    public static PosSession OpenSession(int id = 1, Currency? currency = null, TaxRoundingMode rounding = TaxRoundingMode.Global) => new(
        id,
        $"POS/{id:00000}",
        PosSessionState.Opened,
        ConfigId: 1,
        ConfigName: "POS Web .NET",
        OpenedAt: new DateTimeOffset(2026, 10, 29, 13, 0, 0, TimeSpan.Zero),
        currency ?? Usd,
        rounding,
        [Efectivo, Tarjeta, Datafono],
        UnsupportedSettings: []);

    public static PosSession ClosedSession(int id = 1) => OpenSession(id) with { State = PosSessionState.Closed };
}
