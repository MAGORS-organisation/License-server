using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Symbolon.Crypto;
using Symbolon.Domain;
using Symbolon.Relay;

var builder = WebApplication.CreateBuilder(args);

// Register Core Cryptographic & Store dependencies via factories so container manages lifecycle
builder.Services.AddSingleton<ISignatureProvider>(_ => Es256SignatureProvider.GenerateKey("lease-relay-2026"));
builder.Services.AddSingleton<IKeyRing>(sp =>
{
    var ring = new SymbolonKeyRing();
    var key = sp.GetRequiredService<ISignatureProvider>();
    ring.Add(key);
    return ring;
});

// Sqlite storage
builder.Services.AddSingleton<SqliteSeatStore>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    string connectionString = config["Database:ConnectionString"] ?? "Data Source=symbolon-relay.db";
    return new SqliteSeatStore(connectionString);
});
builder.Services.AddSingleton<ISeatStore>(sp => sp.GetRequiredService<SqliteSeatStore>());

// Token issuer & audit
builder.Services.AddSingleton<ILeaseTokenIssuer>(sp =>
{
    var key = sp.GetRequiredService<ISignatureProvider>();
    return new RelayLeaseTokenIssuer(key, "relay-main");
});
builder.Services.AddSingleton<IAuditLedger, InMemoryAuditLedger>();
builder.Services.AddSingleton(TimeProvider.System);

// Domain Engine
builder.Services.AddSingleton<LeaseEngine>();

builder.Services.AddHostedService<RelayDiscoveryResponderService>();

var app = builder.Build();

// Endpoints
app.MapGet("/health", () => Results.Ok(new { status = "healthy", runtime = ".NET 10" }));

app.MapGet("/v1/.well-known/symbolon-keys", (ISignatureProvider key) =>
{
    var jwk = key.ExportPublicJwk();
    return Results.Ok(new JsonWebKeySetDto { Keys = [jwk] });
});

app.MapLeases();

app.Run();

// Make Program public for WebApplicationFactory in integration tests
#pragma warning disable CA1515
public partial class Program { }
#pragma warning restore CA1515
