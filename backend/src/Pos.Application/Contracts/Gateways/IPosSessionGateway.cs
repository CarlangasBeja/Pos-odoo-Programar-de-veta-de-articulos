using Pos.Domain.Common;
using Pos.Domain.Money;
using Pos.Domain.Sessions;
using Pos.Domain.Taxes;

namespace Pos.Application.Contracts.Gateways;

public enum CurrencySymbolPosition
{
    Before,
    After,
}

/// <summary>Formato de presentación de la moneda (solo informativo para el frontend).</summary>
public sealed record CurrencyFormat(string Symbol, CurrencySymbolPosition Position);

public sealed record PosSessionSnapshot(PosSession Session, CurrencyFormat CurrencyFormat);

/// <summary>Configuración que afecta al cálculo de precios; cambia muy poco y el adaptador puede almacenarla en caché.</summary>
public sealed record PricingSettings(Currency Currency, TaxRoundingMode TaxRounding);

/// <summary>Contrato (puerto de salida) hacia la sesión del punto de venta del ERP.</summary>
public interface IPosSessionGateway
{
    /// <summary>
    /// Devuelve la sesión vigente del punto de venta configurado (abierta o en cierre), consultada en el momento.
    /// Si no hay ninguna, devuelve el error <see cref="SessionErrors.NoOpenSessionCode"/>.
    /// </summary>
    Task<Result<PosSessionSnapshot>> GetActiveSessionAsync(CancellationToken cancellationToken);

    Task<Result<PricingSettings>> GetPricingSettingsAsync(CancellationToken cancellationToken);
}
