using System.Globalization;
using System.Text.Json;

namespace Pos.Infrastructure.Odoo;

/// <summary>
/// Lectura defensiva de los valores que devuelve Odoo. Odoo usa <c>false</c> para los campos vacíos
/// y <c>[id, "nombre"]</c> para los many2one; esta clase aísla esas particularidades (capa anticorrupción).
/// </summary>
internal static class OdooValues
{
    private const string OdooDateTimeFormat = "yyyy-MM-dd HH:mm:ss";

    public static int Int(JsonElement record, string field) =>
        record.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : 0;

    public static decimal Decimal(JsonElement record, string field) =>
        record.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDecimal() : 0m;

    public static bool Bool(JsonElement record, string field) =>
        record.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.True;

    public static string? String(JsonElement record, string field) =>
        record.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public static bool Has(JsonElement record, string field) => record.TryGetProperty(field, out _);

    public static (int Id, string Name)? Many2One(JsonElement record, string field)
    {
        if (!record.TryGetProperty(field, out var value) || value.ValueKind != JsonValueKind.Array || value.GetArrayLength() < 2)
        {
            return null;
        }

        return (value[0].GetInt32(), value[1].GetString() ?? string.Empty);
    }

    public static IReadOnlyList<int> Ids(JsonElement record, string field)
    {
        if (!record.TryGetProperty(field, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.Number).Select(v => v.GetInt32()).ToArray();
    }

    /// <summary>Odoo guarda las fechas en UTC como texto "yyyy-MM-dd HH:mm:ss".</summary>
    public static DateTimeOffset? DateTimeUtc(JsonElement record, string field)
    {
        var text = String(record, field);
        return text is not null && System.DateTime.TryParseExact(
            text,
            OdooDateTimeFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? new DateTimeOffset(parsed, TimeSpan.Zero)
            : null;
    }

    public static string FormatDateTimeUtc(DateTimeOffset value) =>
        value.UtcDateTime.ToString(OdooDateTimeFormat, CultureInfo.InvariantCulture);

    public static IEnumerable<JsonElement> Records(JsonElement result) =>
        result.ValueKind == JsonValueKind.Array ? result.EnumerateArray() : [];
}
