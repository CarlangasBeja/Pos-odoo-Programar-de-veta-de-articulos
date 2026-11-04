using Pos.Application.Contracts.Gateways;
using Pos.Domain.Payments;
using Pos.Domain.Sessions;
using Pos.Domain.Taxes;

namespace Pos.Application.Features.Sessions;

public sealed record CurrencyView(string Code, string Symbol, CurrencySymbolPosition SymbolPosition, int DecimalPlaces, decimal Rounding);

public sealed record PaymentMethodView(int Id, string Name, PaymentMethodType Type, bool AllowsChange, bool Supported);

public sealed record SessionView(
    int Id,
    string Name,
    PosSessionState State,
    string StateLabel,
    int ConfigId,
    string ConfigName,
    DateTimeOffset? OpenedAt,
    CurrencyView Currency,
    TaxRoundingMode TaxRounding,
    IReadOnlyList<PaymentMethodView> PaymentMethods,
    bool AcceptsSales,
    IReadOnlyList<string> Issues);
