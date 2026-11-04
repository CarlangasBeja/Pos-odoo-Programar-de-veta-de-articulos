using Pos.Domain.Common;
using Pos.Domain.Operators;

namespace Pos.Domain.Discounts.Policies;

/// <summary>
/// Base para políticas automáticas (patrón Template Method): centraliza la verificación del rol
/// y deja a cada subclase solo la regla que decide el porcentaje por línea.
/// </summary>
public abstract class AutomaticDiscountPolicy(IReadOnlySet<OperatorRole> allowedRoles) : IDiscountPolicy
{
    public abstract string Code { get; }

    public abstract string Name { get; }

    public abstract string Description { get; }

    public DiscountPolicyKind Kind => DiscountPolicyKind.Automatic;

    public bool IsAvailableFor(OperatorRole role) => allowedRoles.Contains(role);

    public decimal? MaxPercentageFor(OperatorRole role) => null;

    public Result<DiscountDecision> Evaluate(DiscountContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return IsAvailableFor(context.Role)
            ? Decide(context)
            : DiscountErrors.RoleNotAllowed(Name, context.Role);
    }

    protected abstract Result<DiscountDecision> Decide(DiscountContext context);

    protected static DiscountPercentage ValidPercentage(decimal value) =>
        DiscountPercentage.Create(value) is { IsSuccess: true } result
            ? result.Value
            : throw new InvalidOperationException($"Porcentaje configurado inválido: {value}.");
}
