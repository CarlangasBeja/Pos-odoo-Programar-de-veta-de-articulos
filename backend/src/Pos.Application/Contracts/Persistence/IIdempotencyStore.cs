using Pos.Application.Features.Sales;
using Pos.Domain.Common;

namespace Pos.Application.Contracts.Persistence;

public enum SaleAttemptStatus
{
    Registered,
    Rejected,
    Uncertain,
}

/// <summary>Registro de un intento de venta identificado por su clave de idempotencia (dato propio del POS).</summary>
public sealed record SaleAttempt(
    Guid Key,
    string Fingerprint,
    SaleAttemptStatus Status,
    SaleRegistrationResult? Result,
    Error? Error,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Respuesta al intentar reservar una clave de idempotencia.</summary>
public abstract record IdempotencyReservation
{
    private IdempotencyReservation()
    {
    }

    /// <summary>La clave queda bloqueada para esta solicitud. <see cref="Previous"/> trae un intento incierto anterior.</summary>
    public sealed record Reserved(SaleAttempt? Previous) : IdempotencyReservation;

    /// <summary>Otra solicitud con la misma clave se está procesando ahora mismo.</summary>
    public sealed record InProgress : IdempotencyReservation;

    /// <summary>La clave ya tiene un resultado definitivo; se devuelve tal cual (repetición segura).</summary>
    public sealed record Completed(SaleAttempt Attempt) : IdempotencyReservation;

    /// <summary>La clave ya se usó con otra venta distinta.</summary>
    public sealed record FingerprintMismatch : IdempotencyReservation;
}

/// <summary>
/// Almacén de claves de idempotencia (patrón Idempotency Key). Garantiza que una solicitud repetida
/// no genere una segunda venta y que dos solicitudes simultáneas con la misma clave no se procesen a la vez.
/// </summary>
public interface IIdempotencyStore
{
    Task<IdempotencyReservation> TryReserveAsync(Guid key, string fingerprint, CancellationToken cancellationToken);

    /// <summary>Guarda el resultado y libera la reserva.</summary>
    Task CompleteAsync(SaleAttempt attempt, CancellationToken cancellationToken);

    /// <summary>Libera la reserva sin guardar nada (la solicitud falló antes de contactar al ERP).</summary>
    Task ReleaseAsync(Guid key);

    Task<SaleAttempt?> FindAsync(Guid key, CancellationToken cancellationToken);
}
