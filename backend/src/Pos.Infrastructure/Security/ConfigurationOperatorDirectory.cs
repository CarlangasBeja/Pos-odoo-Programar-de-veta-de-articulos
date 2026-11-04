using Microsoft.Extensions.Options;
using Pos.Application.Contracts.Security;
using Pos.Domain.Operators;

namespace Pos.Infrastructure.Security;

public sealed class OperatorsOptions
{
    public const string SectionName = "Operators";

    public List<OperatorAccountOptions> Accounts { get; set; } = [];
}

public sealed class OperatorAccountOptions
{
    public string Username { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public OperatorRole Role { get; set; } = OperatorRole.Cashier;

    /// <summary>Hash PBKDF2 de la contraseña (nunca la contraseña en texto plano).</summary>
    public string PasswordHash { get; set; } = string.Empty;

    public bool Active { get; set; } = true;
}

/// <summary>
/// Directorio de operadores leído de la configuración. Odoo sigue siendo el dueño de los datos empresariales;
/// aquí solo se guardan los usuarios del POS con su rol, que es un dato propio de esta aplicación.
/// </summary>
public sealed class ConfigurationOperatorDirectory(IOptionsMonitor<OperatorsOptions> options) : IOperatorDirectory
{
    public Task<OperatorAccount?> FindByUsernameAsync(string username, CancellationToken cancellationToken)
    {
        var account = options.CurrentValue.Accounts
            .FirstOrDefault(a => string.Equals(a.Username, username, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult(account is null
            ? null
            : new OperatorAccount(account.Username, account.DisplayName, account.Role, account.PasswordHash, account.Active));
    }
}
