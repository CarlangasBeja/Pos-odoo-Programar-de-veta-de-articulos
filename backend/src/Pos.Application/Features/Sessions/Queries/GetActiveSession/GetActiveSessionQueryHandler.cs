using MediatR;
using Pos.Application.Contracts.Gateways;
using Pos.Domain.Common;
using Pos.Domain.Sessions;

namespace Pos.Application.Features.Sessions.Queries.GetActiveSession;

public sealed class GetActiveSessionQueryHandler(IPosSessionGateway sessions) : IRequestHandler<GetActiveSessionQuery, Result<SessionView>>
{
    public async Task<Result<SessionView>> Handle(GetActiveSessionQuery query, CancellationToken cancellationToken)
    {
        var snapshot = await sessions.GetActiveSessionAsync(cancellationToken);
        return snapshot.Map(ToView);
    }

    private static SessionView ToView(PosSessionSnapshot snapshot)
    {
        var session = snapshot.Session;
        var readiness = session.EnsureAcceptsSales();
        var currency = session.Currency;

        return new SessionView(
            session.Id,
            session.Name,
            session.State,
            SessionErrors.Describe(session.State),
            session.ConfigId,
            session.ConfigName,
            session.OpenedAt,
            new CurrencyView(currency.Code, snapshot.CurrencyFormat.Symbol, snapshot.CurrencyFormat.Position, currency.DecimalPlaces, currency.Rounding),
            session.TaxRounding,
            session.PaymentMethods
                .Select(m => new PaymentMethodView(m.Id, m.Name, m.Type, m.AllowsChange, m.IsSupported))
                .ToArray(),
            readiness.IsSuccess,
            readiness.Errors.Select(e => e.Message).ToArray());
    }
}
