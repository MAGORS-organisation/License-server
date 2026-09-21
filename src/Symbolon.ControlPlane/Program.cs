using Microsoft.EntityFrameworkCore;
using Symbolon.ControlPlane;
using Symbolon.ControlPlane.Endpoints;
using Symbolon.Crypto;
using Symbolon.Data;
using Symbolon.Data.Stores;
using Symbolon.Domain;

var builder = WebApplication.CreateBuilder(args);

// DB Configuration
string? connStr = builder.Configuration.GetConnectionString("SymbolonDb");
builder.Services.AddDbContext<SymbolonDbContext>(options =>
{
    if (!string.IsNullOrWhiteSpace(connStr))
    {
        if (connStr.Contains(".db", StringComparison.OrdinalIgnoreCase) || connStr.Contains("Data Source=", StringComparison.OrdinalIgnoreCase))
        {
            options.UseSqlite(connStr);
        }
        else
        {
            options.UseNpgsql(connStr);
        }
    }
    else
    {
        options.UseSqlite("Data Source=symbolon_controlplane.db");
    }
});

// Crypto & Keys
var es256Key = Es256SignatureProvider.GenerateKey("cp-es256-key");
var keyRing = new SymbolonKeyRing();
keyRing.Add(es256Key);

builder.Services.AddSingleton(keyRing);
builder.Services.AddSingleton<ISignatureProvider>(es256Key);

// Services
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddSingleton<Symbolon.ControlPlane.Observability.SymbolonMetrics>();
builder.Services.AddScoped<ISeatStore, EfSeatStore>();
builder.Services.AddScoped<IAuditLedger, EfAuditLedger>();
builder.Services.AddScoped<ILeaseTokenIssuer>(sp =>
    new ControlPlaneLeaseTokenIssuer(sp.GetRequiredService<ISignatureProvider>()));
builder.Services.AddScoped<LeaseEngine>();

// OpenAPI 3.1
builder.Services.AddOpenApi();

var app = builder.Build();

// Ensure DB is created on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SymbolonDbContext>();
    db.Database.EnsureCreated();
}

app.MapOpenApi();

// Observability & Metrics
app.MapGet("/metrics", (Symbolon.ControlPlane.Observability.SymbolonMetrics metrics) =>
    Results.Content(metrics.GeneratePrometheusMetrics(), "text/plain; version=0.0.4"))
   .WithTags("Observability");

// Health endpoints
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTimeOffset.UtcNow }))
   .WithTags("Health");

app.MapGet("/health/live", () => Results.Ok(new { status = "alive", timestamp = DateTimeOffset.UtcNow }))
   .WithTags("Health");

app.MapGet("/health/ready", async (SymbolonDbContext db, CancellationToken ct) =>
{
    bool canConnect = await db.Database.CanConnectAsync(ct).ConfigureAwait(false);
    return canConnect
        ? Results.Ok(new { status = "ready", database = "connected", timestamp = DateTimeOffset.UtcNow })
        : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
}).WithTags("Health");

// Map Endpoints
app.MapPublicEndpoints();
app.MapAdminEndpoints();
app.MapRelaySyncEndpoints();

app.Run();

#pragma warning disable CA1515
public partial class Program { }
#pragma warning restore CA1515
