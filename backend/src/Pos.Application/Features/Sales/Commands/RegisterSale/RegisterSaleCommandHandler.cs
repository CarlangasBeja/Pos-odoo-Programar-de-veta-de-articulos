using MediatR;
using Pos.Application.Contracts.Gateways;
using Pos.Application.Contracts.Persistence;
using Pos.Domain.Common;
using Pos.Domain.Sales;

namespace Pos.Application.Features.Sales.Commands.RegisterSale;

/// <summary>
/// Confirma la venta, la registra en Odoo con su pago y devuelve el resultado real.
/// <list type="bullet">
/// <item>RN-08 / P06: la clave de idempotencia impide procesar dos veces la misma venta.</item>
/// <item>P03 / P04: si la sesión o algún producto no son válidos, no se envía nada a Odoo.</item>
/// <item>RN-07 / P05: solo se informa éxito cuando Odoo confirma la orden pagada; ante timeout, el resultado es incierto.</item>
/// </list>
/// La forma del Command (clave presente, pago presente, identificadores válidos) ya la revisó <see cref="RegisterSaleCommandValidator"/>.
/// </summary>
public sealed class RegisterSaleCommandHandler : IRequestHandler<RegisterSaleCommand, Result<SaleRegistrationResult>>
{
    private readonly IPosSessionGateway _sessions;
    private readonly SaleComposer _composer;
    private readonly IPosOrderGateway _orders;
    private readonly IIdempotencyStore _attempts;
    private readonly TimeProvider _time;
    private readonly SaleAttemptFactory _factory;

    public RegisterSaleCommandHandler(
        IPosSessionGateway sessions,
        SaleComposer composer,
        IPosOrderGateway orders,
        IIdempotencyStore attempts,
        TimeProvider time)
    {
        _sessions = sessions;
        _composer = composer;
        _orders = orders;
        _attempts = attempts;
        _time = time;
        _factory = new SaleAttemptFactory(time);
    }

    public async Task<Result<SaleRegistrationResult>> Handle(RegisterSaleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Garantizado por el Validator; se conserva para que el tipo no admita una venta sin pago.
        if (command.Payment is not { } payment)
        {
            return IdempotencyErrors.PaymentRequired();
        }

        var fingerprint = SaleRequestFingerprint.Compute(command, payment);
        var reservation = await _attempts.TryReserveAsync(command.IdempotencyKey, fingerprint, cancellationToken);

        return reservation switch
        {
            IdempotencyReservation.InProgress => IdempotencyErrors.InProgress(),
            IdempotencyReservation.FingerprintMismatch => IdempotencyErrors.KeyReused(),
            IdempotencyReservation.Completed completed => SaleAttemptFactory.ToResult(completed.Attempt, replayed: true),
            IdempotencyReservation.Reserved reserved => await ProcessAsync(command, payment, fingerprint, reserved.Previous, cancellationToken),
            _ => throw new InvalidOperationException("Reserva de idempotencia desconocida."),
        };
    }

    private async Task<Result<SaleRegistrationResult>> ProcessAsync(
        RegisterSaleCommand command,
        PaymentRequest payment,
        string fingerprint,
        SaleAttempt? previous,
        CancellationToken cancellationToken)
    {
        var key = command.IdempotencyKey;
        var completed = false;
        try
        {
            // Un intento anterior quedó incierto: primero se pregunta a Odoo si la venta ya existe.
            if (previous?.Result is { } previousResult)
            {
                var found = await _orders.FindBySaleKeyAsync(key, cancellationToken);
                if (found is { IsSuccess: true, Value: { } existing })
                {
                    var reconciled = _factory.FromOrder(key, fingerprint, existing, PaymentSummary.From(previousResult), previous.CreatedAt);
                    await _attempts.CompleteAsync(reconciled, CancellationToken.None);
                    completed = true;
                    return SaleAttemptFactory.ToResult(reconciled, replayed: true);
                }
            }

            var session = await _sessions.GetActiveSessionAsync(cancellationToken);
            if (session.IsFailure)
            {
                return Result<SaleRegistrationResult>.Failure(session.Errors);
            }

            var composed = await _composer.ComposeAsync(key, session.Value.Session, command, command.Operator, cancellationToken);
            if (composed.IsFailure)
            {
                return Result<SaleRegistrationResult>.Failure(composed.Errors);
            }

            var sale = composed.Value;
            var method = session.Value.Session.FindPaymentMethod(payment.PaymentMethodId);
            if (method.IsFailure)
            {
                return Result<SaleRegistrationResult>.Failure(method.Errors);
            }

            var confirmed = sale.Confirm(method.Value, payment.AmountTendered);
            if (confirmed.IsFailure)
            {
                return Result<SaleRegistrationResult>.Failure(confirmed.Errors);
            }

            // Desde aquí la venta sale del sistema: no se cancela aunque el cliente HTTP se desconecte,
            // para poder guardar siempre el resultado real (el HttpClient tiene su propio timeout).
            var outcome = await _orders.SubmitAsync(sale, command.Operator, CancellationToken.None);
            var createdAt = previous?.CreatedAt ?? _time.GetUtcNow();

            if (outcome is OrderSubmissionOutcome.Rejected { Error.Kind: ErrorKind.ExternalUnavailable } unavailable)
            {
                // Odoo no recibió la solicitud: no hay nada que recordar y se permite reintentar con la misma clave.
                return unavailable.Error;
            }

            var attempt = BuildAttempt(sale, fingerprint, outcome, createdAt);
            await _attempts.CompleteAsync(attempt, CancellationToken.None);
            completed = true;
            return SaleAttemptFactory.ToResult(attempt, replayed: false);
        }
        finally
        {
            if (!completed)
            {
                await _attempts.ReleaseAsync(key);
            }
        }
    }

    private SaleAttempt BuildAttempt(Sale sale, string fingerprint, OrderSubmissionOutcome outcome, DateTimeOffset createdAt)
    {
        var payment = PaymentSummary.From(sale);
        switch (outcome)
        {
            case OrderSubmissionOutcome.Accepted accepted:
                var attempt = _factory.FromOrder(sale.Key, fingerprint, accepted.Order, payment, createdAt);
                if (attempt.Status == SaleAttemptStatus.Registered)
                {
                    sale.MarkRegistered();
                }
                else
                {
                    sale.MarkUncertain(attempt.Result!.Message);
                }

                return attempt;

            case OrderSubmissionOutcome.Rejected rejected:
                sale.MarkRejected(rejected.Error.Message);
                return _factory.Rejected(sale.Key, fingerprint, rejected.Error, createdAt);

            case OrderSubmissionOutcome.Unknown unknown:
                sale.MarkUncertain(unknown.Reason);
                return _factory.Uncertain(sale.Key, fingerprint, payment, SaleAttemptFactory.UncertainMessage, createdAt);

            default:
                throw new InvalidOperationException("Resultado de envío desconocido.");
        }
    }
}
