using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Pos.Infrastructure.Odoo;

/// <summary>Comprueba que Odoo responde, que la API key es válida y que el punto de venta configurado existe.</summary>
public sealed class OdooHealthCheck(OdooJson2Client odoo, IOptions<OdooOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var response = await odoo.CallAsync(
            "pos.config",
            "search_count",
            new Dictionary<string, object?> { ["domain"] = new object[] { new object[] { "id", "=", options.Value.PosConfigId } } },
            cancellationToken,
            TimeSpan.FromSeconds(5));

        return response switch
        {
            OdooResponse.Success { Result: var count } when count.GetInt32() == 1 => HealthCheckResult.Healthy("Odoo conectado."),
            OdooResponse.Success => HealthCheckResult.Degraded("Odoo responde, pero el punto de venta configurado no existe."),
            OdooResponse.Fault { IsAuthenticationProblem: true } => HealthCheckResult.Unhealthy("Odoo rechazó la API key de integración."),
            OdooResponse.Fault fault => HealthCheckResult.Unhealthy($"Odoo devolvió un error ({(int)fault.Status})."),
            _ => HealthCheckResult.Unhealthy("No fue posible comunicarse con Odoo."),
        };
    }
}
