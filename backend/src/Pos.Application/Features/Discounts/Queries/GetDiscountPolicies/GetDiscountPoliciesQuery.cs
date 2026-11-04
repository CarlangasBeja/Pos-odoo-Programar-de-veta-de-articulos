using MediatR;
using Pos.Application.Common;
using Pos.Domain.Common;

namespace Pos.Application.Features.Discounts.Queries.GetDiscountPolicies;

/// <summary>
/// RF-06: políticas de descuento habilitadas para el rol del operador. El operador sale del token, no del cliente:
/// no tiene Validator.
/// </summary>
public sealed record GetDiscountPoliciesQuery(OperatorIdentity RequestedBy) : IRequest<Result<IReadOnlyList<DiscountPolicyView>>>;
