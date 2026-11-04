using Pos.Application.Contracts.Persistence;

namespace Pos.UnitTests.Fakes;

/// <summary>Doble en memoria del almacén de idempotencia, con la misma semántica que el adaptador real.</summary>
public sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    private readonly Dictionary<Guid, SaleAttempt> _attempts = [];
    private readonly HashSet<Guid> _inProgress = [];
    private readonly Lock _lock = new();

    public IReadOnlyDictionary<Guid, SaleAttempt> Attempts
    {
        get
        {
            lock (_lock)
            {
                return new Dictionary<Guid, SaleAttempt>(_attempts);
            }
        }
    }

    public bool IsLocked(Guid key)
    {
        lock (_lock)
        {
            return _inProgress.Contains(key);
        }
    }

    public Task<IdempotencyReservation> TryReserveAsync(Guid key, string fingerprint, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_inProgress.Contains(key))
            {
                return Task.FromResult<IdempotencyReservation>(new IdempotencyReservation.InProgress());
            }

            if (_attempts.TryGetValue(key, out var existing))
            {
                if (existing.Fingerprint != fingerprint)
                {
                    return Task.FromResult<IdempotencyReservation>(new IdempotencyReservation.FingerprintMismatch());
                }

                if (existing.Status != SaleAttemptStatus.Uncertain)
                {
                    return Task.FromResult<IdempotencyReservation>(new IdempotencyReservation.Completed(existing));
                }
            }

            _inProgress.Add(key);
            return Task.FromResult<IdempotencyReservation>(new IdempotencyReservation.Reserved(existing));
        }
    }

    public Task CompleteAsync(SaleAttempt attempt, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _attempts[attempt.Key] = attempt;
            _inProgress.Remove(attempt.Key);
        }

        return Task.CompletedTask;
    }

    public Task ReleaseAsync(Guid key)
    {
        lock (_lock)
        {
            _inProgress.Remove(key);
        }

        return Task.CompletedTask;
    }

    public Task<SaleAttempt?> FindAsync(Guid key, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return Task.FromResult(_attempts.GetValueOrDefault(key));
        }
    }
}

/// <summary>Reloj fijo para pruebas deterministas.</summary>
public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
