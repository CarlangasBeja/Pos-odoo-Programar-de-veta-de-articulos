using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Pos.Api;
using Pos.Application.Contracts.Gateways;
using Pos.Infrastructure.Security;
using Pos.UnitTests.Fakes;

namespace Pos.ApiTests;

/// <summary>API real escuchando en un puerto libre, con Odoo reemplazado por dobles de prueba.</summary>
public sealed class ApiTestServer : IAsyncDisposable
{
    public const string CashierPassword = "Cajero2026!";
    public const string SupervisorPassword = "Supervisor2026!";

    private readonly WebApplication _app;
    private readonly string _attemptsFile;

    private ApiTestServer(WebApplication app, HttpClient client, string attemptsFile)
    {
        _app = app;
        Client = client;
        _attemptsFile = attemptsFile;
    }

    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web);

    public HttpClient Client { get; }

    public FakeProductCatalog Catalog { get; private init; } = null!;

    public FakePosSessionGateway Sessions { get; private init; } = null!;

    public FakePosOrderGateway Orders { get; private init; } = null!;

    public static async Task<ApiTestServer> StartAsync()
    {
        var hasher = new Pbkdf2PasswordHasher();
        var catalog = new FakeProductCatalog(TestData.Cafe(), TestData.Agua(), TestData.Pan(), TestData.NoDisponibleEnPos());
        var sessions = new FakePosSessionGateway(TestData.OpenSession());
        var orders = new FakePosOrderGateway();
        var attemptsFile = Path.Combine(Path.GetTempPath(), $"pos-api-tests-{Guid.NewGuid():N}.json");

        var app = PosApiHost.Build(["--urls", "http://127.0.0.1:0", "--environment", "Testing"], builder =>
        {
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Odoo:BaseUrl"] = "http://127.0.0.1:9", // nadie escucha aquí: el health check debe informar el problema
                ["Odoo:Database"] = "pruebas",
                ["Odoo:ApiKey"] = "clave-de-prueba",
                ["Odoo:PosConfigId"] = "1",
                ["Idempotency:FilePath"] = attemptsFile,
                ["Operators:Accounts:0:Username"] = "cajero",
                ["Operators:Accounts:0:DisplayName"] = "Ana Cajera",
                ["Operators:Accounts:0:Role"] = "Cashier",
                ["Operators:Accounts:0:PasswordHash"] = hasher.Hash(CashierPassword),
                ["Operators:Accounts:1:Username"] = "supervisor",
                ["Operators:Accounts:1:DisplayName"] = "Carlos Supervisor",
                ["Operators:Accounts:1:Role"] = "Supervisor",
                ["Operators:Accounts:1:PasswordHash"] = hasher.Hash(SupervisorPassword),
            });
            builder.Logging.ClearProviders();
            builder.Services.RemoveAll<IProductCatalog>().AddSingleton<IProductCatalog>(catalog);
            builder.Services.RemoveAll<IPosSessionGateway>().AddSingleton<IPosSessionGateway>(sessions);
            builder.Services.RemoveAll<IPosOrderGateway>().AddSingleton<IPosOrderGateway>(orders);
        });

        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        var client = new HttpClient { BaseAddress = new Uri(address) };
        return new ApiTestServer(app, client, attemptsFile) { Catalog = catalog, Sessions = sessions, Orders = orders };
    }

    public async Task<HttpClient> LoginAsync(string username = "cajero", string password = CashierPassword)
    {
        var response = await Client.PostAsJsonAsync("/api/auth/login", new { username, password });
        response.EnsureSuccessStatusCode();
        var token = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();
        var authenticated = new HttpClient { BaseAddress = Client.BaseAddress };
        authenticated.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return authenticated;
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
        if (File.Exists(_attemptsFile))
        {
            File.Delete(_attemptsFile);
        }
    }
}
