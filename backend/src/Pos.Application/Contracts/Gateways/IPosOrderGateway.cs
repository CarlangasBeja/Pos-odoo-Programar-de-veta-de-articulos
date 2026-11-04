using Pos.Application.Common;
using Pos.Application.Features.History;
using Pos.Application.Features.Sales;
using Pos.Domain.Common;
using Pos.Domain.Sales;

namespace Pos.Application.Contracts.Gateways;

/// <summary>Resultado de enviar una venta al ERP. Distingue explícitamente el caso "sin confirmación".</summary>
public abstract record OrderSubmissionOutcome
{
    private OrderSubmissionOutcome()
    {
    }

    /// <summary>El ERP respondió y la orden existe.</summary>
    public sealed record Accepted(RegisteredOrder Order) : OrderSubmissionOutcome;

    /// <summary>El ERP rechazó la orden de forma definitiva: no quedó registrada.</summary>
    public sealed record Rejected(Error Error) : OrderSubmissionOutcome;

    /// <summary>No se sabe si la orden quedó registrada (timeout, conexión cortada, respuesta ilegible).</summary>
    public sealed record Unknown(string Reason) : OrderSubmissionOutcome;
}

/// <summary>Contrato (puerto de salida) hacia las órdenes del punto de venta del ERP (escritura y consultas del historial).</summary>
public interface IPosOrderGateway
{
    /// <summary>
    /// Registra la venta confirmada con su pago. La clave de la venta viaja como identificador único de la orden,
    /// de modo que un reenvío no crea una segunda orden en el ERP (RN-08).
    /// </summary>
    Task<OrderSubmissionOutcome> SubmitAsync(Sale sale, OperatorIdentity registeredBy, CancellationToken cancellationToken);

    /// <summary>Busca la orden creada con la clave de la venta. Devuelve <c>null</c> si no existe.</summary>
    Task<Result<RegisteredOrder?>> FindBySaleKeyAsync(Guid saleKey, CancellationToken cancellationToken);

    Task<Result<PagedResult<OrderSummary>>> SearchAsync(OrderSearchCriteria criteria, CancellationToken cancellationToken);

    Task<Result<OrderDetail?>> GetDetailAsync(int orderId, CancellationToken cancellationToken);
}
