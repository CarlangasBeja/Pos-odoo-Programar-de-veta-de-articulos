using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pos.Application.Common;
using Pos.Application.Contracts.Persistence;
using Pos.Application.Features.Catalog;
using Pos.Application.Features.Sales;
using Pos.Domain.Catalog;
using Pos.Domain.Payments;
using Pos.Infrastructure.Idempotency;
using Pos.Infrastructure.Odoo;
using Pos.Infrastructure.Security;
using Pos.UnitTests.Fakes;

namespace Pos.UnitTests.Infrastructure;

[TestFixture]
public sealed class FileIdempotencyStoreTests
{
    private string _path = string.Empty;
    private readonly FixedTimeProvider _time = new(new DateTimeOffset(2026, 10, 29, 15, 0, 0, TimeSpan.Zero));

    [SetUp]
    public void SetUp() => _path = Path.Combine(Path.GetTempPath(), $"pos-attempts-{Guid.NewGuid():N}.json");

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    private FileIdempotencyStore NewStore() => new(
        Options.Create(new IdempotencyOptions { FilePath = _path, RetentionDays = 30 }),
        _time,
        NullLogger<FileIdempotencyStore>.Instance);

    private SaleAttempt Attempt(Guid key, SaleAttemptStatus status, string fingerprint = "huella") => new(
        key,
        fingerprint,
        status,
        new SaleRegistrationResult(key, status == SaleAttemptStatus.Registered ? SaleRegistrationStatus.Registered : SaleRegistrationStatus.Uncertain,
            "mensaje", 7, "POS - 7", "260-1-7", OrderState.Paid, 29.75m, 50m, 20.25m, "Efectivo", PaymentMethodType.Cash, _time.GetUtcNow()),
        null,
        _time.GetUtcNow(),
        _time.GetUtcNow());

    [Test]
    public async Task UnaClaveNuevaSeReservaYUnaSegundaReservaQuedaEnCurso()
    {
        var store = NewStore();
        var key = Guid.NewGuid();

        var first = await store.TryReserveAsync(key, "huella", CancellationToken.None);
        var second = await store.TryReserveAsync(key, "huella", CancellationToken.None);

        Assert.That(first, Is.InstanceOf<IdempotencyReservation.Reserved>());
        Assert.That(second, Is.InstanceOf<IdempotencyReservation.InProgress>());
    }

    [Test]
    public async Task ElResultadoSeConservaEntreReiniciosDelServidor()
    {
        var key = Guid.NewGuid();
        var store = NewStore();
        await store.TryReserveAsync(key, "huella", CancellationToken.None);
        await store.CompleteAsync(Attempt(key, SaleAttemptStatus.Registered), CancellationToken.None);

        var restarted = NewStore();
        var reservation = await restarted.TryReserveAsync(key, "huella", CancellationToken.None);

        var completed = (IdempotencyReservation.Completed)reservation;
        Assert.That(completed.Attempt.Result!.OrderName, Is.EqualTo("POS - 7"));
        Assert.That(completed.Attempt.Result.PaymentMethodType, Is.EqualTo(PaymentMethodType.Cash));
    }

    [Test]
    public async Task UnaHuellaDistintaConLaMismaClaveSeDetecta()
    {
        var key = Guid.NewGuid();
        var store = NewStore();
        await store.TryReserveAsync(key, "huella", CancellationToken.None);
        await store.CompleteAsync(Attempt(key, SaleAttemptStatus.Registered), CancellationToken.None);

        Assert.That(await store.TryReserveAsync(key, "otra", CancellationToken.None), Is.InstanceOf<IdempotencyReservation.FingerprintMismatch>());
    }

    [Test]
    public async Task UnIntentoInciertoSePuedeVolverAReservarParaConciliar()
    {
        var key = Guid.NewGuid();
        var store = NewStore();
        await store.TryReserveAsync(key, "huella", CancellationToken.None);
        await store.CompleteAsync(Attempt(key, SaleAttemptStatus.Uncertain), CancellationToken.None);

        var reservation = await store.TryReserveAsync(key, "huella", CancellationToken.None);

        Assert.That(((IdempotencyReservation.Reserved)reservation).Previous!.Status, Is.EqualTo(SaleAttemptStatus.Uncertain));
    }

    [Test]
    public async Task LiberarLaReservaPermiteReintentarSinGuardarNada()
    {
        var key = Guid.NewGuid();
        var store = NewStore();
        await store.TryReserveAsync(key, "huella", CancellationToken.None);

        await store.ReleaseAsync(key);

        Assert.That(await store.TryReserveAsync(key, "huella", CancellationToken.None), Is.InstanceOf<IdempotencyReservation.Reserved>());
        Assert.That(await store.FindAsync(key, CancellationToken.None), Is.Null);
    }

    [Test]
    public async Task UnArchivoDanadoNoImpideVenderYSeRespalda()
    {
        await File.WriteAllTextAsync(_path, "{ esto no es json");
        var store = NewStore();

        var reservation = await store.TryReserveAsync(Guid.NewGuid(), "huella", CancellationToken.None);

        Assert.That(reservation, Is.InstanceOf<IdempotencyReservation.Reserved>());
        Assert.That(Directory.GetFiles(Path.GetDirectoryName(_path)!, Path.GetFileName(_path) + ".*.corrupt"), Is.Not.Empty);
        foreach (var backup in Directory.GetFiles(Path.GetDirectoryName(_path)!, Path.GetFileName(_path) + ".*.corrupt"))
        {
            File.Delete(backup);
        }
    }
}

[TestFixture]
public sealed class Pbkdf2PasswordHasherTests
{
    private readonly Pbkdf2PasswordHasher _hasher = new();

    [Test]
    public void VerificaLaContrasenaCorrectaYRechazaLasDemas()
    {
        var hash = _hasher.Hash("Cajero2026!");

        Assert.That(_hasher.Verify("Cajero2026!", hash), Is.True);
        Assert.That(_hasher.Verify("cajero2026!", hash), Is.False);
        Assert.That(hash, Does.StartWith("PBKDF2$SHA256$210000$"));
        Assert.That(hash, Does.Not.Contain("Cajero2026!"));
    }

    [Test]
    public void LaMismaContrasenaProduceHashesDistintosPorLaSal() =>
        Assert.That(_hasher.Hash("igual"), Is.Not.EqualTo(_hasher.Hash("igual")));

    [TestCase("")]
    [TestCase("texto-plano")]
    [TestCase("PBKDF2$SHA256$10$AAAA$BBBB")]
    [TestCase("PBKDF2$SHA256$210000$no-es-base64$@@")]
    public void UnHashMalFormadoNuncaSeAcepta(string hash) => Assert.That(_hasher.Verify("x", hash), Is.False);
}

[TestFixture]
public sealed class OdooProductCatalogTests
{
    [Test]
    public void LaBusquedaFiltraPorPosTextoCategoriaYExistencias()
    {
        var criteria = new ProductSearchCriteria("caf", 5, OnlyInStock: true, ProductSort.PriceDesc, PageRequest.Create(2, 10, 24).Value);

        var domain = System.Text.Json.JsonSerializer.Serialize(
            OdooProductCatalog.BuildDomain(criteria, supportsStock: true),
            new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

        // El texto se busca en la plantilla con "any" para que Odoo genere una subconsulta indexable (rendimiento con 1M).
        Assert.That(domain, Does.StartWith("""[["available_in_pos","=",true],["sale_ok","=",true],["product_tmpl_id","any",["|","|",["name","ilike","caf"],["default_code","ilike","caf"],["barcode","ilike","caf"]]]"""));
        Assert.That(domain, Does.Contain("""["pos_categ_ids","child_of",5]"""));
        Assert.That(domain, Does.Contain("""["qty_available",">",0]"""));
        Assert.That(OdooProductCatalog.OrderBy(ProductSort.PriceDesc), Does.StartWith("list_price desc"));
        Assert.That(criteria.Page.Offset, Is.EqualTo(10));
    }

    [Test]
    public async Task MapeaLosProductosConSusImpuestosYValoresVacios()
    {
        var host = new OdooTestHost();
        host.Handler
            .On("product.product/fields_get", _ => FakeHttpHandler.Json(new { qty_available = new { type = "float" }, is_storable = new { type = "boolean" } }))
            .On("product.product/search_read", _ => FakeHttpHandler.Json(new object[]
            {
                new
                {
                    id = 14, name = "Café", default_code = false, barcode = "770", lst_price = 3.5,
                    taxes_id = new[] { 18 }, pos_categ_ids = new[] { 5 }, available_in_pos = true, sale_ok = true, active = true,
                    qty_available = 0.0, is_storable = true,
                },
            }))
            .On("account.tax/read", _ => FakeHttpHandler.Json(new object[]
            {
                new { id = 18, name = "IVA 19 %", amount = 19.0, amount_type = "percent", price_include = false, include_base_amount = false },
            }))
            .On("pos.category/search_read", _ => FakeHttpHandler.Json(new object[] { new { id = 5, display_name = "Bebidas" } }));

        var result = await host.Catalog.GetByIdsAsync([14], CancellationToken.None);

        var product = result.Value.Single();
        Assert.Multiple(() =>
        {
            Assert.That(product.Code, Is.Null, "false de Odoo => sin código");
            Assert.That(product.UnitPrice, Is.EqualTo(3.5m));
            Assert.That(product.Taxes.Single().Rate, Is.EqualTo(0.19m));
            Assert.That(product.Categories.Single().Name, Is.EqualTo("Bebidas"));
            Assert.That(product.Availability.Status, Is.EqualTo(AvailabilityStatus.OutOfStock));
        });
    }

    [Test]
    public async Task UnErrorDeOdooEnElCatalogoSeTraduceSinExponerDetallesTecnicos()
    {
        var host = new OdooTestHost();
        host.Handler.On("product.product/fields_get", _ => FakeHttpHandler.OdooError(HttpStatusCode.Unauthorized, "werkzeug.exceptions.Unauthorized", "Invalid apikey"));
        host.Handler.On("product.product/search_read", _ => FakeHttpHandler.OdooError(HttpStatusCode.Unauthorized, "werkzeug.exceptions.Unauthorized", "Invalid apikey"));

        var result = await host.Catalog.GetByIdsAsync([14], CancellationToken.None);

        Assert.That(result.FirstError.Code, Is.EqualTo("ODOO.UNAUTHORIZED"));
        Assert.That(result.FirstError.Message, Does.Not.Contain("apikey"));
    }
}
