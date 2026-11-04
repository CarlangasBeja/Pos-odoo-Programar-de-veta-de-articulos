using MediatR;
using Pos.Domain.Common;

namespace Pos.Application.Features.Discounts.Queries.GetDiscountPolicies;

public sealed class GetDiscountPoliciesQueryHandler(DiscountPolicyCatalog catalog)
    : IRequestHandler<GetDiscountPoliciesQuery, Result<IReadOnlyList<DiscountPolicyView>>>
{
    public Task<Result<IReadOnlyList<DiscountPolicyView>>> Handle(GetDiscountPoliciesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var role = query.RequestedBy.Role;
        IReadOnlyList<DiscountPolicyView> policies = catalog.AvailableFor(role)
            .Select(p => new DiscountPolicyView(p.Code, p.Name, p.Description, p.Kind, p.MaxPercentageFor(role)))
            .ToArray();
        return Task.FromResult(Result<IReadOnlyList<DiscountPolicyView>>.Ok(policies));
    }
}
