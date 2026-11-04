using MediatR;
using Pos.Application.Common;
using Pos.Application.Contracts.Security;
using Pos.Domain.Common;

namespace Pos.Application.Features.Auth.Commands.Login;

/// <summary>
/// Verifica las credenciales del operador. No revela si el usuario existe: el mensaje es el mismo y el costo también
/// (si el usuario no existe se verifica contra un hash de relleno).
/// </summary>
public sealed class LoginCommandHandler(IOperatorDirectory directory, IPasswordHasher hasher)
    : IRequestHandler<LoginCommand, Result<OperatorIdentity>>
{
    public const string InvalidCredentialsCode = "AUTH.INVALID_CREDENTIALS";

    // Se calcula una sola vez por proceso: si se calculara en cada intento, un usuario inexistente costaría el doble
    // (calcular + verificar) y el tiempo de respuesta delataría que no existe.
    private static string? s_dummyHash;

    public async Task<Result<OperatorIdentity>> Handle(LoginCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var dummyHash = s_dummyHash ??= hasher.Hash(Guid.NewGuid().ToString("N"));

        var account = await directory.FindByUsernameAsync((command.Username ?? string.Empty).Trim(), cancellationToken);
        var passwordMatches = hasher.Verify(command.Password ?? string.Empty, account?.PasswordHash ?? dummyHash);

        if (account is null || !account.Active || !passwordMatches)
        {
            return new Error(InvalidCredentialsCode, "Usuario o contraseña incorrectos.", ErrorKind.Unauthorized);
        }

        return new OperatorIdentity(account.Username, account.DisplayName, account.Role);
    }
}
