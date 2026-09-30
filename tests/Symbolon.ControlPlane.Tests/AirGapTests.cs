using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Symbolon.ControlPlane.Models;
using Symbolon.Crypto;
using Symbolon.Data.Entities;
using Symbolon.Format;
using Symbolon.Protocol;
using Xunit;

namespace Symbolon.ControlPlane.Tests;

public sealed class AirGapTests : IClassFixture<ControlPlaneFactory>
{
    private readonly ControlPlaneFactory _factory;
    private readonly HttpClient _client;

    public AirGapTests(ControlPlaneFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task AirGap_Grant_Request_And_Issuance_With_UsageDigest_Succeeds()
    {
        // 1. Create a product, policy and license with 10 floating seats
        string prodCode = $"airgap-prod-{Guid.NewGuid():N}";
        var prodRes = await _client.PostAsJsonAsync("/admin/v1/products", new CreateProductDto(prodCode, "AirGap CAD", ["linux"]));
        Assert.Equal(HttpStatusCode.Created, prodRes.StatusCode);
        var prod = await prodRes.Content.ReadFromJsonAsync<ProductDto>();
        Assert.NotNull(prod);

        var polRes = await _client.PostAsJsonAsync("/admin/v1/policies", new CreatePolicyDto
        {
            ProductId = prod.Id,
            Code = $"pol-{Guid.NewGuid():N}",
            Name = "AirGap Floating Policy",
            MaxSeats = 10,
            LicenseModel = "floating"
        });
        Assert.Equal(HttpStatusCode.Created, polRes.StatusCode);
        var policy = await polRes.Content.ReadFromJsonAsync<PolicyDto>();
        Assert.NotNull(policy);

        var licRes = await _client.PostAsJsonAsync("/admin/v1/licenses", new CreateLicenseDto
        {
            PolicyId = policy.Id,
            MaxSeats = 10,
            CustomerRef = "Air-Gap Plant 1"
        });
        Assert.Equal(HttpStatusCode.Created, licRes.StatusCode);
        var license = await licRes.Content.ReadFromJsonAsync<LicenseResponseDto>();
        Assert.NotNull(license);
        Assert.NotNull(license.LicenseKey);

        // 2. Air-gapped Relay generates a .symreq and submits via offline portal
        var reqDto = new OfflineGrantRequestDto
        {
            RelayId = "rly_airgap_isolated_01",
            LicenseKey = license.LicenseKey,
            RequestedSeats = 4,
            LastSeq = 0,
            UsageDigest = "sha256:d41d8cd98f00b204e9800998ecf8427e",
            Nonce = "nonce_123"
        };

        var grantRes = await _client.PostAsJsonAsync("/v1/offline/grants", reqDto);
        Assert.Equal(HttpStatusCode.OK, grantRes.StatusCode);

        var grant = await grantRes.Content.ReadFromJsonAsync<OfflineGrantResponseDto>();
        Assert.NotNull(grant);
        Assert.Equal("rly_airgap_isolated_01", grant.RelayId);
        Assert.Equal(4, grant.SeatTo - grant.SeatFrom + 1);
        Assert.StartsWith("-----BEGIN SYMBOLON SEAT GRANT-----", grant.SymgrantPem, StringComparison.Ordinal);
        Assert.Contains("-----END SYMBOLON SEAT GRANT-----", grant.SymgrantPem, StringComparison.Ordinal);

        // 3. Verify that the usageDigest was recorded in the audit ledger
        var auditRes = await _client.GetAsync("/admin/v1/audit");
        Assert.Equal(HttpStatusCode.OK, auditRes.StatusCode);
        var auditEvents = await auditRes.Content.ReadFromJsonAsync<List<AuditEventEntity>>();
        Assert.NotNull(auditEvents);
        Assert.Contains(auditEvents, e => e.Type == "airgap_usage_digest" && e.PayloadJson.Contains("sha256:d41d8cd98f00b204e9800998ecf8427e", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AirGap_NodeLock_Activation_Generates_Valid_Symlic_Pem()
    {
        // 1. Create product, policy and nodelock license
        string prodCode = $"airgap-node-{Guid.NewGuid():N}";
        var prodRes = await _client.PostAsJsonAsync("/admin/v1/products", new CreateProductDto(prodCode, "AirGap Node", ["windows"]));
        var prod = await prodRes.Content.ReadFromJsonAsync<ProductDto>();
        Assert.NotNull(prod);

        var polRes = await _client.PostAsJsonAsync("/admin/v1/policies", new CreatePolicyDto
        {
            ProductId = prod.Id,
            Code = $"pol-{Guid.NewGuid():N}",
            Name = "AirGap Nodelock Policy",
            MaxSeats = 1,
            LicenseModel = "nodelock"
        });
        var policy = await polRes.Content.ReadFromJsonAsync<PolicyDto>();
        Assert.NotNull(policy);

        var licRes = await _client.PostAsJsonAsync("/admin/v1/licenses", new CreateLicenseDto
        {
            PolicyId = policy.Id,
            MaxSeats = 1,
            CustomerRef = "Air-Gap CNC Machine"
        });
        var license = await licRes.Content.ReadFromJsonAsync<LicenseResponseDto>();
        Assert.NotNull(license);
        Assert.NotNull(license.LicenseKey);

        // 2. Perform offline activation
        var actReq = new OfflineActivationRequestDto
        {
            LicenseKey = license.LicenseKey,
            Fingerprint = "sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            MachineName = "CNC-Milling-Unit-02"
        };

        var actRes = await _client.PostAsJsonAsync("/v1/offline/activations", actReq);
        Assert.Equal(HttpStatusCode.OK, actRes.StatusCode);

        var act = await actRes.Content.ReadFromJsonAsync<OfflineActivationResponseDto>();
        Assert.NotNull(act);
        Assert.Equal(actReq.Fingerprint, act.Fingerprint);
        Assert.StartsWith("-----BEGIN SYMBOLON LICENSE KEY-----", act.SymlicPem, StringComparison.Ordinal);
        Assert.Contains("-----END SYMBOLON LICENSE KEY-----", act.SymlicPem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AirGap_Signed_Symreq_Artifact_Request_And_Issuance_FLT32_to_FLT35_Succeeds()
    {
        // 1. Create product, policy and license with 10 floating seats
        string prodCode = $"airgap-symreq-{Guid.NewGuid():N}";
        var prodRes = await _client.PostAsJsonAsync("/admin/v1/products", new CreateProductDto(prodCode, "AirGap Robot Suite", ["linux"]));
        var prod = await prodRes.Content.ReadFromJsonAsync<ProductDto>();
        Assert.NotNull(prod);

        var polRes = await _client.PostAsJsonAsync("/admin/v1/policies", new CreatePolicyDto
        {
            ProductId = prod.Id,
            Code = $"pol-{Guid.NewGuid():N}",
            Name = "AirGap Floating Policy",
            MaxSeats = 10,
            LicenseModel = "floating"
        });
        var policy = await polRes.Content.ReadFromJsonAsync<PolicyDto>();
        Assert.NotNull(policy);

        var licRes = await _client.PostAsJsonAsync("/admin/v1/licenses", new CreateLicenseDto
        {
            PolicyId = policy.Id,
            MaxSeats = 10,
            CustomerRef = "Secure Lab 1"
        });
        var license = await licRes.Content.ReadFromJsonAsync<LicenseResponseDto>();
        Assert.NotNull(license);
        Assert.NotNull(license.LicenseKey);

        // 2. Generate Relay identity key and trust it in CP keyring
        string relayId = $"rly_secure_lab_{Guid.NewGuid():N}";
        using var relayKey = Es256SignatureProvider.GenerateKey(relayId);
        var cpKeyRing = _factory.Services.GetRequiredService<IKeyRing>();
        if (cpKeyRing is SymbolonKeyRing sk)
        {
            sk.Add(relayKey);
        }

        // 3. Construct and sign .symreq artifact (FLT-32)
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string nonce1 = $"nonce_{Guid.NewGuid():N}";
        var reqClaims1 = new AirGapRequestClaims
        {
            Iss = relayId,
            Sub = license.LicenseKey,
            Jti = $"req_{Guid.NewGuid():N}",
            Iat = now,
            Exp = now + 3600,
            Symreq = new AirGapRequestPayload
            {
                V = 1,
                RelayId = relayId,
                LicenseKey = license.LicenseKey,
                RequestedSeats = 4,
                LastSeq = 0,
                UsageDigest = "sha256:d41d8cd98f00b204e9800998ecf8427e",
                Nonce = nonce1
            }
        };

        var reqSigner = new AirGapRequestSigner(relayKey);
        string symreqPem1 = reqSigner.Sign(reqClaims1);

        // 4. Submit .symreq via POST /v1/offline/requests
        var submitRes1 = await _client.PostAsJsonAsync("/v1/offline/requests", new OfflineAirGapRequestDto { RequestPem = symreqPem1 });
        Assert.Equal(HttpStatusCode.OK, submitRes1.StatusCode);

        var resp1 = await submitRes1.Content.ReadFromJsonAsync<OfflineAirGapResponseDto>();
        Assert.NotNull(resp1);
        Assert.Equal(relayId, resp1.RelayId);
        Assert.Equal(4, resp1.Seats);
        Assert.Equal(1, resp1.Seq);
        Assert.Null(resp1.Supersedes);
        Assert.StartsWith("-----BEGIN SYMBOLON SEAT GRANT-----", resp1.SymgrantPem, StringComparison.Ordinal);

        // 5. Anti-replay test (FLT-34): re-submitting exact same symreq must return 409 Conflict
        var replayRes = await _client.PostAsJsonAsync("/v1/offline/requests", new OfflineAirGapRequestDto { RequestPem = symreqPem1 });
        Assert.Equal(HttpStatusCode.Conflict, replayRes.StatusCode);

        // 6. Request next sequence with lastSeq = 1 (GNT-8, GNT-9, FLT-33)
        string nonce2 = $"nonce_{Guid.NewGuid():N}";
        var reqClaims2 = new AirGapRequestClaims
        {
            Iss = relayId,
            Sub = license.LicenseKey,
            Jti = $"req_{Guid.NewGuid():N}",
            Iat = now,
            Exp = now + 3600,
            Symreq = new AirGapRequestPayload
            {
                V = 1,
                RelayId = relayId,
                LicenseKey = license.LicenseKey,
                RequestedSeats = 4,
                LastSeq = 1,
                UsageDigest = "sha256:4a6f2389...",
                Nonce = nonce2
            }
        };
        string symreqPem2 = reqSigner.Sign(reqClaims2);

        var submitRes2 = await _client.PostAsJsonAsync("/v1/offline/requests", new OfflineAirGapRequestDto { RequestPem = symreqPem2 });
        Assert.Equal(HttpStatusCode.OK, submitRes2.StatusCode);

        var resp2 = await submitRes2.Content.ReadFromJsonAsync<OfflineAirGapResponseDto>();
        Assert.NotNull(resp2);
        Assert.Equal(2, resp2.Seq);
        Assert.Equal(1, resp2.Supersedes); // GNT-9: supersedes seq 1!
    }

    [Fact]
    public async Task AirGap_Admin_Grants_Endpoints_List_And_Revoke()
    {
        // 1. Create product, policy, license
        string prodCode = $"admin-grants-prod-{Guid.NewGuid():N}";
        var prodRes = await _client.PostAsJsonAsync("/admin/v1/products", new CreateProductDto(prodCode, "Grant Admin Test", ["linux"]));
        var prod = await prodRes.Content.ReadFromJsonAsync<ProductDto>();
        Assert.NotNull(prod);

        var polRes = await _client.PostAsJsonAsync("/admin/v1/policies", new CreatePolicyDto
        {
            ProductId = prod.Id,
            Code = $"pol-{Guid.NewGuid():N}",
            Name = "Floating",
            MaxSeats = 20,
            LicenseModel = "floating"
        });
        var policy = await polRes.Content.ReadFromJsonAsync<PolicyDto>();
        Assert.NotNull(policy);

        var licRes = await _client.PostAsJsonAsync("/admin/v1/licenses", new CreateLicenseDto
        {
            PolicyId = policy.Id,
            MaxSeats = 20
        });
        var license = await licRes.Content.ReadFromJsonAsync<LicenseResponseDto>();
        Assert.NotNull(license);

        // 2. Issue grant
        var grantRes = await _client.PostAsJsonAsync("/v1/offline/grants", new OfflineGrantRequestDto
        {
            RelayId = "rly_admin_test_01",
            LicenseKey = license.LicenseKey!,
            RequestedSeats = 5,
            LastSeq = 0,
            UsageDigest = "sha256:abc",
            Nonce = $"nonce_{Guid.NewGuid():N}"
        });
        Assert.Equal(HttpStatusCode.OK, grantRes.StatusCode);
        var grant = await grantRes.Content.ReadFromJsonAsync<OfflineGrantResponseDto>();
        Assert.NotNull(grant);

        // 3. Query GET /admin/v1/licenses/{id}/grants
        var listRes = await _client.GetAsync($"/admin/v1/licenses/{license.Id}/grants");
        Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);
        var grantsList = await listRes.Content.ReadFromJsonAsync<List<Symbolon.Domain.Grants.SeatGrantRecord>>();
        Assert.NotNull(grantsList);
        Assert.Contains(grantsList, g => g.Id == grant.GrantId);

        // 4. Query GET /admin/v1/grants
        var allRes = await _client.GetAsync("/admin/v1/grants");
        Assert.Equal(HttpStatusCode.OK, allRes.StatusCode);

        // 5. Revoke grant DELETE /admin/v1/grants/{id}
        var delRes = await _client.DeleteAsync($"/admin/v1/grants/{grant.GrantId}");
        Assert.Equal(HttpStatusCode.NoContent, delRes.StatusCode);
    }
}
