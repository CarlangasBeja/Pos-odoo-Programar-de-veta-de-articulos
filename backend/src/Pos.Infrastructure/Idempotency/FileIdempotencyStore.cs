using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pos.Application.Contracts.Persistence;

namespace Pos.Infrastructure.Idempotency;

public sealed class IdempotencyOptions
{
    public const string SectionName = "Idempotency";

    /// <summary>Archivo donde se guardan los intentos de venta (dato propio del POS, no se duplica nada de Odoo).</summary>
    public string FilePath { get; set; } = "App_Data/sale-attempts.json";

    public int RetentionDays { get; set; } = 30;
}

/// <summary>
/// Implementación del almacén de idempotencia sobre un archivo JSON. Pensado para un único servidor de POS:
/// la reserva en memoria evita el procesamiento concurrente y el archivo conserva los resultados entre reinicios.
/// Si se necesitara escalar a varias instancias, se reemplaza este adaptador por uno de base de datos
/// sin tocar los casos de uso (el puerto <see cref="IIdempotencyStore"/> no cambia).
/// </summary>
public sealed class FileIdempotencyStore : IIdempotencyStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly TimeSpan _retention;
    private readonly TimeProvider _time;
    private readonly ILogger<FileIdempotencyStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HashSet<Guid> _inProgress = [];
    private Dictionary<Guid, SaleAttempt>? _attempts;

    public FileIdempotencyStore(IOptions<IdempotencyOptions> options, TimeProvider time, ILogger<FileIdempotencyStore> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _path = Path.GetFullPath(options.Value.FilePath);
        _retention = TimeSpan.FromDays(Math.Max(1, options.Value.RetentionDays));
        _time = time;
        _logger = logger;
    }

    public async Task<IdempotencyReservation> TryReserveAsync(Guid key, string fingerprint, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var attempts = await LoadAsync(cancellationToken);
            if (_inProgress.Contains(key))
            {
                return new IdempotencyReservation.InProgress();
            }

            if (attempts.TryGetValue(key, out var existing))
            {
                if (!string.Equals(existing.Fingerprint, fingerprint, StringComparison.Ordinal))
                {
                    return new IdempotencyReservation.FingerprintMismatch();
                }

                if (existing.Status != SaleAttemptStatus.Uncertain)
                {
                    return new IdempotencyReservation.Completed(existing);
                }
            }

            _inProgress.Add(key);
            return new IdempotencyReservation.Reserved(existing);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task CompleteAsync(SaleAttempt attempt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        // Siempre se completa (sin cancelación) para no dejar una clave bloqueada ni perder un resultado real.
        await _gate.WaitAsync(CancellationToken.None);
        try
        {
            var attempts = await LoadAsync(CancellationToken.None);
            attempts[attempt.Key] = attempt;
            Prune(attempts);
            await SaveAsync(attempts, CancellationToken.None);
        }
        finally
        {
            _inProgress.Remove(attempt.Key);
            _gate.Release();
        }
    }

    public async Task ReleaseAsync(Guid key)
    {
        await _gate.WaitAsync();
        try
        {
            _inProgress.Remove(key);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SaleAttempt?> FindAsync(Guid key, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var attempts = await LoadAsync(cancellationToken);
            return attempts.GetValueOrDefault(key);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Dictionary<Guid, SaleAttempt>> LoadAsync(CancellationToken cancellationToken)
    {
        if (_attempts is not null)
        {
            return _attempts;
        }

        if (!File.Exists(_path))
        {
            _attempts = [];
            return _attempts;
        }

        try
        {
            await using var stream = File.OpenRead(_path);
            var list = await JsonSerializer.DeserializeAsync<List<SaleAttempt>>(stream, SerializerOptions, cancellationToken) ?? [];
            _attempts = list.ToDictionary(a => a.Key);
        }
        catch (JsonException exception)
        {
            // Un archivo dañado no debe impedir vender; se conserva una copia para revisarla.
            var backup = $"{_path}.{_time.GetUtcNow():yyyyMMddHHmmss}.corrupt";
            File.Copy(_path, backup, overwrite: true);
            _logger.LogError(exception, "El archivo de idempotencia estaba dañado; se respaldó en {Backup}", backup);
            _attempts = [];
        }

        return _attempts;
    }

    private async Task SaveAsync(Dictionary<Guid, SaleAttempt> attempts, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Escritura atómica: primero a un temporal y luego se reemplaza el archivo.
        var temporary = _path + ".tmp";
        await using (var stream = File.Create(temporary))
        {
            await JsonSerializer.SerializeAsync(stream, attempts.Values.OrderBy(a => a.CreatedAt).ToList(), SerializerOptions, cancellationToken);
        }

        File.Move(temporary, _path, overwrite: true);
    }

    private void Prune(Dictionary<Guid, SaleAttempt> attempts)
    {
        var limit = _time.GetUtcNow() - _retention;
        foreach (var key in attempts.Where(a => a.Value.UpdatedAt < limit && a.Value.Status != SaleAttemptStatus.Uncertain).Select(a => a.Key).ToArray())
        {
            attempts.Remove(key);
        }
    }
}
