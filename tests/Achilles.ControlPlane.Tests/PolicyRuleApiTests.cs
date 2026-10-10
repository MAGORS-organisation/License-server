using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Achilles.ControlPlane.Models;
using Achilles.Protocol;
using Xunit;

namespace Achilles.ControlPlane.Tests;

public sealed class PolicyRuleApiTests : IClassFixture<ControlPlaneFactory>
{
    private readonly HttpClient _client;

    public PolicyRuleApiTests(ControlPlaneFactory factory)
    {
        _client = factory.CreateClient();
    }

    private async Task<(string LicenseId, string LicenseKey)> CreateTestLicenseAsync(int seats = 2)
    {
        var tenantRes = await _client.PostAsJsonAsync("/admin/v1/tenants", new CreateTenantDto($"tenant-{Guid.NewGuid():N}", "Tenant"));
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
            MaxSeats = seats,
            LicenseModel = "floating"
        });
        var policy = await polRes.Content.ReadFromJsonAsync<PolicyDto>();

        var licRes = await _client.PostAsJsonAsync("/admin/v1/licenses", new CreateLicenseDto
        {
            PolicyId = policy!.Id,
            MaxSeats = seats
        });
        var license = await licRes.Content.ReadFromJsonAsync<LicenseResponseDto>();

        return (license!.Id, license.LicenseKey!);
    }

    [Fact]
    public async Task GetRules_WhenNoneConfigured_Returns200WithDefaultRules()
    {
        var (licenseId, _) = await CreateTestLicenseAsync(seats: 2);

        var res = await _client.GetAsync($"/admin/v1/licenses/{licenseId}/rules");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await res.Content.ReadFromJsonAsync<PolicyRuleSetDto>();
        content.Should().NotBeNull();
        content!.Rules.Should().BeEmpty();
    }

    [Fact]
    public async Task PutRules_ValidYaml_UpdatesRulesAndSyncsReservations()
    {
        var (licenseId, _) = await CreateTestLicenseAsync(seats: 5);

        string yaml = @"version: 1
groups:
  - name: 'CAD_TEAM'
    members: ['alice', 'bob']
rules:
  - type: 'reserve'
    group: 'CAD_TEAM'
    value: 2
  - type: 'deny'
    user: 'blocked_user'
";

        var updateReq = new UpdatePolicyRulesRequestDto { RulesYaml = yaml };
        var putRes = await _client.PutAsJsonAsync($"/admin/v1/licenses/{licenseId}/rules", updateReq);
        putRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var getRes = await _client.GetAsync($"/admin/v1/licenses/{licenseId}/rules");
        getRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var setDto = await getRes.Content.ReadFromJsonAsync<PolicyRuleSetDto>();
        setDto.Should().NotBeNull();
        setDto!.Rules.Should().HaveCount(2);
        setDto.Rules.Should().Contain(r => r.Type == "deny" && r.User == "blocked_user");
        setDto.Rules.Should().Contain(r => r.Type == "reserve" && r.Group == "CAD_TEAM" && r.Value == 2);
    }

    [Fact]
    public async Task PostSimulate_EvaluatesContextWithoutSideEffects()
    {
        var (licenseId, _) = await CreateTestLicenseAsync(seats: 5);

        string yaml = @"version: 1
groups:
  - name: 'ENGINEERING'
    members: ['eng_*']
rules:
  - type: 'deny'
    user: 'banned_user'
    reason: 'Security blacklist'
  - type: 'reserve'
    group: 'ENGINEERING'
    value: 2
  - type: 'priority'
    group: 'ENGINEERING'
    value: 85
";
        await _client.PutAsJsonAsync($"/admin/v1/licenses/{licenseId}/rules", new UpdatePolicyRulesRequestDto { RulesYaml = yaml });

        // Simulate banned user
        var simBannedReq = new SimulateRuleEvaluationRequestDto { UserId = "banned_user" };
        var simBannedRes = await _client.PostAsJsonAsync($"/admin/v1/licenses/{licenseId}/rules/simulate", simBannedReq);
        simBannedRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var bannedResult = await simBannedRes.Content.ReadFromJsonAsync<SimulateRuleEvaluationResponseDto>();
        bannedResult!.Allowed.Should().BeFalse();
        bannedResult.DenyReason.Should().NotBeNullOrEmpty();

        // Simulate engineering user
        var simEngReq = new SimulateRuleEvaluationRequestDto { UserId = "eng_peter" };
        var simEngRes = await _client.PostAsJsonAsync($"/admin/v1/licenses/{licenseId}/rules/simulate", simEngReq);
        simEngRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var engResult = await simEngRes.Content.ReadFromJsonAsync<SimulateRuleEvaluationResponseDto>();
        engResult!.Allowed.Should().BeTrue();
        engResult.MatchedReservation.Should().Be("ENGINEERING");
        engResult.ResolvedPriority.Should().Be(85);
    }

    [Fact]
    public async Task Checkout_DeniedByRule_Returns403ProblemDetails_ConformsToFLT24()
    {
        var (licenseId, key) = await CreateTestLicenseAsync(seats: 2);

        string yaml = @"version: 1
rules:
  - type: 'deny'
    user: 'prohibited_user'
    reason: 'Security policy violation'
";
        await _client.PutAsJsonAsync($"/admin/v1/licenses/{licenseId}/rules", new UpdatePolicyRulesRequestDto { RulesYaml = yaml });

        var req = new CheckoutRequestDto
        {
            LicenseKey = key,
            UserId = "prohibited_user",
            FingerprintComponents = new Dictionary<string, string> { ["machineId"] = "m1" }
        };

        var res = await _client.PostAsJsonAsync("/v1/leases", req);
        res.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        string body = await res.Content.ReadAsStringAsync();
        body.Should().Contain(ProblemTypes.RuleDenied);
        body.Should().Contain("Security policy violation");
    }

    [Fact]
    public async Task Checkout_ExceedsGroupMaxQuota_Returns409ProblemDetails()
    {
        var (licenseId, key) = await CreateTestLicenseAsync(seats: 5);

        string yaml = @"version: 1
groups:
  - name: 'JUNIORS'
    members: ['junior_*']
rules:
  - type: 'max'
    group: 'JUNIORS'
    value: 1
";
        await _client.PutAsJsonAsync($"/admin/v1/licenses/{licenseId}/rules", new UpdatePolicyRulesRequestDto { RulesYaml = yaml });

        // 1st junior checkout succeeds
        var req1 = new CheckoutRequestDto
        {
            LicenseKey = key,
            UserId = "junior_anna",
            FingerprintComponents = new Dictionary<string, string> { ["machineId"] = "m-j1" }
        };
        var res1 = await _client.PostAsJsonAsync("/v1/leases", req1);
        res1.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2nd junior checkout exceeds quota -> 409 Conflict
        var req2 = new CheckoutRequestDto
        {
            LicenseKey = key,
            UserId = "junior_boris",
            FingerprintComponents = new Dictionary<string, string> { ["machineId"] = "m-j2" }
        };
        var res2 = await _client.PostAsJsonAsync("/v1/leases", req2);
        res2.StatusCode.Should().Be(HttpStatusCode.Conflict);

        string body = await res2.Content.ReadAsStringAsync();
        body.Should().Contain(ProblemTypes.GroupQuotaExceeded);
        body.Should().Contain("Maximum limit");
    }

    [Fact]
    public async Task Checkout_ReservedSeats_ProtectsReservedCapacity_FLT23()
    {
        // 2 seats total: 1 reserved for VIP_GROUP, 1 unreserved
        var (licenseId, key) = await CreateTestLicenseAsync(seats: 2);

        string yaml = @"version: 1
groups:
  - name: 'VIP_GROUP'
    members: ['vip_*']
rules:
  - type: 'reserve'
    group: 'VIP_GROUP'
    value: 1
";
        await _client.PutAsJsonAsync($"/admin/v1/licenses/{licenseId}/rules", new UpdatePolicyRulesRequestDto { RulesYaml = yaml });

        // Regular user 1 checkouts -> takes the 1 free unreserved seat
        var req1 = new CheckoutRequestDto
        {
            LicenseKey = key,
            UserId = "regular_user1",
            FingerprintComponents = new Dictionary<string, string> { ["machineId"] = "m-reg1" }
        };
        var res1 = await _client.PostAsJsonAsync("/v1/leases", req1);
        res1.StatusCode.Should().Be(HttpStatusCode.OK);

        // Regular user 2 checkouts -> only the reserved seat remains, non-VIP is denied
        var req2 = new CheckoutRequestDto
        {
            LicenseKey = key,
            UserId = "regular_user2",
            FingerprintComponents = new Dictionary<string, string> { ["machineId"] = "m-reg2" }
        };
        var res2 = await _client.PostAsJsonAsync("/v1/leases", req2);
        res2.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // VIP user checkouts -> qualifies for reservation, acquires the reserved seat
        var reqVip = new CheckoutRequestDto
        {
            LicenseKey = key,
            UserId = "vip_john",
            FingerprintComponents = new Dictionary<string, string> { ["machineId"] = "m-vip" }
        };
        var resVip = await _client.PostAsJsonAsync("/v1/leases", reqVip);
        resVip.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RulesUpdate_PreservesActiveLeases_FLT25()
    {
        var (licenseId, key) = await CreateTestLicenseAsync(seats: 2);

        // 1. Initial checkout
        var chkReq = new CheckoutRequestDto
        {
            LicenseKey = key,
            UserId = "client1",
            FingerprintComponents = new Dictionary<string, string> { ["machineId"] = "m-client" }
        };
        var chkRes = await _client.PostAsJsonAsync("/v1/leases", chkReq);
        chkRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var lease = await chkRes.Content.ReadFromJsonAsync<CheckoutResponseDto>();

        // 2. Admin updates rules (FLT-25: does not revoke active leases)
        string yaml = @"version: 1
rules:
  - type: 'deny'
    user: 'new_blocked_user'
";
        var putRes = await _client.PutAsJsonAsync($"/admin/v1/licenses/{licenseId}/rules", new UpdatePolicyRulesRequestDto { RulesYaml = yaml });
        putRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Client renews existing active lease -> succeeds
        var renewReq = new RenewRequestDto
        {
            ClientSeq = 0,
            FingerprintComponents = new Dictionary<string, string> { ["machineId"] = "m-client" }
        };
        var renewRes = await _client.PostAsJsonAsync($"/v1/leases/{lease!.LeaseId}/renew", renewReq);
        renewRes.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
