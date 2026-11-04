using Pos.Domain.Common;
using Pos.Domain.Money;
using Pos.Domain.Payments;
using Pos.Domain.Taxes;

namespace Pos.Domain.Sessions;

/// <summary>Estado de <c>pos.session</c> en Odoo.</summary>
public enum PosSessionState
{
    OpeningControl,
    Opened,
    ClosingControl,
    Closed,
}

/// <summary>Sesión del punto de venta en Odoo, con la configuración que afecta al cálculo de la venta.</summary>
public sealed record PosSession(
    int Id,
    string Name,
    PosSessionState State,
    int ConfigId,
    string ConfigName,
    DateTimeOffset? OpenedAt,
    Currency Currency,
    TaxRoundingMode TaxRounding,
    IReadOnlyList<PaymentMethod> PaymentMethods,
    IReadOnlyList<string> UnsupportedSettings)
{
    /// <summary>Una venta solo se registra en una sesión abierta y con configuración compatible (P03).</summary>
    public Result EnsureAcceptsSales()
    {
        if (State != PosSessionState.Opened)
        {
            return SessionErrors.NotOpen(this);
        }

        return UnsupportedSettings.Count == 0
            ? Result.Success()
            : Result.Failure(UnsupportedSettings.Select(SessionErrors.UnsupportedSetting));
    }

    public bool AcceptsSales => EnsureAcceptsSales().IsSuccess;

    public Result<PaymentMethod> FindPaymentMethod(int paymentMethodId) =>
        PaymentMethods.FirstOrDefault(m => m.Id == paymentMethodId) is { } method
            ? method
            : PaymentErrors.MethodNotFound(paymentMethodId);
}

public static class SessionErrors
{
    public const string NoOpenSessionCode = "SESSION.NO_OPEN_SESSION";
    public const string NotOpenCode = "SESSION.NOT_OPEN";
    public const string MismatchCode = "SESSION.MISMATCH";
    public const string UnsupportedSettingCode = "SESSION.UNSUPPORTED_CONFIGURATION";
    private const string Target = "sessionId";

    public static Error NoOpenSession() => Error.Rule(
        NoOpenSessionCode,
        "No hay una sesión POS abierta en Odoo. Abra una sesión en Odoo para poder vender.",
        Target);

    public static Error NotOpen(PosSession session) => Error.Rule(
        NotOpenCode,
        $"La sesión {session.Name} no está abierta (estado: {Describe(session.State)}). No se pueden registrar ventas.",
        Target);

    public static Error Mismatch(int requested, PosSession active) => Error.Rule(
        MismatchCode,
        $"La venta se preparó para la sesión {requested}, pero la sesión abierta ahora es {active.Name} ({active.Id}). "
        + "Actualice la pantalla y revise la venta.",
        Target);

    public static Error UnsupportedSetting(string setting) => Error.Rule(UnsupportedSettingCode, setting, Target);

    public static string Describe(PosSessionState state) => state switch
    {
        PosSessionState.OpeningControl => "control de apertura",
        PosSessionState.Opened => "abierta",
        PosSessionState.ClosingControl => "en cierre",
        PosSessionState.Closed => "cerrada",
        _ => state.ToString(),
    };
}
