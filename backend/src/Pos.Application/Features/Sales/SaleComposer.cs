using Pos.Application.Common;
using Pos.Application.Contracts.Gateways;
using Pos.Application.Features.Discounts;
using Pos.Domain.Catalog;
using Pos.Domain.Common;
using Pos.Domain.Discounts;
using Pos.Domain.Sales;
using Pos.Domain.Sessions;

namespace Pos.Application.Features.Sales;

/// <summary>
/// Arma el agregado <see cref="Sale"/> a partir de la venta en preparación: aplica las reglas del negocio sobre las
/// líneas (RN-01, RN-02), obtiene los precios desde Odoo (RN-03), verifica que cada producto esté habilitado (P04) y
/// aplica la política de descuento (RN-04). La comparten los Handlers de vista previa y de registro para no duplicar reglas.
/// La forma de la solicitud (identificadores, longitudes) ya la revisaron los Validators.
/// </summary>
public sealed class SaleComposer(IProductCatalog catalog, DiscountPolicyCatalog discountPolicies)
{
    public async Task<Result<Sale>> ComposeAsync(
        Guid saleKey,
        PosSession session,
        ISaleDraft request,
        OperatorIdentity requestedBy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(requestedBy);

        var lines = ValidateLines(request.Lines);
        if (lines.IsFailure)
        {
            return Result<Sale>.Failure(lines.Errors);
        }

        if (request.SessionId != session.Id)
        {
            return SessionErrors.Mismatch(request.SessionId, session);
        }

        var started = Sale.Start(saleKey, session);
        if (started.IsFailure)
        {
            return started;
        }

        var sale = started.Value;
        var products = await LoadProductsAsync(lines.Value, cancellationToken);
        if (products.IsFailure)
        {
            return Result<Sale>.Failure(products.Errors);
        }

        var added = Result.Combine(lines.Value.Select(line => sale.AddLine(products.Value[line.ProductId], line.Quantity)));
        if (added.IsFailure)
        {
            return Result<Sale>.Failure(added.Errors);
        }

        var discount = ApplyDiscount(sale, request.Discount, requestedBy);
        return discount.IsSuccess ? sale : Result<Sale>.Failure(discount.Errors);
    }

    private static Result<IReadOnlyList<ValidLine>> ValidateLines(IReadOnlyList<SaleLineRequest>? requested)
    {
        if (requested is null || requested.Count == 0)
        {
            return SaleErrors.Empty(); // RN-01 / P01
        }

        if (requested.Count > Sale.MaxLines)
        {
            return SaleErrors.TooManyLines(Sale.MaxLines);
        }

        var errors = new List<Error>();
        var valid = new List<ValidLine>();
        for (var index = 0; index < requested.Count; index++)
        {
            var line = requested[index];
            var quantity = Quantity.Create(line.Quantity, $"lines[{index}].quantity"); // RN-02 / P02
            if (quantity.IsFailure)
            {
                errors.AddRange(quantity.Errors);
                continue;
            }

            valid.Add(new ValidLine(line.ProductId, quantity.Value));
        }

        return errors.Count > 0
            ? Result<IReadOnlyList<ValidLine>>.Failure(errors)
            : Result<IReadOnlyList<ValidLine>>.Ok(valid);
    }

    private async Task<Result<IReadOnlyDictionary<int, Product>>> LoadProductsAsync(
        IReadOnlyList<ValidLine> lines,
        CancellationToken cancellationToken)
    {
        var ids = lines.Select(l => l.ProductId).Distinct().ToArray();
        var found = await catalog.GetByIdsAsync(ids, cancellationToken);
        if (found.IsFailure)
        {
            return Result<IReadOnlyDictionary<int, Product>>.Failure(found.Errors);
        }

        var byId = found.Value.ToDictionary(p => p.Id);
        var missing = ids.Where(id => !byId.ContainsKey(id)).Select(ProductErrors.NotFound).ToArray();
        return missing.Length > 0
            ? Result<IReadOnlyDictionary<int, Product>>.Failure(missing)
            : Result<IReadOnlyDictionary<int, Product>>.Ok(byId);
    }

    private Result ApplyDiscount(Sale sale, DiscountRequest? request, OperatorIdentity requestedBy)
    {
        if (request is null
            || string.IsNullOrWhiteSpace(request.PolicyCode)
            || string.Equals(request.PolicyCode, DiscountDecision.NoneCode, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Success();
        }

        var policy = discountPolicies.Find(request.PolicyCode);
        if (policy.IsFailure)
        {
            return Result.Failure(policy.Errors);
        }

        var decision = policy.Value.Evaluate(new DiscountContext(requestedBy.Role, sale.ToDiscountableLines(), request.Percentage));
        return decision.IsSuccess ? sale.ApplyDiscount(decision.Value) : Result.Failure(decision.Errors);
    }

    private sealed record ValidLine(int ProductId, Quantity Quantity);
}
