using MediatR;
using Pos.Domain.Common;

namespace Pos.Application.Features.Sessions.Queries.GetActiveSession;

/// <summary>Sesión POS vigente en Odoo y si permite vender (RF-10, P03). No recibe datos del cliente: no tiene Validator.</summary>
public sealed record GetActiveSessionQuery : IRequest<Result<SessionView>>;
