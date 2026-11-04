using MediatR;
using Pos.Domain.Common;

namespace Pos.Application.Features.Sales.Commands.VerifySaleAttempt;

/// <summary>
/// RF-17: verifica en Odoo el estado real de una venta cuyo resultado quedó incierto. Es un Command (y no una Query)
/// porque actualiza el registro local del intento con lo que encuentra; nunca reenvía la venta a Odoo.
/// </summary>
public sealed record VerifySaleAttemptCommand(Guid SaleKey) : IRequest<Result<SaleRegistrationResult>>;
