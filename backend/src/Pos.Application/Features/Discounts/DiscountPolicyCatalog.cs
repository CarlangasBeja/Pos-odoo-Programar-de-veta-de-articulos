using Pos.Domain.Common;
using Pos.Domain.Discounts;
using Pos.Domain.Operators;

namespace Pos.Application.Features.Discounts;

/// <summary>
/// Registro de las políticas de descuento habilitadas (se arma con inyección de dependencias).
/// Para agregar una política basta con implementar <see cref="IDiscountPolicy"/> y registrarla: este catálogo
/// y los Handlers no cambian (principio abierto/cerrado, RF-07).
/// </summary>
public sealed class DiscountPolicyCatalog
{
    private readonly Dictionary<string, IDiscountPolicy> _policies;

    public DiscountPolicyCatalog(IEnumerable<IDiscountPolicy> policies)
    {
        ArgumentNullException.ThrowIfNull(policies);
        _policies = new Dictionary<string, IDiscountPolicy>(StringComparer.OrdinalIgnoreCase);
        foreach (var policy in policies)
        {
            if (!_policies.TryAdd(policy.Code, policy))
            {
                throw new InvalidOperationException($"La política de descuento «{policy.Code}» está registrada dos veces.");
            }
        }
    }

    public Result<IDiscountPolicy> Find(string code) =>
        _policies.TryGetValue(code.Trim(), out var policy) ? Result<IDiscountPolicy>.Ok(policy) : DiscountErrors.UnknownPolicy(code);

    public IReadOnlyList<IDiscountPolicy> AvailableFor(OperatorRole role) =>
        _policies.Values.Where(p => p.IsAvailableFor(role)).OrderBy(p => p.Name, StringComparer.CurrentCulture).ToArray();
}

public sealed record DiscountPolicyView(
    string Code,
    string Name,
    string Description,
    DiscountPolicyKind Kind,
    decimal? MaxPercentage);
