using System.Net;
using System.Net.Http.Json;
using Symbolon.ControlPlane.Models;
using Symbolon.Data.Entities;
using Symbolon.Protocol;
using Xunit;

namespace Symbolon.ControlPlane.Tests;

public sealed class AirGapTests : IClassFixture<ControlPlaneFactory>
{
    private readonly HttpClient _client;

    public AirGapTests(ControlPlaneFactory factory)
    {
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
}
