using FluentAssertions;
using Achilles.Domain.Migration;
using Xunit;

namespace Achilles.Domain.Tests;

public sealed class MigrationTests
{
    [Fact]
    public void FlexNetLicenseParser_ParsesComplexLicenseFileWithContinuationLines()
    {
        string licenseContent = """
            # FlexNet Publisher Sample License File
            SERVER srv01.company.internal 001122334455 27000
            SERVER srv02.company.internal 001122334456 27000
            SERVER srv03.company.internal 001122334457 27000
            VENDOR mysw_vd /opt/licenses/mysw_vd

            FEATURE CAD_PRO mysw_vd 2026.1 31-dec-2027 10 \
                SIGN="98A7B6C5D4E3" \
                HOSTID=001122334455 \
                NOTICE="Licensed to ACME Corp" \
                SN=SN-998877

            INCREMENT FEA_SOLVER mysw_vd 2026.0 permanent 5 \
                SIGN="112233445566"

            PACKAGE SUITE_ENTERPRISE mysw_vd 2026.1 \
                COMPONENTS="CAD_PRO:2026.1 FEA_SOLVER:2026.0 SIM_RENDER:1.0"

            FEATURE VIEWER_FREE mysw_vd 1.0 permanent uncounted \
                SIGN="AABBCCDDEEFF"
            """;

        var parsed = FlexNetLicenseParser.Parse(licenseContent);

        parsed.Should().NotBeNull();
        parsed.Servers.Should().HaveCount(3);
        parsed.Servers[0].Hostname.Should().Be("srv01.company.internal");
        parsed.Servers[0].Port.Should().Be(27000);

        parsed.Vendors.Should().Contain("mysw_vd");

        parsed.Features.Should().HaveCount(3);

        var cad = parsed.Features.First(f => f.Name == "CAD_PRO");
        cad.Seats.Should().Be(10);
        cad.IsPermanent.Should().BeFalse();
        cad.ExpirationDate.Should().NotBeNull();
        cad.ExpirationDate!.Value.Year.Should().Be(2027);
        cad.HostId.Should().Be("001122334455");
        cad.SerialNumber.Should().Be("SN-998877");

        var fea = parsed.Features.First(f => f.Name == "FEA_SOLVER");
        fea.Seats.Should().Be(5);
        fea.IsPermanent.Should().BeTrue();

        var viewer = parsed.Features.First(f => f.Name == "VIEWER_FREE");
        viewer.IsUncounted.Should().BeTrue();
        viewer.IsPermanent.Should().BeTrue();

        parsed.Packages.Should().HaveCount(1);
        parsed.Packages[0].Name.Should().Be("SUITE_ENTERPRISE");
        parsed.Packages[0].Components.Should().Contain(["CAD_PRO", "FEA_SOLVER", "SIM_RENDER"]);

        // Verify conversion to Symbolon plans
        var conv = FlexNetLicenseParser.ConvertToSymbolonPlans(parsed);
        conv.ConvertedPlans.Should().HaveCount(3);
        conv.Recommendations.Should().NotBeEmpty();
        conv.Recommendations.Should().Contain(r => r.Contains("Triad", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FlexNetOptionsTranspiler_TranspilesRulesToSymbolonPolicy()
    {
        string optContent = """
            # Options File for mysw_vd
            GROUP engineering jan.novak peter.kovac maria.horvathova
            GROUP contractors extern1 extern2
            HOST_GROUP simulation_cluster node-01 node-02 node-03

            RESERVE 5 CAD_PRO GROUP engineering
            MAX 2 CAD_PRO GROUP contractors
            EXCLUDE FEA_SOLVER GROUP contractors
            INCLUDE CAD_PRO HOST_GROUP simulation_cluster

            BORROW_LOWWATER CAD_PRO 3
            MAX_BORROW_HOURS CAD_PRO 168
            TIMEOUT CAD_PRO 1800
            TIMEOUTALL 3600
            LINGER CAD_PRO 300
            REPORTLOG +/var/log/report.log
            """;

        var report = FlexNetOptionsTranspiler.Transpile(optContent);

        report.Should().NotBeNull();
        report.TotalRulesParsed.Should().BeGreaterThanOrEqualTo(10);
        report.RecognizedDirectives.Should().Contain(["GROUP", "HOST_GROUP", "RESERVE", "MAX", "EXCLUDE", "INCLUDE", "BORROW_LOWWATER", "MAX_BORROW_HOURS", "TIMEOUT", "TIMEOUTALL", "LINGER", "REPORTLOG"]);

        var policy = report.Policy;
        policy.UserGroups.Should().HaveCount(2);
        var engGroup = policy.UserGroups.First(g => g.Name == "engineering");
        engGroup.Members.Should().Contain(["jan.novak", "peter.kovac", "maria.horvathova"]);

        policy.HostGroups.Should().HaveCount(1);
        policy.HostGroups[0].Members.Should().Contain(["node-01", "node-02", "node-03"]);

        // Allocations
        policy.SeatAllocations.Should().HaveCount(2);
        var res = policy.SeatAllocations.First(a => a.AllocationType == "Reservation");
        res.FeatureCode.Should().Be("CAD_PRO");
        res.Seats.Should().Be(5);
        res.TargetName.Should().Be("engineering");

        var maxLimit = policy.SeatAllocations.First(a => a.AllocationType == "Limit");
        maxLimit.Seats.Should().Be(2);

        // Entitlements
        policy.Entitlements.Should().HaveCount(2);
        policy.Entitlements.Should().Contain(e => e.Effect == "Exclude" && e.FeatureCode == "FEA_SOLVER");
        policy.Entitlements.Should().Contain(e => e.Effect == "Include" && e.TargetType == "HostGroup");

        // Borrowing
        policy.Borrowing.Enabled.Should().BeTrue();
        policy.Borrowing.MaxBorrowDurationDays.Should().Be(7); // 168 hours / 24
        policy.Borrowing.MinAvailableSeatsForBorrow.Should().Be(3);

        // Timeouts
        policy.IdleTimeoutSeconds.Should().Be(3600);
        policy.LingerSeconds.Should().Be(300);
        policy.FeatureTimeouts["CAD_PRO"].Should().Be(1800);
    }

    [Fact]
    public void FlexNetLogAnalyzer_ReconstructsConcurrency_AndCalculatesRightSizing()
    {
        string logContent = """
            09:00:00 (mysw_vd) TIMESTAMP 9/22/2026
            09:05:00 (mysw_vd) OUT: "CAD_PRO" alice@ws-01
            09:10:00 (mysw_vd) OUT: "CAD_PRO" bob@ws-02
            09:15:00 (mysw_vd) OUT: "CAD_PRO" charlie@ws-03
            09:20:00 (mysw_vd) OUT: "FEA_SOLVER" david@ws-compute
            09:30:00 (mysw_vd) DENIED: "CAD_PRO" eve@ws-04 (Licensed number of users already reached. (-4,342))
            09:35:00 (mysw_vd) DENIED: "CAD_PRO" frank@ws-05 (Licensed number of users already reached. (-4,342))
            10:00:00 (mysw_vd) IN: "CAD_PRO" alice@ws-01
            10:15:00 (mysw_vd) IN: "CAD_PRO" bob@ws-02
            10:30:00 (mysw_vd) IN: "CAD_PRO" charlie@ws-03
            11:00:00 (mysw_vd) IN: "FEA_SOLVER" david@ws-compute
            """;

        var result = FlexNetLogAnalyzer.Analyze(logContent);

        result.Should().NotBeNull();
        result.TotalEventsCount.Should().Be(10);
        result.OverallPeakConcurrency.Should().Be(4); // 3 CAD + 1 FEA at 09:20
        result.TotalDenialsAcrossAllFeatures.Should().Be(2);

        var cadStats = result.FeatureStatistics.First(s => s.FeatureCode == "CAD_PRO");
        cadStats.PeakConcurrency.Should().Be(3);
        cadStats.TotalCheckouts.Should().Be(3);
        cadStats.TotalCheckins.Should().Be(3);
        cadStats.TotalDenials.Should().Be(2);
        cadStats.UniqueUsersCount.Should().Be(5); // alice, bob, charlie, eve, frank
        cadStats.RecommendedSeats.Should().BeGreaterThanOrEqualTo(3);
        cadStats.RecommendedOverdraftBuffer.Should().BeGreaterThanOrEqualTo(1);
        cadStats.TopDenialReasons.Should().NotBeEmpty();

        var feaStats = result.FeatureStatistics.First(s => s.FeatureCode == "FEA_SOLVER");
        feaStats.PeakConcurrency.Should().Be(1);
        feaStats.TotalDenials.Should().Be(0);

        result.KeyInsights.Should().NotBeEmpty();
    }

    [Fact]
    public void KeygenImporter_ParsesJsonExportAndMapsToSymbolonEntities()
    {
        string keygenJson = """
            {
              "data": [
                {
                  "id": "pol_cad_01",
                  "type": "policies",
                  "attributes": {
                    "name": "CAD Floating Annual",
                    "code": "CAD-FLOAT",
                    "duration": 31536000,
                    "maxMachines": 25,
                    "floating": true,
                    "concurrent": true
                  }
                },
                {
                  "id": "usr_alice",
                  "type": "users",
                  "attributes": {
                    "email": "alice@company.com",
                    "firstName": "Alice",
                    "lastName": "Engineer"
                  }
                },
                {
                  "id": "lic_cad_99",
                  "type": "licenses",
                  "attributes": {
                    "key": "KEYGEN-CAD-1234-5678",
                    "name": "Acme CAD Floating Pool",
                    "status": "ACTIVE",
                    "expiry": "2027-12-31T23:59:59Z",
                    "maxMachines": 25
                  },
                  "relationships": {
                    "policy": { "data": { "id": "pol_cad_01", "type": "policies" } },
                    "user": { "data": { "id": "usr_alice", "type": "users" } }
                  }
                }
              ]
            }
            """;

        var summary = KeygenImporter.Parse(keygenJson);

        summary.Should().NotBeNull();
        summary.TotalRecordsRead.Should().Be(3);
        summary.Policies.Should().HaveCount(1);
        summary.Policies[0].Code.Should().Be("CAD-FLOAT");
        summary.Policies[0].MaxSeats.Should().Be(25);
        summary.Policies[0].LicenseModel.Should().Be("floating");

        summary.Users.Should().HaveCount(1);
        summary.Users[0].Email.Should().Be("alice@company.com");
        summary.Users[0].FullName.Should().Be("Alice Engineer");

        summary.Licenses.Should().HaveCount(1);
        summary.Licenses[0].Key.Should().Be("KEYGEN-CAD-1234-5678");
        summary.Licenses[0].PolicyId.Should().Be("pol_cad_01");
        summary.Licenses[0].UserId.Should().Be("usr_alice");
        summary.Licenses[0].MaxSeats.Should().Be(25);
        summary.Licenses[0].ExpiresAt.Should().NotBeNull();
    }
}
