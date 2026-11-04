using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pos.Application.Contracts.Gateways;
using Pos.Application.Contracts.Persistence;
using Pos.Application.Contracts.Security;
using Pos.Infrastructure.Idempotency;
using Pos.Infrastructure.Odoo;
using Pos.Infrastructure.Security;

namespace Pos.Infrastructure;

public static class DependencyInjection
{
    public const string OdooHealthCheckName = "odoo";

    /// <summary>Registra los adaptadores que implementan los puertos de Application.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<OdooOptions>()
            .Bind(configuration.GetSection(OdooOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<OdooOptions>, OdooOptionsValidator>();
        services.Configure<IdempotencyOptions>(configuration.GetSection(IdempotencyOptions.SectionName));
        services.Configure<OperatorsOptions>(configuration.GetSection(OperatorsOptions.SectionName));

        services.AddMemoryCache();
        services.TryAddTimeProvider();

        services.AddHttpClient<OdooJson2Client>((provider, client) =>
        {
            var odoo = provider.GetRequiredService<IOptions<OdooOptions>>().Value;
            client.BaseAddress = new Uri(odoo.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = Timeout.InfiniteTimeSpan; // cada llamada define su propio tiempo de espera
        });

        services.AddTransient<OdooReferenceData>();
        services.AddTransient<IProductCatalog, OdooProductCatalog>();
        services.AddTransient<IPosSessionGateway, OdooPosSessionGateway>();
        services.AddTransient<IPosOrderGateway, OdooPosOrderGateway>();

        services.AddSingleton<IIdempotencyStore, FileIdempotencyStore>();
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<IOperatorDirectory, ConfigurationOperatorDirectory>();

        services.AddHealthChecks().AddCheck<OdooHealthCheck>(OdooHealthCheckName);
        return services;
    }

    private static void TryAddTimeProvider(this IServiceCollection services)
    {
        if (services.All(d => d.ServiceType != typeof(TimeProvider)))
        {
            services.AddSingleton(TimeProvider.System);
        }
    }
}
