using MediatR;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Pos.Api.Http;
using Pos.Application.Features.Auth.Commands.Login;
using Pos.Domain.Operators;

namespace Pos.Api.Controllers;

public sealed record LoginRequest(string? Username, string? Password);

public sealed record OperatorResponse(string Username, string DisplayName, OperatorRole Role, string RoleLabel);

[ApiController]
[Route("api/auth")]
public sealed class AuthController(ISender sender) : ControllerBase
{
    /// <summary>Identifica al operador y entrega un token Bearer (formato estándar de ASP.NET Core).</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(PosApiHost.LoginRateLimitPolicy)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new LoginCommand(request.Username, request.Password), cancellationToken);
        return result.IsSuccess
            ? SignIn(result.Value.ToPrincipal(), BearerTokenDefaults.AuthenticationScheme)
            : result.ToProblem(HttpContext);
    }

    [HttpGet("me")]
    public ActionResult<OperatorResponse> Me()
    {
        var current = User.ToOperator();
        return new OperatorResponse(current.Username, current.DisplayName, current.Role, current.Role.ToDisplayName());
    }
}
