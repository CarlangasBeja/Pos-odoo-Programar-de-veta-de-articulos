using MediatR;
using Pos.Application.Common;
using Pos.Domain.Common;

namespace Pos.Application.Features.Auth.Commands.Login;

/// <summary>Identificación del operador del POS.</summary>
public sealed record LoginCommand(string? Username, string? Password) : IRequest<Result<OperatorIdentity>>
{
    public const int MaxFieldLength = 128;

    // La contraseña nunca debe aparecer en registros ni en mensajes de depuración.
    public override string ToString() => $"{nameof(LoginCommand)} {{ Username = {Username} }}";
}
