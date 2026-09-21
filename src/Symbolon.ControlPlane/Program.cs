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
builder.Services.AddSingleton<Symbolon.ControlPlane.Queuing.IQueueManager, Symbolon.ControlPlane.Queuing.QueueManager>();
builder.Services.AddScoped<Symbolon.ControlPlane.Alerting.IAlertService, Symbolon.ControlPlane.Alerting.AlertService>();
builder.Services.AddScoped<LeaseEngine>();

// Rate Limiting (STRIDE T11, T12, SEC-05: Partitioned by Client IP / Admin Key)
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("public-leases", httpContext =>
    {
        var partitionKey = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown_client";
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 100,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });

    options.AddPolicy("admin", httpContext =>
    {
        var partitionKey = httpContext.User.Identity?.Name
            ?? httpContext.Connection.RemoteIpAddress?.ToString()
            ?? "unknown_admin";
        return RateLimitPartition.GetSlidingWindowLimiter(partitionKey, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 200,
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 4,
            QueueLimit = 0
        });
    });

    options.AddPolicy("relay", httpContext =>
    {
        var partitionKey = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown_relay";
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 120,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });
});

// OpenAPI 3.1
builder.Services.AddOpenApi();

// Authentication & Authorization (STRIDE T8, T10, §9.6)
builder.Services.AddAuthentication(Symbolon.ControlPlane.Security.ApiKeyAuthenticationOptions.DefaultScheme)
    .AddScheme<Symbolon.ControlPlane.Security.ApiKeyAuthenticationOptions, Symbolon.ControlPlane.Security.ApiKeyAuthenticationHandler>(
        Symbolon.ControlPlane.Security.ApiKeyAuthenticationOptions.DefaultScheme, _ => { });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("SuperAdminOnly", policy => policy.RequireRole("admin:super"));
    options.AddPolicy("AdminOrAuditor", policy => policy.RequireRole("admin:super", "admin:tenant", "auditor"));
});

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

// Security Headers Middleware (SEC-06)
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    context.Response.Headers.Append("Content-Security-Policy", "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; font-src 'self'; img-src 'self' data:; connect-src 'self'");
    context.Response.Headers.Append("Permissions-Policy", "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()");
    if (!app.Environment.IsDevelopment())
    {
        context.Response.Headers.Append("Strict-Transport-Security", "max-age=31536000; includeSubDomains");
    }
    await next().ConfigureAwait(false);
});

// Static Files & Web Dashboard
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// Map Endpoints with Rate Limiting (STRIDE T11, T12)
app.MapPublicEndpoints().RequireRateLimiting("public-leases");
app.MapEntitlementEndpoints().RequireRateLimiting("public-leases");
app.MapTransparencyEndpoints().RequireRateLimiting("public-leases");
app.MapComplianceEndpoints().RequireRateLimiting("public-leases");
app.MapAdminEndpoints().RequireAuthorization().RequireRateLimiting("admin");
app.MapApiKeyEndpoints().RequireAuthorization("SuperAdminOnly").RequireRateLimiting("admin");
app.MapWebhookEndpoints().RequireAuthorization().RequireRateLimiting("admin");
app.MapRelaySyncEndpoints().RequireRateLimiting("relay");
app.MapOfflineEndpoints();

// SPA Fallback
app.MapFallbackToFile("index.html");

app.Run();

#pragma warning disable CA1515
public partial class Program { }
#pragma warning restore CA1515
