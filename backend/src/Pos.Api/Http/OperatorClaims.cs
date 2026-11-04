using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Pos.Application.Common;
using Pos.Domain.Operators;

namespace Pos.Api.Http;

/// <summary>Conversión entre el operador autenticado y los claims del token.</summary>
public static class OperatorClaims
{
    private const string DisplayNameClaim = "display_name";

    public static ClaimsPrincipal ToPrincipal(this OperatorIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, identity.Username),
            new Claim(DisplayNameClaim, identity.DisplayName),
            new Claim(ClaimTypes.Role, identity.Role.ToString()),
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, BearerTokenDefaults.AuthenticationScheme));
    }

    public static OperatorIdentity ToOperator(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        var username = principal.FindFirstValue(ClaimTypes.Name)
            ?? throw new InvalidOperationException("El token no contiene el usuario.");
        var displayName = principal.FindFirstValue(DisplayNameClaim) ?? username;
        var role = Enum.TryParse<OperatorRole>(principal.FindFirstValue(ClaimTypes.Role), out var parsed) ? parsed : OperatorRole.Cashier;
        return new OperatorIdentity(username, displayName, role);
    }
}
