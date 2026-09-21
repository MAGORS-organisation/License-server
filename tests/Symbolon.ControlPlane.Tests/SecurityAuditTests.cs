using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Symbolon.ControlPlane.Models;
using Symbolon.Data;
using Symbolon.Data.Entities;
using Xunit;

namespace Symbolon.ControlPlane.Tests;

public sealed class SecurityAuditTests : IClassFixture<ControlPlaneFactory>
{
    private readonly ControlPlaneFactory _factory;
    private readonly HttpClient _client;

    public SecurityAuditTests(ControlPlaneFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task SEC01_FailClosed_Anonymous_Request_Rejected_When_Bypass_Disabled()
    {
        // Custom factory where dev super admin bypass is explicitly disabled
        var secureFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Security:EnableDevSuperAdminBypass", "false");
        });

        var secureClient = secureFactory.CreateClient();

        var response = await secureClient.GetAsync("/admin/v1/products");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SEC02_Checkout_Rejects_KeyLookup_Collision_When_KeyHash_Mismatches()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SymbolonDbContext>();

        // Generate a fake license in DB with a specific KeyLookup and KeyHash
        string genuineKey = "SYM-GENUINE-KEY-AAAA-BBBB";
        byte[] rawBytes = Encoding.UTF8.GetBytes(genuineKey);
        byte[] lookup = SHA256.HashData(rawBytes)[..4];
        string genuineHash = Convert.ToHexString(SHA256.HashData(rawBytes));

        string licenseId = $"lic_sec02_{Guid.NewGuid():N}";
        var tenant = await db.Tenants.FirstOrDefaultAsync() ?? new Tenant { Id = "ten_default", Name = "Default Org", Slug = "default", CreatedAt = DateTimeOffset.UtcNow };
        if (!await db.Tenants.AnyAsync(t => t.Id == tenant.Id))
        {
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
        }

        var product = await db.Products.FirstOrDefaultAsync() ?? new Product { Id = "prd_sec02", TenantId = tenant.Id, Code = "PRD-SEC02", Name = "Product SEC", PlatformsJson = "[\"windows\"]", CreatedAt = DateTimeOffset.UtcNow };
        if (!await db.Products.AnyAsync(p => p.Id == product.Id))
        {
            db.Products.Add(product);
            await db.SaveChangesAsync();
        }

        var policy = await db.Policies.FirstOrDefaultAsync() ?? new Policy { Id = "pol_sec02", TenantId = tenant.Id, ProductId = product.Id, Code = "POL-SEC02", Name = "Policy SEC", LicenseModel = "floating", MaxSeats = 10, LeaseTtlSeconds = 600, GraceTtlSeconds = 1200, EntitlementsJson = "[\"core\"]" };
        if (!await db.Policies.AnyAsync(p => p.Id == policy.Id))
        {
            db.Policies.Add(policy);
            await db.SaveChangesAsync();
        }

        var license = new LicenseEntity
        {
            Id = licenseId,
            TenantId = policy.TenantId,
            PolicyId = policy.Id,
            KeyLookup = lookup,
            KeyHash = genuineHash,
            State = "active",
            MaxSeats = 5,
            IssuedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Licenses.Add(license);

        db.Seats.Add(new SeatEntity
        {
            LicenseId = licenseId,
            SeatNo = 1,
            IsOverage = false
        });
        await db.SaveChangesAsync();

        // Create a colliding key candidate that has arbitrary different text
        // Even if an attacker managed to craft a key with matching lookup, the full hash won't match
        string collidingCandidateKey = "SYM-COLLIDING-KEY-CCCC-DDDD";
        // Override lookup of colliding key in DB or test using the license lookup
        // We simulate that the DB found this license because candidate key has same lookup:
        byte[] candidateLookup = SHA256.HashData(Encoding.UTF8.GetBytes(collidingCandidateKey))[..4];
        license.KeyLookup = candidateLookup; // Artificially simulate 32-bit collision!
        await db.SaveChangesAsync();

        // Now attacker sends collidingCandidateKey. Its lookup matches DB, but its KeyHash DOES NOT!
        var checkoutReq = new HttpRequestMessage(HttpMethod.Post, "/v1/leases")
        {
            Content = JsonContent.Create(new
            {
                licenseKey = collidingCandidateKey,
                machineId = "mch_attacker_1",
                quantity = 1,
                fingerprintComponents = new Dictionary<string, string> { { "cpu", "test_cpu" } }
            })
        };

        var response = await _client.SendAsync(checkoutReq);

        // SEC-02 fix must reject with 404 License Not Found
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SEC03_Webhook_Blocks_SSRF_Target_Urls()
    {
        // Try creating webhook pointing to AWS IMDS metadata service
        var imdsReq = await _client.PostAsJsonAsync("/admin/v1/webhooks", new CreateWebhookDto(
            "http://169.254.169.254/latest/meta-data/",
            ["license.created"],
            null));

        Assert.Equal(HttpStatusCode.BadRequest, imdsReq.StatusCode);

        // Try creating webhook pointing to Google Cloud metadata service
        var gcpReq = await _client.PostAsJsonAsync("/admin/v1/webhooks", new CreateWebhookDto(
            "http://metadata.google.internal/computeMetadata/v1/",
            ["license.created"],
            null));

        Assert.Equal(HttpStatusCode.BadRequest, gcpReq.StatusCode);
    }

    [Fact]
    public async Task SEC04_Tenant_Isolation_Enforced_And_Privilege_Escalation_Prevented()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SymbolonDbContext>();

        // Seed 2 tenants
        string tenantA = $"ten_sec_a_{Guid.NewGuid():N}";
        string tenantB = $"ten_sec_b_{Guid.NewGuid():N}";

        db.Tenants.Add(new Tenant { Id = tenantA, Name = "Tenant A", Slug = $"slug-a-{Guid.NewGuid():N}"[..12], CreatedAt = DateTimeOffset.UtcNow });
        db.Tenants.Add(new Tenant { Id = tenantB, Name = "Tenant B", Slug = $"slug-b-{Guid.NewGuid():N}"[..12], CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        // Issue API key for Tenant A (admin:tenant role)
        var createKeyReqA = await _client.PostAsJsonAsync("/admin/v1/api-keys", new CreateApiKeyDto("Key A", "admin:tenant", null));
        Assert.Equal(HttpStatusCode.Created, createKeyReqA.StatusCode);
        var keyDtoA = await createKeyReqA.Content.ReadFromJsonAsync<ApiKeyResponseDto>();
        Assert.NotNull(keyDtoA);

        // Reassign key to Tenant A in DB
        var keyEntityA = await db.ApiKeys.FindAsync(keyDtoA.Id);
        Assert.NotNull(keyEntityA);
        keyEntityA.TenantId = tenantA;
        keyEntityA.Role = "admin:tenant";
        await db.SaveChangesAsync();

        // Issue API key for Tenant B (admin:tenant role)
        var createKeyReqB = await _client.PostAsJsonAsync("/admin/v1/api-keys", new CreateApiKeyDto("Key B", "admin:tenant", null));
        Assert.Equal(HttpStatusCode.Created, createKeyReqB.StatusCode);
        var keyDtoB = await createKeyReqB.Content.ReadFromJsonAsync<ApiKeyResponseDto>();
        Assert.NotNull(keyDtoB);

        var keyEntityB = await db.ApiKeys.FindAsync(keyDtoB.Id);
        Assert.NotNull(keyEntityB);
        keyEntityB.TenantId = tenantB;
        keyEntityB.Role = "admin:tenant";
        await db.SaveChangesAsync();

        // Client for Tenant A
        var clientA = _factory.CreateClient();
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", keyDtoA.SecretKey);

        // Client for Tenant B
        var clientB = _factory.CreateClient();
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", keyDtoB.SecretKey);

        // Tenant A creates a product
        var createProdRes = await clientA.PostAsJsonAsync("/admin/v1/products", new CreateProductDto($"PRD_A_{Guid.NewGuid():N}"[..10], "Product A Exclusive", ["windows"]));
        Assert.Equal(HttpStatusCode.Created, createProdRes.StatusCode);
        var prodA = await createProdRes.Content.ReadFromJsonAsync<ProductDto>();
        Assert.NotNull(prodA);

        // Tenant B lists products -> MUST NOT see Product A!
        var listBRes = await clientB.GetAsync("/admin/v1/products");
        Assert.Equal(HttpStatusCode.OK, listBRes.StatusCode);
        var productsB = await listBRes.Content.ReadFromJsonAsync<List<ProductDto>>();
        Assert.NotNull(productsB);
        Assert.DoesNotContain(productsB, p => p.Id == prodA.Id);

        // Tenant B attempts privilege escalation: create an admin:super key
        var escReq = await clientB.PostAsJsonAsync("/admin/v1/api-keys", new CreateApiKeyDto("Escalated Key", "admin:super", null));
        Assert.Equal(HttpStatusCode.Forbidden, escReq.StatusCode);
    }

    [Fact]
    public async Task SEC06_Security_Headers_Present_On_Responses()
    {
        var response = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.True(response.Headers.Contains("X-Content-Type-Options"), "Response must contain X-Content-Type-Options");
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").First());

        Assert.True(response.Headers.Contains("X-Frame-Options"), "Response must contain X-Frame-Options");
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").First());

        Assert.True(response.Headers.Contains("Referrer-Policy"), "Response must contain Referrer-Policy");
        Assert.True(response.Headers.Contains("Content-Security-Policy"), "Response must contain Content-Security-Policy");
    }
}
