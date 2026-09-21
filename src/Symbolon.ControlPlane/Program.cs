using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Symbolon.ControlPlane;
using Symbolon.ControlPlane.Endpoints;
using Symbolon.Crypto;
using Symbolon.Data;
using Symbolon.Data.Entities;
using Symbolon.Data.Stores;
using Symbolon.Domain;
using System.Threading.RateLimiting;

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
builder.Services.AddScoped<Symbolon.ControlPlane.Security.KeyManager>();
builder.Services.AddScoped<ISeatStore, EfSeatStore>();
builder.Services.AddScoped<IAuditLedger, EfAuditLedger>();
builder.Services.AddScoped<ILeaseTokenIssuer>(sp =>
    new ControlPlaneLeaseTokenIssuer(sp.GetRequiredService<ISignatureProvider>()));
builder.Services.AddHttpClient();
builder.Services.AddSingleton<Symbolon.ControlPlane.Webhooks.IWebhookDispatcher, Symbolon.ControlPlane.Webhooks.WebhookDispatcher>();
builder.Services.AddScoped<LeaseEngine>();

// Rate Limiting (STRIDE T11, T12)
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddFixedWindowLimiter("public-leases", opt =>
    {
        opt.PermitLimit = 100;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 0;
    });

    options.AddSlidingWindowLimiter("admin", opt =>
    {
        opt.PermitLimit = 200;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.SegmentsPerWindow = 4;
        opt.QueueLimit = 0;
    });

    options.AddFixedWindowLimiter("relay", opt =>
    {
        opt.PermitLimit = 120;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 0;
    });
});

// OpenAPI 3.1
builder.Services.AddOpenApi();

var app = builder.Build();

// Ensure DB is created on startup and default tenant seeded
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SymbolonDbContext>();
    db.Database.EnsureCreated();
    if (!db.Tenants.Any())
    {
        db.Tenants.Add(new Tenant
        {
            Id = "ten_default",
            Slug = "default",
            Name = "Default Organization",
            CreatedAt = DateTimeOffset.UtcNow
        });
        db.SaveChanges();
    }
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

// Static Files & Web Dashboard
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseRateLimiter();

// Map Endpoints with Rate Limiting (STRIDE T11, T12)
app.MapPublicEndpoints().RequireRateLimiting("public-leases");
app.MapAdminEndpoints().RequireRateLimiting("admin");
app.MapWebhookEndpoints().RequireRateLimiting("admin");
app.MapRelaySyncEndpoints().RequireRateLimiting("relay");
app.MapOfflineEndpoints();

// SPA Fallback
app.MapFallbackToFile("index.html");

app.Run();

#pragma warning disable CA1515
public partial class Program { }
#pragma warning restore CA1515
