using Pos.Application.Common;
using Pos.Application.Features.Catalog;
using Pos.Infrastructure.Odoo;
using Pos.UnitTests.Fakes;

namespace Pos.UnitTests.Infrastructure;

/// <summary>
/// Rendimiento con un millón de productos: los totales grandes del catálogo se guardan un minuto para no recontar el
/// millón en cada página; los pequeños se cuentan siempre. Ver docs/RENDIMIENTO_Y_RED.md.
/// </summary>
[TestFixture]
public sealed class OdooProductCatalogCountTests
{
    private static OdooTestHost HostCounting(int total, Action onCount)
    {
        var host = new OdooTestHost();
        host.Handler
            .On("product.product/fields_get", _ => FakeHttpHandler.Json(new { }))
            .On("product.product/search_count", _ =>
            {
                onCount();
                return FakeHttpHandler.Json(total);
            })
            .On("product.product/search_read", _ => FakeHttpHandler.Json(Array.Empty<object>()))
            .On("pos.category/search_read", _ => FakeHttpHandler.Json(Array.Empty<object>()));
        return host;
    }

    private static ProductSearchCriteria Criteria(int page, string? text = null) =>
        new(text, null, OnlyInStock: false, ProductSort.NameAsc, PageRequest.Create(page, 24, 24).Value);

    [Test]
    public async Task UnTotalGrandeNoSeVuelveAContarAlCambiarDePagina()
    {
        var counts = 0;
        var host = HostCounting(1_000_011, () => counts++);
        var catalog = host.Catalog;

        var first = await catalog.SearchAsync(Criteria(1), CancellationToken.None);
        var second = await catalog.SearchAsync(Criteria(2), CancellationToken.None);

        Assert.That(counts, Is.EqualTo(1));
        Assert.That(first.Value.TotalCount, Is.EqualTo(1_000_011));
        Assert.That(second.Value.TotalCount, Is.EqualTo(1_000_011));
    }

    [Test]
    public async Task UnTotalPequenoSiempreSeCuenta()
    {
        var counts = 0;
        var host = HostCounting(OdooProductCatalog.LargeCountThreshold - 1, () => counts++);
        var catalog = host.Catalog;

        await catalog.SearchAsync(Criteria(1, "café"), CancellationToken.None);
        await catalog.SearchAsync(Criteria(1, "café"), CancellationToken.None);

        Assert.That(counts, Is.EqualTo(2), "Los totales pequeños son exactos: reflejan enseguida un producto nuevo");
    }

    [Test]
    public async Task CadaBusquedaTieneSuPropioTotal()
    {
        var counts = 0;
        var host = HostCounting(50_000, () => counts++);
        var catalog = host.Catalog;

        await catalog.SearchAsync(Criteria(1, "chocolate"), CancellationToken.None);
        await catalog.SearchAsync(Criteria(1, "queso"), CancellationToken.None);

        Assert.That(counts, Is.EqualTo(2));
    }
}
