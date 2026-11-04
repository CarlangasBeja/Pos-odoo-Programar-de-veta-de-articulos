using Pos.Domain.Catalog;
using Pos.Domain.Common;
using Pos.Domain.Discounts;
using Pos.Domain.Payments;
using Pos.Domain.Sessions;
using Pos.Domain.Taxes;

namespace Pos.Domain.Sales;

/// <summary>Línea de la venta con la instantánea del producto tomada de Odoo.</summary>
public sealed class SaleLine
{
    internal SaleLine(Product product, Quantity quantity)
    {
        Product = product;
        Quantity = quantity;
    }

    public Product Product { get; }

    public Quantity Quantity { get; internal set; }

    public DiscountPercentage Discount { get; internal set; } = DiscountPercentage.Zero;

    /// <summary>Importe con impuestos antes del descuento.</summary>
    public decimal GrossAmount { get; internal set; }

    public LineAmounts Amounts { get; internal set; } = new(0m, 0m, 0m);
}

public sealed record SaleTotals(
    decimal Untaxed,
    decimal Tax,
    decimal Total,
    decimal GrossBeforeDiscount,
    IReadOnlyList<TaxTotal> Taxes)
{
    public static readonly SaleTotals Empty = new(0m, 0m, 0m, 0m, []);

    public decimal DiscountAmount => GrossBeforeDiscount - Total;
}

/// <summary>
/// Agregado Venta: concentra las reglas RN-01, RN-02, RN-04 (a través de la política), RN-05 y RN-06.
/// Los precios provienen siempre del producto leído de Odoo (RN-03); el cliente solo envía cantidades.
/// </summary>
public sealed class Sale
{
    public const int MaxLines = 100;

    private readonly List<SaleLine> _lines = [];

    private Sale(Guid key, PosSession session)
    {
        Key = key;
        Session = session;
        Discount = DiscountDecision.None(0);
    }

    /// <summary>Identificador idempotente de la venta; se usa como <c>uuid</c> de la orden en Odoo.</summary>
    public Guid Key { get; }

    public PosSession Session { get; }

    public SaleStatus Status { get; private set; } = SaleStatus.Draft;

    public IReadOnlyList<SaleLine> Lines => _lines;

    public DiscountDecision Discount { get; private set; }

    public SaleTotals Totals { get; private set; } = SaleTotals.Empty;

    public Payment? Payment { get; private set; }

    public string? StatusReason { get; private set; }

    public static Result<Sale> Start(Guid key, PosSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (key == Guid.Empty)
        {
            throw new ArgumentException("La clave de la venta es obligatoria.", nameof(key));
        }

        var accepts = session.EnsureAcceptsSales();
        return accepts.IsSuccess ? new Sale(key, session) : Result<Sale>.Failure(accepts.Errors);
    }

    public Result AddLine(Product product, Quantity quantity)
    {
        ArgumentNullException.ThrowIfNull(product);

        var editable = EnsureDraft();
        if (editable.IsFailure)
        {
            return editable;
        }

        var sellable = product.EnsureSellable();
        if (sellable.IsFailure)
        {
            return sellable;
        }

        var existing = _lines.FirstOrDefault(l => l.Product.Id == product.Id);
        if (existing is not null)
        {
            var merged = existing.Quantity.Add(quantity);
            if (merged.IsFailure)
            {
                return Result.Failure(merged.Errors);
            }

            existing.Quantity = merged.Value;
        }
        else
        {
            if (_lines.Count >= MaxLines)
            {
                return SaleErrors.TooManyLines(MaxLines);
            }

            _lines.Add(new SaleLine(product, quantity));
        }

        Discount = DiscountDecision.None(_lines.Count);
        Recalculate();
        return Result.Success();
    }

    /// <summary>Datos que necesita una política de descuento para decidir.</summary>
    public IReadOnlyList<DiscountableLine> ToDiscountableLines() =>
        _lines.Select(l => new DiscountableLine(l.Product.Id, l.Product.Name, l.Quantity.Value, l.GrossAmount)).ToArray();

    public Result ApplyDiscount(DiscountDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);

        var editable = EnsureDraft();
        if (editable.IsFailure)
        {
            return editable;
        }

        if (decision.LinePercentages.Count != _lines.Count)
        {
            return SaleErrors.DiscountMismatch();
        }

        Discount = decision;
        Recalculate();
        return Result.Success();
    }

    /// <summary>Confirma la venta con su pago. Después de confirmar, la venta no se puede editar (RN-05, RN-06).</summary>
    public Result Confirm(PaymentMethod method, decimal amountTendered)
    {
        ArgumentNullException.ThrowIfNull(method);

        var editable = EnsureDraft();
        if (editable.IsFailure)
        {
            return editable;
        }

        if (_lines.Count == 0)
        {
            return SaleErrors.Empty();
        }

        if (Totals.Total <= 0m)
        {
            return SaleErrors.TotalNotPositive();
        }

        var payment = Payments.Payment.Create(method, amountTendered, Totals.Total, Session.Currency);
        if (payment.IsFailure)
        {
            return Result.Failure(payment.Errors);
        }

        Payment = payment.Value;
        return TransitionTo(SaleStatus.Confirmed, reason: null);
    }

    public Result MarkRegistered() => TransitionTo(SaleStatus.Registered, reason: null);

    public Result MarkRejected(string reason) => TransitionTo(SaleStatus.Rejected, reason);

    public Result MarkUncertain(string reason) => TransitionTo(SaleStatus.Uncertain, reason);

    private Result TransitionTo(SaleStatus target, string? reason)
    {
        if (!SaleStateMachine.CanTransition(Status, target))
        {
            return SaleErrors.InvalidTransition(Status, target);
        }

        Status = target;
        StatusReason = reason;
        return Result.Success();
    }

    private Result EnsureDraft() =>
        Status == SaleStatus.Draft ? Result.Success() : SaleErrors.NotEditable(Status);

    private void Recalculate()
    {
        var currency = Session.Currency;
        var rounding = Session.TaxRounding;

        var gross = TaxCalculator.Calculate(
            _lines.Select(l => new TaxableLine(l.Product.UnitPrice, l.Quantity.Value, 0m, l.Product.Taxes)).ToArray(),
            currency,
            rounding);

        var discounted = TaxCalculator.Calculate(
            _lines.Select((l, i) => new TaxableLine(
                l.Product.UnitPrice,
                l.Quantity.Value,
                Discount.LinePercentages[i].Value,
                l.Product.Taxes)).ToArray(),
            currency,
            rounding);

        for (var i = 0; i < _lines.Count; i++)
        {
            _lines[i].GrossAmount = gross.Lines[i].Total;
            _lines[i].Discount = Discount.LinePercentages[i];
            _lines[i].Amounts = discounted.Lines[i];
        }

        Totals = new SaleTotals(discounted.Untaxed, discounted.TaxAmount, discounted.Total, gross.Total, discounted.Taxes);
    }
}
