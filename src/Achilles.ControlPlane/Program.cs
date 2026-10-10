using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Achilles.ControlPlane;
using Achilles.ControlPlane.Endpoints;
using Achilles.Crypto;
using Achilles.Data;
using Achilles.Data.Entities;
using Achilles.Data.Stores;
using Achilles.Domain;
using System.Security.Cryptography;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Enforce §9.3 Rule 4: Plaintext PKCS#8 private keys are forbidden in Production
Achilles.ControlPlane.Security.ProductionKeySafetyGuard.EnforceSafetyRules(builder.Configuration, builder.Environment);

// DB Configuration
string? connStr = builder.Configuration.GetConnectionString("AchillesDb")
    ?? builder.Configuration.GetConnectionString("SymbolonDb");
builder.Services.AddDbContext<AchillesDbContext>(options =>
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
        options.UseSqlite("Data Source=achilles_controlplane.db");
    }
});

// Crypto & Keys
var es256Key = Es256SignatureProvider.GenerateKey("cp-es256-key");
var pqcKey = MlDsaSignatureProvider.GenerateKey(MLDsaAlgorithm.MLDsa65, "cp-pqc-key");
var keyRing = new AchillesKeyRing();
keyRing.Add(es256Key);
keyRing.Add(pqcKey);

builder.Services.AddSingleton(keyRing);
builder.Services.AddSingleton<IKeyRing>(keyRing);
builder.Services.AddSingleton<ISignatureProvider>(es256Key);

if (MlKemKeyEncapsulationProvider.IsSupported)
{
    var kemKey = MlKemKeyEncapsulationProvider.GenerateKey(MLKemAlgorithm.MLKem768, "cp-kem-key");
    builder.Services.AddSingleton<IKeyEncapsulationProvider>(kemKey);
}

// Services
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddSingleton<Achilles.ControlPlane.Observability.AchillesMetrics>();
builder.Services.AddSingleton<Achilles.ControlPlane.Observability.AchillesTraceBuffer>();
builder.Services.AddScoped<Achilles.ControlPlane.Security.KeyManager>();
builder.Services.AddScoped<ISeatStore, EfSeatStore>();
builder.Services.AddScoped<Achilles.Domain.Grants.ISeatGrantStore, Achilles.Data.Stores.EfSeatGrantStore>();
builder.Services.AddScoped<IAuditLedger, EfAuditLedger>();
builder.Services.AddScoped<Achilles.Domain.Tokens.ITokenStore, Achilles.Data.Stores.EfTokenStore>();
builder.Services.AddScoped<Achilles.Domain.Tokens.ITokenEngine, Achilles.Domain.Tokens.TokenEngine>();
builder.Services.AddScoped<ILeaseTokenIssuer>(sp =>
    new ControlPlaneLeaseTokenIssuer(sp.GetRequiredService<ISignatureProvider>()));
builder.Services.AddHttpClient();
builder.Services.AddSingleton<Achilles.Domain.Webhooks.IWebhookQueue, Achilles.Domain.Webhooks.WebhookQueue>();
builder.Services.AddScoped<Achilles.Domain.Webhooks.IWebhookStore, Achilles.Data.Stores.EfWebhookStore>();
builder.Services.AddSingleton<Achilles.Domain.Lifecycle.LicenseLifecycleEngine>();
builder.Services.AddSingleton<Achilles.ControlPlane.Webhooks.IWebhookDispatcher, Achilles.ControlPlane.Webhooks.WebhookDispatcher>();
builder.Services.AddHostedService<Achilles.ControlPlane.Services.WebhookBackgroundService>();
builder.Services.AddHostedService<Achilles.ControlPlane.Services.DiscoveryResponderService>();
builder.Services.AddSingleton<Achilles.ControlPlane.Queuing.IQueueManager, Achilles.ControlPlane.Queuing.QueueManager>();
builder.Services.AddHostedService<Achilles.ControlPlane.Services.QueueReaperBackgroundService>();
builder.Services.AddSingleton<Achilles.ControlPlane.Services.ScimDirectorySyncWorker>();
builder.Services.AddSingleton<Achilles.ControlPlane.Services.IScimDirectorySyncWorker>(sp => sp.GetRequiredService<Achilles.ControlPlane.Services.ScimDirectorySyncWorker>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<Achilles.ControlPlane.Services.ScimDirectorySyncWorker>());
builder.Services.AddSingleton<Achilles.Domain.Borrow.IReturnChallengeStore, Achilles.Domain.Borrow.InMemoryReturnChallengeStore>();
builder.Services.AddScoped<Achilles.ControlPlane.Alerting.IAlertService, Achilles.ControlPlane.Alerting.AlertService>();
builder.Services.AddSingleton<Achilles.Relay.Mesh.IRelayMeshCoordinator>(_ =>
    new Achilles.Relay.Mesh.RelayMeshCoordinator("cp_primary_cluster", Enumerable.Range(1, 100)));
builder.Services.AddSingleton<Achilles.Domain.Replication.IGeoReplicationEngine>(_ =>
{
    var engine = new Achilles.Domain.Replication.GeoReplicationEngine("eu-central-1", seatRangeStart: 1, seatRangeEnd: 100);
    engine.RegisterPeer("us-east-1", "https://us.symbolon.internal/v1/replication", 101, 200);
    engine.RegisterPeer("ap-southeast-1", "https://ap.symbolon.internal/v1/replication", 201, 300);
    return engine;
});
builder.Services.AddSingleton<Achilles.Domain.Enforcement.IEbpfEnforcementEngine, Achilles.Domain.Enforcement.EbpfEnforcementEngine>();
builder.Services.AddSingleton<Achilles.Domain.Security.IFraudDetectionService, Achilles.Domain.Security.FraudDetectionService>();
builder.Services.AddSingleton<Achilles.ControlPlane.Security.Sso.SsoSessionManager>();
builder.Services.AddSingleton<Achilles.ControlPlane.Security.Sso.SsoEngine>();
builder.Services.AddSingleton<Achilles.ControlPlane.Security.KmsHierarchyManager>();
builder.Services.AddSingleton<Achilles.ControlPlane.Events.IClusterEventBroadcaster, Achilles.ControlPlane.Events.ClusterEventBroadcaster>();
builder.Services.AddSingleton<Achilles.ControlPlane.Compliance.ComplianceBundleService>();
builder.Services.AddScoped<Achilles.Domain.Entitlements.IFeatureEntitlementStore, Achilles.Data.Stores.EfFeatureEntitlementStore>();
builder.Services.AddScoped<Achilles.Domain.Entitlements.FeatureEntitlementEngine>();
builder.Services.AddScoped<Achilles.Domain.Experiments.IExperimentStore, Achilles.Data.Stores.EfExperimentStore>();
builder.Services.AddScoped<Achilles.ControlPlane.Billing.BillingLicenseProvisioner>();
builder.Services.AddScoped<LeaseEngine>();

// Rate Limiting (STRIDE T11, T12, SEC-05: Partitioned by Client IP / Admin Key)
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("public-leases", httpContext =>
    {
        var partitionKey = httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault()
            ?? httpContext.Connection.RemoteIpAddress?.ToString()
            ?? "unknown_client";
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10000,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });

    options.AddPolicy("admin", httpContext =>
    {
        var partitionKey = httpContext.User.Identity?.Name
            ?? httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault()
            ?? httpContext.Connection.RemoteIpAddress?.ToString()
            ?? "unknown_admin";
        return RateLimitPartition.GetSlidingWindowLimiter(partitionKey, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 5000,
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 4,
            QueueLimit = 0
        });
    });

    options.AddPolicy("relay", httpContext =>
    {
        var partitionKey = httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault()
            ?? httpContext.Connection.RemoteIpAddress?.ToString()
            ?? "unknown_relay";
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5000,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });
});

// OpenAPI 3.1
builder.Services.AddOpenApi();

// Authentication & Authorization (STRIDE T8, T10, §9.6)
builder.Services.AddAuthentication(Achilles.ControlPlane.Security.ApiKeyAuthenticationOptions.DefaultScheme)
    .AddScheme<Achilles.ControlPlane.Security.ApiKeyAuthenticationOptions, Achilles.ControlPlane.Security.ApiKeyAuthenticationHandler>(
        Achilles.ControlPlane.Security.ApiKeyAuthenticationOptions.DefaultScheme, _ => { });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("SuperAdminOnly", policy => policy.RequireRole("admin:super"));
    options.AddPolicy("AdminOrAuditor", policy => policy.RequireRole("admin:super", "admin:tenant", "auditor"));
});

var app = builder.Build();

// Ensure DB is created on startup and default tenant seeded
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AchillesDbContext>();
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

// Eagerly initialize OpenTelemetry trace buffer listener
_ = app.Services.GetRequiredService<Achilles.ControlPlane.Observability.AchillesTraceBuffer>();

app.MapOpenApi();

// Observability & Metrics
app.MapGet("/metrics", (Achilles.ControlPlane.Observability.AchillesMetrics metrics) =>
    Results.Content(metrics.GeneratePrometheusMetrics(), "text/plain; version=0.0.4"))
   .WithTags("Observability");

// Health endpoints
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTimeOffset.UtcNow }))
   .WithTags("Health");

app.MapGet("/health/live", () => Results.Ok(new { status = "alive", timestamp = DateTimeOffset.UtcNow }))
   .WithTags("Health");

app.MapGet("/health/ready", async (AchillesDbContext db, CancellationToken ct) =>
{
    bool canConnect = await db.Database.CanConnectAsync(ct).ConfigureAwait(false);
    return canConnect
        ? Results.Ok(new { status = "ready", database = "connected", timestamp = DateTimeOffset.UtcNow })
        : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
}).WithTags("Health");

app.MapGet("/v1/system/mesh", (Achilles.Relay.Mesh.IRelayMeshCoordinator mesh) =>
    Results.Ok(new
    {
        localNodeId = mesh.LocalNodeId,
        lamportClock = mesh.CurrentClock,
        peers = mesh.GetPeers()
    })).WithTags("Mesh");

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
app.MapAdminEntitlementEndpoints().RequireAuthorization().RequireRateLimiting("admin");
app.MapTransparencyEndpoints().RequireRateLimiting("public-leases");
app.MapComplianceEndpoints().RequireRateLimiting("public-leases");
app.MapAdminEndpoints().RequireAuthorization().RequireRateLimiting("admin");
app.MapApiKeyEndpoints().RequireAuthorization("SuperAdminOnly").RequireRateLimiting("admin");
app.MapWebhookEndpoints().RequireAuthorization().RequireRateLimiting("admin");
app.MapRelaySyncEndpoints().RequireRateLimiting("relay");
app.MapOfflineEndpoints();
app.MapReplicationEndpoints();
app.MapEbpfEndpoints();
app.MapScimEndpoints();
app.MapSsoEndpoints();
app.MapKmsEndpoints();
app.MapTokenEndpoints();
app.MapMigrationEndpoints();
app.MapLicenseLifecycleEndpoints().RequireAuthorization().RequireRateLimiting("admin");
app.MapBillingEndpoints();
app.MapVisualizerEndpoints();
app.MapAnalyticsEndpoints().RequireAuthorization().RequireRateLimiting("admin");

// SPA Fallback
app.MapFallbackToFile("index.html");

app.Run();

#pragma warning disable CA1515
public partial class Program { }
#pragma warning restore CA1515
