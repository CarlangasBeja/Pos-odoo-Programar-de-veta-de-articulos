using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pos.Infrastructure.Odoo;
using Pos.UnitTests.Fakes;

namespace Pos.UnitTests.Infrastructure;

/// <summary>Construye los adaptadores de Odoo sobre un <see cref="FakeHttpHandler"/> (sin red).</summary>
internal sealed class OdooTestHost
{
    public OdooTestHost(int submitTimeoutSeconds = 45)
    {
        Options = Microsoft.Extensions.Options.Options.Create(new OdooOptions
        {
            BaseUrl = "http://odoo.test",
            Database = "pos",
            ApiKey = "clave-de-prueba",
            PosConfigId = 1,
            Language = "es_419",
            RequestTimeoutSeconds = 5,
            SubmitTimeoutSeconds = submitTimeoutSeconds,
        });
        Client = new OdooJson2Client(
            new HttpClient(Handler) { BaseAddress = new Uri("http://odoo.test/"), Timeout = Timeout.InfiniteTimeSpan },
            Options,
            NullLogger<OdooJson2Client>.Instance);
        ReferenceData = new OdooReferenceData(Client, new MemoryCache(new MemoryCacheOptions()), Options);
    }

    public FakeHttpHandler Handler { get; } = new();

    public IOptions<OdooOptions> Options { get; }

    public OdooJson2Client Client { get; }

    public OdooReferenceData ReferenceData { get; }

    public OdooPosOrderGateway Orders => new(Client, Options, NullLogger<OdooPosOrderGateway>.Instance);

    public OdooProductCatalog Catalog => new(Client, ReferenceData);

    public OdooPosSessionGateway Sessions => new(Client, ReferenceData, Options);
}
