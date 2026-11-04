using Pos.Domain.Operators;

namespace Pos.Application.Contracts.Security;

/// <summary>Cuenta de un operador del POS. La contraseña se guarda solo como hash.</summary>
public sealed record OperatorAccount(string Username, string DisplayName, OperatorRole Role, string PasswordHash, bool Active);

public interface IOperatorDirectory
{
    Task<OperatorAccount?> FindByUsernameAsync(string username, CancellationToken cancellationToken);
}

public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string password, string passwordHash);
}
