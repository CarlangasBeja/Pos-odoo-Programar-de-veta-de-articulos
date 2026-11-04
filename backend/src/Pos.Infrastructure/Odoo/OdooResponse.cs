using System.Net;
using System.Text.Json;

namespace Pos.Infrastructure.Odoo;

public enum OdooTransportFailureKind
{
    /// <summary>No se pudo conectar: la solicitud no llegó a Odoo.</summary>
    NotSent,

    /// <summary>Se envió la solicitud pero no llegó respuesta a tiempo.</summary>
    Timeout,

    /// <summary>La conexión se cortó durante el intercambio.</summary>
    Interrupted,

    /// <summary>Llegó una respuesta que no es la esperada (por ejemplo, HTML de un proxy).</summary>
    InvalidResponse,
}

/// <summary>Respuesta de una llamada JSON-2, sin excepciones para los casos esperados.</summary>
public abstract record OdooResponse
{
    private OdooResponse()
    {
    }

    public sealed record Success(JsonElement Result) : OdooResponse;

    /// <summary>Odoo procesó la llamada y lanzó un error (la transacción se revirtió).</summary>
    public sealed record Fault(HttpStatusCode Status, string ErrorName, string Message) : OdooResponse
    {
        public bool IsAuthenticationProblem => Status is HttpStatusCode.Unauthorized;

        public bool IsPermissionProblem => Status is HttpStatusCode.Forbidden;
    }

    public sealed record TransportFailure(OdooTransportFailureKind Kind, string Detail) : OdooResponse
    {
        /// <summary>Con <see cref="OdooTransportFailureKind.NotSent"/> se sabe con certeza que Odoo no hizo nada.</summary>
        public bool RequestMayHaveBeenProcessed => Kind != OdooTransportFailureKind.NotSent;
    }
}
