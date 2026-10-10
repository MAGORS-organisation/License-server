using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Achilles.ControlPlane.Endpoints;
using Achilles.Domain.Migration;
using Xunit;

namespace Achilles.ControlPlane.Tests;

public sealed class MigrationApiTests : IClassFixture<ControlPlaneFactory>
{
    private readonly HttpClient _client;

    public MigrationApiTests(ControlPlaneFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task MigrateFlexNetLicense_PreviewAndApply_CreatesEntitiesInDatabase()
    {
        string licenseContent = """
            SERVER srv01.test.corp 001122334455 27000
            VENDOR mysw_vd /opt/licenses/mysw_vd

            FEATURE CAD_SIMULATOR mysw_vd 2026.1 31-dec-2028 12 \
                SIGN="TESTSIGN1234" \
                NOTICE="Contract ACME-99"

            INCREMENT FEA_CORE mysw_vd 2026.0 permanent 4 \
                SIGN="TESTSIGN5678"
            """;

        // 1. Preview Mode (Apply = false)
        var previewReq = new FlexNetLicenseMigrateRequest(
            Content: licenseContent,
            TenantId: null,
            Apply: false);

        var previewRes = await _client.PostAsJsonAsync("/admin/v1/migrate/flexnet/license", previewReq);
        previewRes.StatusCode.Should().Be(HttpStatusCode.OK);

        using var previewDoc = JsonDocument.Parse(await previewRes.Content.ReadAsStringAsync());
        var previewRoot = previewDoc.RootElement;
        previewRoot.GetProperty("applied").GetBoolean().Should().BeFalse();
        previewRoot.GetProperty("plans").GetArrayLength().Should().Be(2);

        // 2. Apply Mode (Apply = true)
        var applyReq = new FlexNetLicenseMigrateRequest(
            Content: licenseContent,
            TenantId: null,
            Apply: true);

        var applyRes = await _client.PostAsJsonAsync("/admin/v1/migrate/flexnet/license", applyReq);
        applyRes.StatusCode.Should().Be(HttpStatusCode.OK);

        using var applyDoc = JsonDocument.Parse(await applyRes.Content.ReadAsStringAsync());
        var applyRoot = applyDoc.RootElement;
        applyRoot.GetProperty("applied").GetBoolean().Should().BeTrue();
        applyRoot.GetProperty("importedCount").GetInt32().Should().Be(2);

        var importedLicenses = applyRoot.GetProperty("importedLicenses");
        importedLicenses.GetArrayLength().Should().Be(2);

        string firstKey = importedLicenses[0].GetProperty("licenseKey").GetString()!;
        firstKey.Should().StartWith("SYM-");
    }

    [Fact]
    public async Task MigrateFlexNetOptions_ReturnsTranspiledPolicyModel()
    {
        string optContent = """
            GROUP r_d alice bob charlie
            HOST_GROUP render_farm node-1 node-2

            RESERVE 3 CAD_SIMULATOR GROUP r_d
            MAX 1 CAD_SIMULATOR GROUP r_d
            BORROW_LOWWATER CAD_SIMULATOR 2
            TIMEOUT CAD_SIMULATOR 1200
            TIMEOUTALL 3600
            """;

        var req = new FlexNetOptionsMigrateRequest(optContent);
        var res = await _client.PostAsJsonAsync("/admin/v1/migrate/flexnet/options", req);
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var report = await res.Content.ReadFromJsonAsync<OptionsTranspilationReport>();
        report.Should().NotBeNull();
        report!.TotalRulesParsed.Should().BeGreaterThanOrEqualTo(5);
        report.Policy.UserGroups.Should().ContainSingle(g => g.Name == "r_d");
        report.Policy.SeatAllocations.Should().HaveCount(2);
        report.Policy.Borrowing.MinAvailableSeatsForBorrow.Should().Be(2);
    }

    [Fact]
    public async Task MigrateFlexNetLog_CalculatesRightSizingAndDenials()
    {
        string logContent = """
            11:00:00 (mysw_vd) OUT: "CAD_ULTRA" user1@box1
            11:05:00 (mysw_vd) OUT: "CAD_ULTRA" user2@box2
            11:10:00 (mysw_vd) DENIED: "CAD_ULTRA" user3@box3 (Licensed number of users already reached. (-4,342))
            11:30:00 (mysw_vd) IN: "CAD_ULTRA" user1@box1
            11:45:00 (mysw_vd) IN: "CAD_ULTRA" user2@box2
            """;

        var req = new FlexNetLogAnalysisRequest(logContent);
        var res = await _client.PostAsJsonAsync("/admin/v1/migrate/flexnet/log-analysis", req);
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await res.Content.ReadFromJsonAsync<LogAnalysisResult>();
        result.Should().NotBeNull();
        result!.OverallPeakConcurrency.Should().Be(2);
        result.TotalDenialsAcrossAllFeatures.Should().Be(1);

        var stat = result.FeatureStatistics.First(s => s.FeatureCode == "CAD_ULTRA");
        stat.PeakConcurrency.Should().Be(2);
        stat.TotalDenials.Should().Be(1);
        stat.RecommendedSeats.Should().BeGreaterThanOrEqualTo(2);
        stat.RecommendedOverdraftBuffer.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task KeygenImport_ParsesJsonAndAppliesToDatabase()
    {
        string json = """
            {
              "data": [
                {
                  "id": "pol_fea_annual",
                  "type": "policies",
                  "attributes": {
                    "name": "FEA Annual Floating",
                    "code": "FEA-ANNUAL",
                    "duration": 31536000,
                    "maxMachines": 10,
                    "floating": true
                  }
                },
                {
                  "id": "lic_fea_01",
                  "type": "licenses",
                  "attributes": {
                    "key": "KEYGEN-FEA-9999",
                    "name": "Acme FEA License",
                    "status": "ACTIVE",
                    "maxMachines": 10
                  },
                  "relationships": {
                    "policy": { "data": { "id": "pol_fea_annual", "type": "policies" } }
                  }
                }
              ]
            }
            """;

        var req = new KeygenImportRequest(Content: json, Apply: true);
        var res = await _client.PostAsJsonAsync("/admin/v1/migrate/keygen", req);
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        root.GetProperty("applied").GetBoolean().Should().BeTrue();
        root.GetProperty("importedCount").GetInt32().Should().Be(1);
    }
}
