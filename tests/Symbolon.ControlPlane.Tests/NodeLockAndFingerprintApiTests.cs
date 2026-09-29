using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Symbolon.ControlPlane.Models;
using Symbolon.Protocol;
using Xunit;

namespace Symbolon.ControlPlane.Tests;

public sealed class NodeLockAndFingerprintApiTests : IClassFixture<ControlPlaneFactory>
{
    private readonly HttpClient _client;

    public NodeLockAndFingerprintApiTests(ControlPlaneFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task NodeLock_Activation_And_FuzzyReactivation_Succeeds_UnderMatchMost()
    {
        // 1. Create a product and a node-locked policy with MaxSeats=1, MachineMatching="match-most"
        string prodCode = $"node-prod-{Guid.NewGuid():N}";
        var prodRes = await _client.PostAsJsonAsync("/admin/v1/products", new CreateProductDto(prodCode, "Node CAD", ["windows"]));
        prodRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var prod = await prodRes.Content.ReadFromJsonAsync<ProductDto>();
        prod.Should().NotBeNull();

        var polRes = await _client.PostAsJsonAsync("/admin/v1/policies", new CreatePolicyDto
        {
            ProductId = prod!.Id,
            Code = $"pol-node-{Guid.NewGuid():N}",
            Name = "NodeLock Policy",
            MaxSeats = 1,
            LicenseModel = "nodelock",
            MachineMatching = "match-most",
            MachineUniqueness = "per-license"
        });
        polRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var policy = await polRes.Content.ReadFromJsonAsync<PolicyDto>();
        policy.Should().NotBeNull();

        // Issue license
        var licRes = await _client.PostAsJsonAsync("/admin/v1/licenses", new CreateLicenseDto
        {
            PolicyId = policy!.Id,
            MaxSeats = 1,
            CustomerRef = "Engineering Node 1"
        });
        licRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var license = await licRes.Content.ReadFromJsonAsync<LicenseResponseDto>();
        license.Should().NotBeNull();
        license!.LicenseKey.Should().NotBeNull();

        // 2. Initial Machine Activation (FPR-15)
        var initialComponents = new Dictionary<string, string>
        {
            ["machineId"] = "GUID-MACHINE-001",
            ["cpu"] = "Intel Core i7-12700K",
            ["board"] = "ASUS PRIME Z690",
            ["mac"] = "00:11:22:33:44:55"
        };

        var actRes = await _client.PostAsJsonAsync("/v1/activations", new ActivationRequestDto
        {
            LicenseKey = license.LicenseKey!,
            FingerprintComponents = initialComponents,
            MachineId = "CAD-WORKSTATION-01"
        });
        actRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var act1 = await actRes.Content.ReadFromJsonAsync<ActivationResponseDto>();
        act1.Should().NotBeNull();
        act1!.State.Should().Be("active");
        act1.ActivationId.Should().StartWith("mch_");

        // 3. Hardware upgrade simulation (upgraded motherboard and RAM, but machineId and cpu remain)
        // 2 out of 3 common components match -> majority under match-most (> 1.5)
        var upgradedComponents = new Dictionary<string, string>
        {
            ["machineId"] = "GUID-MACHINE-001", // Match
            ["cpu"] = "Intel Core i7-12700K",   // Match
            ["board"] = "GIGABYTE Z790 AORUS"    // Drift / upgrade
        };

        var reactRes = await _client.PostAsJsonAsync("/v1/activations", new ActivationRequestDto
        {
            LicenseKey = license.LicenseKey!,
            FingerprintComponents = upgradedComponents,
            MachineId = "CAD-WORKSTATION-01"
        });
        reactRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var act2 = await reactRes.Content.ReadFromJsonAsync<ActivationResponseDto>();
        act2.Should().NotBeNull();
        // Should reuse existing machine slot rather than failing with 409
        act2!.ActivationId.Should().Be(act1.ActivationId);

        // 4. Admin inspections: verify activations endpoint
        var adminListRes = await _client.GetAsync($"/admin/v1/licenses/{license.Id}/activations");
        adminListRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var adminList = await adminListRes.Content.ReadFromJsonAsync<List<MachineActivationAdminDto>>();
        adminList.Should().NotBeNull();
        adminList!.Should().HaveCount(1);
        adminList[0].Id.Should().Be(act1.ActivationId);

        // 5. Deactivation (FPR-15)
        var deactRes = await _client.DeleteAsync($"/v1/activations/{act1.ActivationId}");
        deactRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 6. Verify slot is freed after deactivation: can activate completely new machine
        var newMachineComponents = new Dictionary<string, string>
        {
            ["machineId"] = "GUID-MACHINE-002",
            ["cpu"] = "AMD Ryzen 9 7950X",
            ["board"] = "MSI MEG X670E"
        };
        var newActRes = await _client.PostAsJsonAsync("/v1/activations", new ActivationRequestDto
        {
            LicenseKey = license.LicenseKey!,
            FingerprintComponents = newMachineComponents,
            MachineId = "CAD-WORKSTATION-02"
        });
        newActRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var act3 = await newActRes.Content.ReadFromJsonAsync<ActivationResponseDto>();
        act3.Should().NotBeNull();
        act3!.ActivationId.Should().NotBe(act1.ActivationId);
    }

    [Fact]
    public void VerifyMatch_Endpoint_EvaluatesMatchingMatrixAccurately()
    {
        var stored = new Dictionary<string, string>
        {
            ["machineId"] = "ID-100",
            ["cpu"] = "CPU-100",
            ["board"] = "BOARD-100"
        };

        var incoming = new Dictionary<string, string>
        {
            ["machineId"] = "ID-100",
            ["cpu"] = "CPU-100",
            ["board"] = "BOARD-DIFF"
        };

        // POST /v1/activations/verify-match
        var requestDto = new VerifyFingerprintMatchRequestDto
        {
            StoredComponents = stored,
            IncomingComponents = incoming,
            Strategy = "match-most"
        };

        var result = FingerprintMatchingEngine.EvaluateMatch(
            requestDto.StoredComponents,
            requestDto.IncomingComponents,
            requestDto.Strategy);

        result.IsMatch.Should().BeTrue();
        result.CommonComponentsCount.Should().Be(3);
        result.MatchedComponentsCount.Should().Be(2);
        result.MatchedKeys.Should().Contain("machineId");
        result.MatchedKeys.Should().Contain("cpu");
        result.MismatchedKeys.Should().Contain("board");
    }

    [Fact]
    public async Task NodeLock_EnforcesMachineUniqueness_AcrossLicenses_FPR16()
    {
        // 1. Create product and policy with MachineUniqueness="unique"
        string prodCode = $"unique-prod-{Guid.NewGuid():N}";
        var prodRes = await _client.PostAsJsonAsync("/admin/v1/products", new CreateProductDto(prodCode, "Unique CAD", ["windows"]));
        var prod = await prodRes.Content.ReadFromJsonAsync<ProductDto>();
        prod.Should().NotBeNull();

        var polRes = await _client.PostAsJsonAsync("/admin/v1/policies", new CreatePolicyDto
        {
            ProductId = prod!.Id,
            Code = $"pol-uniq-{Guid.NewGuid():N}",
            Name = "Strict Unique Machine Policy",
            MaxSeats = 1,
            LicenseModel = "nodelock",
            MachineUniqueness = "unique"
        });
        polRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var policy = await polRes.Content.ReadFromJsonAsync<PolicyDto>();
        policy.Should().NotBeNull();

        // 2. Issue two distinct licenses under this policy
        var lic1Res = await _client.PostAsJsonAsync("/admin/v1/licenses", new CreateLicenseDto
        {
            PolicyId = policy!.Id,
            MaxSeats = 1,
            CustomerRef = "Client A"
        });
        var lic1 = await lic1Res.Content.ReadFromJsonAsync<LicenseResponseDto>();

        var lic2Res = await _client.PostAsJsonAsync("/admin/v1/licenses", new CreateLicenseDto
        {
            PolicyId = policy.Id,
            MaxSeats = 1,
            CustomerRef = "Client B"
        });
        var lic2 = await lic2Res.Content.ReadFromJsonAsync<LicenseResponseDto>();

        var sharedComponents = new Dictionary<string, string>
        {
            ["machineId"] = "GLOBAL-MACHINE-007",
            ["cpu"] = "Intel Core i9-13900K",
            ["board"] = "Dell Precision 5820"
        };

        // Activate on License 1 -> Succeeds
        var act1Res = await _client.PostAsJsonAsync("/v1/activations", new ActivationRequestDto
        {
            LicenseKey = lic1!.LicenseKey!,
            FingerprintComponents = sharedComponents,
            MachineId = "WORKSTATION-GLOBAL"
        });
        act1Res.StatusCode.Should().Be(HttpStatusCode.OK);

        // Try activating the SAME machine on License 2 -> Must be rejected due to machineUniqueness (FPR-16)
        var act2Res = await _client.PostAsJsonAsync("/v1/activations", new ActivationRequestDto
        {
            LicenseKey = lic2!.LicenseKey!,
            FingerprintComponents = sharedComponents,
            MachineId = "WORKSTATION-GLOBAL"
        });
        act2Res.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task NodeLock_FuzzyMismatch_Returns403_FingerprintMismatch_FPR17()
    {
        // 1. Create product and policy with MachineMatching="match-all"
        string prodCode = $"strict-prod-{Guid.NewGuid():N}";
        var prodRes = await _client.PostAsJsonAsync("/admin/v1/products", new CreateProductDto(prodCode, "Strict CAD", ["linux"]));
        var prod = await prodRes.Content.ReadFromJsonAsync<ProductDto>();

        var polRes = await _client.PostAsJsonAsync("/admin/v1/policies", new CreatePolicyDto
        {
            ProductId = prod!.Id,
            Code = $"pol-strict-{Guid.NewGuid():N}",
            Name = "Strict Policy",
            MaxSeats = 1,
            LicenseModel = "nodelock",
            MachineMatching = "match-all"
        });
        var policy = await polRes.Content.ReadFromJsonAsync<PolicyDto>();

        var licRes = await _client.PostAsJsonAsync("/admin/v1/licenses", new CreateLicenseDto
        {
            PolicyId = policy!.Id,
            MaxSeats = 1
        });
        var license = await licRes.Content.ReadFromJsonAsync<LicenseResponseDto>();

        // 2. Initial activation
        var originalComponents = new Dictionary<string, string>
        {
            ["machineId"] = "SERVER-ALPHA",
            ["cpu"] = "AMD EPYC 7763",
            ["board"] = "Supermicro H12SSL"
        };
        var actRes = await _client.PostAsJsonAsync("/v1/activations", new ActivationRequestDto
        {
            LicenseKey = license!.LicenseKey!,
            FingerprintComponents = originalComponents,
            MachineId = "NODE-ALPHA"
        });
        actRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var act = await actRes.Content.ReadFromJsonAsync<ActivationResponseDto>();

        // 3. Reactivation with altered hardware for the same MachineId under "match-all"
        var alteredComponents = new Dictionary<string, string>
        {
            ["machineId"] = "SERVER-ALPHA",
            ["cpu"] = "Intel Xeon Platinum", // Mismatch
            ["board"] = "Supermicro H12SSL"
        };
        var reactRes = await _client.PostAsJsonAsync("/v1/activations", new ActivationRequestDto
        {
            LicenseKey = license.LicenseKey!,
            FingerprintComponents = alteredComponents,
            MachineId = act!.ActivationId
        });

        // FPR-17: Nezhoda fingerprintu MUSÍ viesť k odpovedi 403 s typom problému fingerprint-mismatch
        reactRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var problemJson = await reactRes.Content.ReadAsStringAsync();
        problemJson.Should().Contain(ProblemTypes.FingerprintMismatch);
    }
}
