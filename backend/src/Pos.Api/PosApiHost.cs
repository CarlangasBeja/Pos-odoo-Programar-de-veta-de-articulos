using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Authorization;
using Pos.Api.Composition;
using Pos.Api.Http;
using Pos.Application;
using Pos.Infrastructure;

namespace Pos.Api;

/// <summary>
/// Raíz de composición de la API (el host): reúne el módulo de cada capa (Application, Infrastructure y las políticas
/// de descuento) y la configuración propia de HTTP. ASP.NET Core registra el logging que MediatR necesita.
/// Las pruebas reutilizan este método y reemplazan los adaptadores de Odoo por dobles de prueba.
/// </summary>
public static class PosApiHost
{
    public const string FrontendCorsPolicy = "frontend";
    public const string LoginRateLimitPolicy = "login";

    public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var culture = CultureInfo.GetCultureInfo("es-CO");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        var builder = WebApplication.CreateBuilder(args);

        builder.Services
            .AddControllers()
            .AddApplicationPart(typeof(PosApiHost).Assembly) // descubre los controladores aunque el host lo arranque otro ensamblado (pruebas)
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
                options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
            })
            .ConfigureApiBehaviorOptions(options => options.InvalidModelStateResponseFactory = InvalidRequestResponse.Create);

        builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = ProblemDetailsDefaults.Customize);
        builder.Services.AddExceptionHandler<UnexpectedExceptionHandler>();

        builder.Services
            .AddAuthentication(BearerTokenDefaults.AuthenticationScheme)
            .AddBearerToken(options =>
            {
                var hours = builder.Configuration.GetValue("Auth:TokenLifetimeHours", 8);
                options.BearerTokenExpiration = TimeSpan.FromHours(hours);
                options.RefreshTokenExpiration = TimeSpan.FromHours(hours);
            });
        builder.Services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        builder.Services.AddCors(options => options.AddPolicy(FrontendCorsPolicy, policy => policy
            .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? ["http://localhost:5173"])
            .WithHeaders("Authorization", "Content-Type", "Idempotency-Key")
            .WithMethods("GET", "POST")
            .WithExposedHeaders("Location")));

        builder.Services.AddLoginRateLimiting(LoginRateLimitPolicy);
        builder.Services.AddApplicationServices();
        builder.Services.AddInfrastructure(builder.Configuration);
        builder.Services.AddDiscountPolicies(builder.Configuration);

        configure?.Invoke(builder);

        var app = builder.Build();

        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseSecurityHeaders();
        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
        }

        app.UseCors(FrontendCorsPolicy);
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllers();
        app.MapPosHealthChecks("/api/health");

        return app;
    }
}
