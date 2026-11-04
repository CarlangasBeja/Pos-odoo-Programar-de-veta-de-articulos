using Microsoft.Extensions.Options;

namespace Pos.Infrastructure.Odoo;

/// <summary>
/// Configuración de la integración con Odoo. La API key NUNCA va en appsettings.json:
/// se define con <c>dotnet user-secrets</c> o con la variable de entorno <c>Odoo__ApiKey</c>.
/// </summary>
public sealed class OdooOptions
{
    public const string SectionName = "Odoo";

    /// <summary>URL base de Odoo, por ejemplo http://localhost:8069.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Nombre de la base de datos (encabezado X-Odoo-Database).</summary>
    public string Database { get; set; } = string.Empty;

    /// <summary>API key del usuario de integración (ámbito "rpc").</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Identificador del punto de venta (<c>pos.config</c>) que usa este POS.</summary>
    public int PosConfigId { get; set; }

    /// <summary>Idioma de los mensajes de Odoo.</summary>
    public string Language { get; set; } = "es_419";

    /// <summary>Zona horaria de la tienda, para filtrar el historial por fecha local.</summary>
    public string TimeZone { get; set; } = "America/Bogota";

    public int RequestTimeoutSeconds { get; set; } = 15;

    /// <summary>Tiempo máximo para registrar una venta; si se supera, el resultado se considera incierto.</summary>
    public int SubmitTimeoutSeconds { get; set; } = 45;

    /// <summary>Duración de la caché de datos casi estáticos (moneda, impuestos, categorías).</summary>
    public int ReferenceDataCacheSeconds { get; set; } = 300;
}

public sealed class OdooOptionsValidator : IValidateOptions<OdooOptions>
{
    public ValidateOptionsResult Validate(string? name, OdooOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();

        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            failures.Add("Odoo:BaseUrl debe ser una URL http(s) válida, por ejemplo http://localhost:8069.");
        }

        if (string.IsNullOrWhiteSpace(options.Database))
        {
            failures.Add("Odoo:Database es obligatorio (nombre de la base de datos de Odoo).");
        }

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            failures.Add("Odoo:ApiKey no está configurada. Ejecute odoo/setup.ps1 o use: "
                + "dotnet user-secrets set \"Odoo:ApiKey\" \"<clave>\" --project src/Pos.Api");
        }

        if (options.PosConfigId <= 0)
        {
            failures.Add("Odoo:PosConfigId debe ser el id del punto de venta en Odoo (lo imprime odoo/setup_pos.py).");
        }

        if (options.RequestTimeoutSeconds is < 1 or > 120 || options.SubmitTimeoutSeconds is < 5 or > 300)
        {
            failures.Add("Los tiempos de espera de Odoo están fuera de rango (lectura 1-120 s, registro 5-300 s).");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
