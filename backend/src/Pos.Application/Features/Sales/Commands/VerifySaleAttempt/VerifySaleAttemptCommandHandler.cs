using MediatR;
using Pos.Application.Contracts.Gateways;
using Pos.Application.Contracts.Persistence;
using Pos.Domain.Common;

namespace Pos.Application.Features.Sales.Commands.VerifySaleAttempt;

/// <summary>Concilia un intento incierto con lo que Odoo tiene registrado (busca la orden por la clave de la venta).</summary>
public sealed class VerifySaleAttemptCommandHandler : IRequestHandler<VerifySaleAttemptCommand, Result<SaleRegistrationResult>>
{
    public const string StillUnknownMessage =
        "Odoo todavía no muestra esta venta. Si la conexión ya se restableció, puede reintentar el cobro: "
        + "la misma clave evita que se registre dos veces.";

    private readonly IPosOrderGateway _orders;
    private readonly IIdempotencyStore _attempts;
    private readonly SaleAttemptFactory _factory;

    public VerifySaleAttemptCommandHandler(IPosOrderGateway orders, IIdempotencyStore attempts, TimeProvider time)
    {
        _orders = orders;
        _attempts = attempts;
        _factory = new SaleAttemptFactory(time);
    }

    public async Task<Result<SaleRegistrationResult>> Handle(VerifySaleAttemptCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var saleKey = command.SaleKey;

        var attempt = await _attempts.FindAsync(saleKey, cancellationToken);
        if (attempt is null)
        {
            return IdempotencyErrors.AttemptNotFound(saleKey);
        }

        if (attempt.Status != SaleAttemptStatus.Uncertain)
        {
            return SaleAttemptFactory.ToResult(attempt, replayed: true);
        }

        var reservation = await _attempts.TryReserveAsync(saleKey, attempt.Fingerprint, cancellationToken);
        switch (reservation)
        {
            case IdempotencyReservation.InProgress:
                return IdempotencyErrors.InProgress();
            case IdempotencyReservation.Completed completed:
                return SaleAttemptFactory.ToResult(completed.Attempt, replayed: true);
            case IdempotencyReservation.Reserved:
                break;
            default:
                return IdempotencyErrors.KeyReused();
        }

        var updated = attempt;
        Result<RegisteredOrder?>? lookup = null;
        try
        {
            var payment = PaymentSummary.From(attempt.Result!);
            lookup = await _orders.FindBySaleKeyAsync(saleKey, cancellationToken);
            if (lookup.IsSuccess)
            {
                updated = lookup.Value is { } order
                    ? _factory.FromOrder(saleKey, attempt.Fingerprint, order, payment, attempt.CreatedAt)
                    : _factory.Uncertain(saleKey, attempt.Fingerprint, payment, StillUnknownMessage, attempt.CreatedAt);
            }
        }
        finally
        {
            await _attempts.CompleteAsync(updated, CancellationToken.None);
        }

        // Si Odoo no respondió a la consulta, se informa ese problema; el intento sigue incierto.
        return lookup is { IsFailure: true }
            ? Result<SaleRegistrationResult>.Failure(lookup.Errors)
            : SaleAttemptFactory.ToResult(updated, replayed: true);
    }
}
