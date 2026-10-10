using System.Net;
using System.Net.Http.Json;
using Achilles.ControlPlane.Models;
using Achilles.Protocol;
using Xunit;

namespace Achilles.ControlPlane.Tests;

public sealed class SecurityAndTransparencyTests : IClassFixture<ControlPlaneFactory>
{
    private readonly HttpClient _client;

    public SecurityAndTransparencyTests(ControlPlaneFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ApiKey_Lifecycle_Authentication_And_Revocation_Works()
    {
        // 1. Create a new super-admin API key
        var createReq = new HttpRequestMessage(HttpMethod.Post, "/admin/v1/api-keys")
        {
            Content = JsonContent.Create(new CreateApiKeyDto("Build Agent", "admin:super", DateTimeOffset.UtcNow.AddDays(30)))
        };
        var createRes = await _client.SendAsync(createReq);
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);

        var keyDto = await createRes.Content.ReadFromJsonAsync<ApiKeyResponseDto>();
        Assert.NotNull(keyDto);
        Assert.NotNull(keyDto.SecretKey);
        Assert.StartsWith("sym_adm_", keyDto.SecretKey, StringComparison.Ordinal);

        // 2. Call admin endpoint with valid API key in X-Api-Key header
        var authReq = new HttpRequestMessage(HttpMethod.Get, "/admin/v1/api-keys");
        authReq.Headers.Add("X-Api-Key", keyDto.SecretKey);
        var authRes = await _client.SendAsync(authReq);
        Assert.Equal(HttpStatusCode.OK, authRes.StatusCode);
        var keysList = await authRes.Content.ReadFromJsonAsync<List<ApiKeyResponseDto>>();
        Assert.NotNull(keysList);
        Assert.Contains(keysList, k => k.Id == keyDto.Id);

        // 3. Call admin endpoint with valid API key as Bearer token
        var bearerReq = new HttpRequestMessage(HttpMethod.Get, "/admin/v1/api-keys");
        bearerReq.Headers.Add("Authorization", $"Bearer {keyDto.SecretKey}");
        var bearerRes = await _client.SendAsync(bearerReq);
        Assert.Equal(HttpStatusCode.OK, bearerRes.StatusCode);

        // 4. Call admin endpoint with invalid API key -> 401 Unauthorized
        var badReq = new HttpRequestMessage(HttpMethod.Get, "/admin/v1/api-keys");
        badReq.Headers.Add("X-Api-Key", "sym_adm_00000000_11112222333344445555666677778888");
        var badRes = await _client.SendAsync(badReq);
        Assert.Equal(HttpStatusCode.Unauthorized, badRes.StatusCode);

        // 5. Revoke the API key
        var delReq = new HttpRequestMessage(HttpMethod.Delete, $"/admin/v1/api-keys/{keyDto.Id}");
        delReq.Headers.Add("X-Api-Key", keyDto.SecretKey);
        var delRes = await _client.SendAsync(delReq);
        Assert.Equal(HttpStatusCode.OK, delRes.StatusCode);

        // 6. Calling with the revoked key -> 401 Unauthorized
        var revokedReq = new HttpRequestMessage(HttpMethod.Get, "/admin/v1/api-keys");
        revokedReq.Headers.Add("X-Api-Key", keyDto.SecretKey);
        var revokedRes = await _client.SendAsync(revokedReq);
        Assert.Equal(HttpStatusCode.Unauthorized, revokedRes.StatusCode);
    }

    [Fact]
    public async Task Rbac_Auditor_Role_Enforcement_Works()
    {
        // 1. Super-admin generates an auditor API key
        var createAuditorRes = await _client.PostAsJsonAsync("/admin/v1/api-keys", new CreateApiKeyDto("Security Auditor", "auditor"));
        Assert.Equal(HttpStatusCode.Created, createAuditorRes.StatusCode);
        var auditorKeyDto = await createAuditorRes.Content.ReadFromJsonAsync<ApiKeyResponseDto>();
        Assert.NotNull(auditorKeyDto);
        Assert.NotNull(auditorKeyDto.SecretKey);

        // 2. Auditor can read licenses (GET)
        var readReq = new HttpRequestMessage(HttpMethod.Get, "/admin/v1/licenses");
        readReq.Headers.Add("X-Api-Key", auditorKeyDto.SecretKey);
        var readRes = await _client.SendAsync(readReq);
        Assert.Equal(HttpStatusCode.OK, readRes.StatusCode);

        // 3. Auditor CANNOT write or modify resources (POST -> 403 Forbidden)
        var writeReq = new HttpRequestMessage(HttpMethod.Post, "/admin/v1/policies")
        {
            Content = JsonContent.Create(new CreatePolicyDto
            {
                ProductId = "dummy",
                Code = "test-policy",
                Name = "Test Policy"
            })
        };
        writeReq.Headers.Add("X-Api-Key", auditorKeyDto.SecretKey);
        var writeRes = await _client.SendAsync(writeReq);
        Assert.Equal(HttpStatusCode.Forbidden, writeRes.StatusCode);

        // 4. Auditor CANNOT manage API keys (protected by SuperAdminOnly policy -> 403 Forbidden)
        var keyMgmtReq = new HttpRequestMessage(HttpMethod.Get, "/admin/v1/api-keys");
        keyMgmtReq.Headers.Add("X-Api-Key", auditorKeyDto.SecretKey);
        var keyMgmtRes = await _client.SendAsync(keyMgmtReq);
        Assert.Equal(HttpStatusCode.Forbidden, keyMgmtRes.StatusCode);
    }

    [Fact]
    public async Task MerkleTree_Transparency_Root_And_Inclusion_Proof_Works()
    {
        // 1. Create a tenant, product, policy, and issue a license to populate audit events
        var tenantRes = await _client.PostAsJsonAsync("/admin/v1/tenants", new CreateTenantDto($"org-{Guid.NewGuid():N}", "Org"));
        var tenant = await tenantRes.Content.ReadFromJsonAsync<TenantDto>();

        var prodReq = new HttpRequestMessage(HttpMethod.Post, "/admin/v1/products")
        {
            Content = JsonContent.Create(new CreateProductDto($"prod-{Guid.NewGuid():N}", "Product", ["windows"]))
        };
        prodReq.Headers.Add("X-Tenant-Id", tenant!.Id);
        var prodRes = await _client.SendAsync(prodReq);
        var product = await prodRes.Content.ReadFromJsonAsync<ProductDto>();

        var polRes = await _client.PostAsJsonAsync("/admin/v1/policies", new CreatePolicyDto
        {
            ProductId = product!.Id,
            Code = $"policy-{Guid.NewGuid():N}",
            Name = "Policy",
            MaxSeats = 1,
            LicenseModel = "floating"
        });
        var policy = await polRes.Content.ReadFromJsonAsync<PolicyDto>();

        var licRes = await _client.PostAsJsonAsync("/admin/v1/licenses", new CreateLicenseDto
        {
            PolicyId = policy!.Id,
            MaxSeats = 1
        });
        Assert.Equal(HttpStatusCode.Created, licRes.StatusCode);

        // 2. Query Transparency Root
        var rootRes = await _client.GetAsync("/v1/transparency/root");
        Assert.Equal(HttpStatusCode.OK, rootRes.StatusCode);
        var rootDto = await rootRes.Content.ReadFromJsonAsync<TransparencyRootResponseDto>();
        Assert.NotNull(rootDto);
        Assert.NotEmpty(rootDto.RootHash);
        Assert.True(rootDto.TreeSize > 0);

        // 3. Query latest audit events
        var auditRes = await _client.GetAsync("/admin/v1/audit");
        Assert.Equal(HttpStatusCode.OK, auditRes.StatusCode);
        var events = await auditRes.Content.ReadFromJsonAsync<List<Achilles.Data.Entities.AuditEventEntity>>();
        Assert.NotNull(events);
        Assert.NotEmpty(events);

        string targetAuditId = events[0].Id;

        // 4. Request Inclusion Proof for the audit event
        var proofRes = await _client.GetAsync($"/v1/transparency/inclusion/{targetAuditId}");
        Assert.Equal(HttpStatusCode.OK, proofRes.StatusCode);
        var proofDto = await proofRes.Content.ReadFromJsonAsync<TransparencyInclusionResponseDto>();
        Assert.NotNull(proofDto);
        Assert.Equal(targetAuditId, proofDto.AuditId);
        Assert.NotEmpty(proofDto.LeafHash);
        Assert.NotEmpty(proofDto.RootHash);

        // 5. Verify the cryptographic proof using verification endpoint
        var verifyRes = await _client.PostAsJsonAsync("/v1/transparency/verify", new VerifyTransparencyProofRequestDto
        {
            LeafHash = proofDto.LeafHash,
            RootHash = proofDto.RootHash,
            Path = proofDto.Path
        });
        Assert.Equal(HttpStatusCode.OK, verifyRes.StatusCode);
        var verifyDto = await verifyRes.Content.ReadFromJsonAsync<VerifyTransparencyProofResponseDto>();
        Assert.NotNull(verifyDto);
        Assert.True(verifyDto.IsValid);

        // 6. Tampered leaf verification fails
        var fakeLeafHash = Convert.ToHexStringLower(new byte[32]);
        var tamperedRes = await _client.PostAsJsonAsync("/v1/transparency/verify", new VerifyTransparencyProofRequestDto
        {
            LeafHash = fakeLeafHash,
            RootHash = proofDto.RootHash,
            Path = proofDto.Path
        });
        Assert.Equal(HttpStatusCode.OK, tamperedRes.StatusCode);
        var tamperedDto = await tamperedRes.Content.ReadFromJsonAsync<VerifyTransparencyProofResponseDto>();
        Assert.NotNull(tamperedDto);
        Assert.False(tamperedDto.IsValid);
    }
}
