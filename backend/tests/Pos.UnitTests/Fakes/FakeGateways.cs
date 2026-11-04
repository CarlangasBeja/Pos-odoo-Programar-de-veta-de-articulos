using Pos.Application.Common;
using Pos.Application.Contracts.Gateways;
using Pos.Application.Features.Catalog;
using Pos.Application.Features.History;
using Pos.Application.Features.Sales;
using Pos.Domain.Catalog;
using Pos.Domain.Common;
using Pos.Domain.Sales;
using Pos.Domain.Sessions;

namespace Pos.UnitTests.Fakes;

/// <summary>Doble del catálogo de Odoo: productos en memoria.</summary>
public sealed class FakeProductCatalog(params Product[] products) : IProductCatalog
{
    private readonly Dictionary<int, Product> _products = products.ToDictionary(p => p.Id);

    public Error? FailWith { get; set; }

    public int Calls { get; private set; }

    public ProductSearchCriteria? LastCriteria { get; private set; }

    public Task<Result<PagedResult<Product>>> SearchAsync(ProductSearchCriteria criteria, CancellationToken cancellationToken)
    {
        Calls++;
        LastCriteria = criteria;
        if (FailWith is not null)
        {
            return Task.FromResult(Result<PagedResult<Product>>.Failure(FailWith));
        }

        var matches = _products.Values
            .Where(p => criteria.Text is null || p.Name.Contains(criteria.Text, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.Name)
            .ToArray();
        var page = matches.Skip(criteria.Page.Offset).Take(criteria.Page.PageSize).ToArray();
        return Task.FromResult(Result<PagedResult<Product>>.Ok(new PagedResult<Product>(page, criteria.Page.Page, criteria.Page.PageSize, matches.Length)));
    }

    public Task<Result<IReadOnlyList<Product>>> GetByIdsAsync(IReadOnlyCollection<int> productIds, CancellationToken cancellationToken)
    {
        Calls++;
        if (FailWith is not null)
        {
            return Task.FromResult(Result<IReadOnlyList<Product>>.Failure(FailWith));
        }

        IReadOnlyList<Product> found = productIds.Where(_products.ContainsKey).Select(id => _products[id]).ToArray();
        return Task.FromResult(Result<IReadOnlyList<Product>>.Ok(found));
    }

    public Task<Result<IReadOnlyList<ProductCategory>>> GetCategoriesAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Result<IReadOnlyList<ProductCategory>>.Ok(
            _products.Values.SelectMany(p => p.Categories).DistinctBy(c => c.Id).ToArray()));
}

/// <summary>Doble de la sesión POS (contrato externo simulado, P03).</summary>
public sealed class FakePosSessionGateway : IPosSessionGateway
{
    public FakePosSessionGateway(PosSession? session) => Session = session;

    public PosSession? Session { get; set; }

    public Error? FailWith { get; set; }

    public Task<Result<PosSessionSnapshot>> GetActiveSessionAsync(CancellationToken cancellationToken)
    {
        if (FailWith is not null)
        {
            return Task.FromResult(Result<PosSessionSnapshot>.Failure(FailWith));
        }

        return Task.FromResult(Session is null
            ? Result<PosSessionSnapshot>.Failure(SessionErrors.NoOpenSession())
            : Result<PosSessionSnapshot>.Ok(new PosSessionSnapshot(Session, new CurrencyFormat("$", CurrencySymbolPosition.Before))));
    }

    public Task<Result<PricingSettings>> GetPricingSettingsAsync(CancellationToken cancellationToken)
    {
        var session = Session ?? TestData.OpenSession();
        return Task.FromResult(Result<PricingSettings>.Ok(new PricingSettings(session.Currency, session.TaxRounding)));
    }
}

/// <summary>
/// Doble del registro de órdenes en Odoo. Permite programar respuestas (aceptada, rechazada, sin confirmación)
/// y simular las órdenes que "existen" en Odoo para la conciliación.
/// </summary>
public sealed class FakePosOrderGateway : IPosOrderGateway
{
    private readonly Queue<Func<Sale, OrderSubmissionOutcome>> _outcomes = new();
    private int _nextOrderId = 100;

    public List<Sale> Submitted { get; } = [];

    public Dictionary<Guid, RegisteredOrder> OrdersInOdoo { get; } = [];

    public Error? LookupFailsWith { get; set; }

    /// <summary>Si se asigna, el envío espera esta tarea (para simular solicitudes simultáneas).</summary>
    public TaskCompletionSource? HoldSubmission { get; set; }

    public int SubmitCount => Submitted.Count;

    public FakePosOrderGateway AcceptsNext(OrderState state = OrderState.Paid)
    {
        _outcomes.Enqueue(sale =>
        {
            var order = NewOrder(sale, state);
            OrdersInOdoo[sale.Key] = order;
            return new OrderSubmissionOutcome.Accepted(order);
        });
        return this;
    }

    public FakePosOrderGateway RejectsNext(Error error)
    {
        _outcomes.Enqueue(_ => new OrderSubmissionOutcome.Rejected(error));
        return this;
    }

    /// <summary>Timeout: Odoo pudo o no registrar la venta.</summary>
    public FakePosOrderGateway TimesOutNext(bool butOdooRegisteredIt = false)
    {
        _outcomes.Enqueue(sale =>
        {
            if (butOdooRegisteredIt)
            {
                OrdersInOdoo[sale.Key] = NewOrder(sale, OrderState.Paid);
            }

            return new OrderSubmissionOutcome.Unknown("Timeout simulado");
        });
        return this;
    }

    public async Task<OrderSubmissionOutcome> SubmitAsync(Sale sale, OperatorIdentity registeredBy, CancellationToken cancellationToken)
    {
        Submitted.Add(sale);
        if (HoldSubmission is not null)
        {
            await HoldSubmission.Task;
        }

        if (_outcomes.Count == 0)
        {
            AcceptsNext();
        }

        return _outcomes.Dequeue()(sale);
    }

    public Task<Result<RegisteredOrder?>> FindBySaleKeyAsync(Guid saleKey, CancellationToken cancellationToken)
    {
        if (LookupFailsWith is not null)
        {
            return Task.FromResult(Result<RegisteredOrder?>.Failure(LookupFailsWith));
        }

        return Task.FromResult(Result<RegisteredOrder?>.Ok(OrdersInOdoo.GetValueOrDefault(saleKey)));
    }

    public OrderSearchCriteria? LastSearch { get; private set; }

    public Task<Result<PagedResult<OrderSummary>>> SearchAsync(OrderSearchCriteria criteria, CancellationToken cancellationToken)
    {
        LastSearch = criteria;
        var items = OrdersInOdoo.Values
            .Select(o => new OrderSummary(o.Id, o.Name, o.PosReference, o.DateOrder, o.State, o.AmountTotal, o.AmountPaid, "POS/00001", null))
            .ToArray();
        return Task.FromResult(Result<PagedResult<OrderSummary>>.Ok(new PagedResult<OrderSummary>(items, criteria.Page.Page, criteria.Page.PageSize, items.Length)));
    }

    public Task<Result<OrderDetail?>> GetDetailAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = OrdersInOdoo.Values.FirstOrDefault(o => o.Id == orderId);
        var detail = order is null
            ? null
            : new OrderDetail(order.Id, order.Name, order.PosReference, null, order.DateOrder, order.State, "POS/00001", 0m, order.AmountTotal, order.AmountPaid, order.AmountReturn, null, [], []);
        return Task.FromResult(Result<OrderDetail?>.Ok(detail));
    }

    private RegisteredOrder NewOrder(Sale sale, OrderState state)
    {
        var id = _nextOrderId++;
        var change = sale.Payment?.Change ?? 0m;
        return new RegisteredOrder(
            id,
            $"POS Web .NET - {id:000000}",
            $"260-1-{id:000000}",
            state,
            sale.Totals.Total,
            sale.Totals.Total,
            change,
            new DateTimeOffset(2026, 10, 29, 15, 0, 0, TimeSpan.Zero));
    }
}
