using Symbolon.Domain.Security;
using Xunit;

namespace Symbolon.Domain.Tests;

public class FraudDetectionTests
{
    [Fact]
    public async Task EvaluateAccess_WhenSingleAccess_ReturnsNoneRisk()
    {
        var service = new FraudDetectionService();
        var now = DateTimeOffset.UtcNow;

        var access = new FraudAccessEvent(
            LicenseId: "lic_test_1",
            FingerprintHash: "fp_hash_1",
            FingerprintComponents: null,
            MachineId: "node-1",
            UserId: "alice",
            IpAddress: "85.237.100.1", // Bratislava GeoIP
            Location: null,
            Timestamp: now);

        var result = await service.EvaluateAccessAsync(access);

        Assert.False(result.IsSuspicious);
        Assert.Equal(FraudRiskLevel.None, result.RiskLevel);
        Assert.Null(result.RiskType);
        Assert.NotNull(result.CurrentLocation);
        Assert.Equal("Bratislava", result.CurrentLocation.City);
    }

    [Fact]
    public async Task EvaluateAccess_WhenImpossibleTravelVelocityExceeded_ReturnsCriticalRisk()
    {
        var service = new FraudDetectionService(maxVelocityKmH: 900.0);
        var t0 = DateTimeOffset.UtcNow;

        // 1. First access from Bratislava
        var access1 = new FraudAccessEvent(
            LicenseId: "lic_test_1",
            FingerprintHash: "fp_hash_user_alice",
            FingerprintComponents: null,
            MachineId: "laptop-alice",
            UserId: "alice",
            IpAddress: "85.237.100.1", // Bratislava, Slovakia
            Location: null,
            Timestamp: t0);

        var result1 = await service.EvaluateAccessAsync(access1);
        Assert.False(result1.IsSuspicious);

        // 2. Second access 15 minutes later from Tokyo (~9100 km away -> velocity > 36,000 km/h)
        var access2 = new FraudAccessEvent(
            LicenseId: "lic_test_1",
            FingerprintHash: "fp_hash_user_alice",
            FingerprintComponents: null,
            MachineId: "laptop-alice",
            UserId: "alice",
            IpAddress: "133.242.1.1", // Tokyo, Japan
            Location: null,
            Timestamp: t0.AddMinutes(15));

        var result2 = await service.EvaluateAccessAsync(access2);

        Assert.True(result2.IsSuspicious);
        Assert.Equal(FraudRiskLevel.Critical, result2.RiskLevel);
        Assert.Equal("impossible_travel", result2.RiskType);
        Assert.NotNull(result2.VelocityKmH);
        Assert.True(result2.VelocityKmH > 1000.0);
        Assert.NotNull(result2.DistanceKm);
        Assert.True(result2.DistanceKm > 8000.0);

        var anomalies = service.GetRecentAnomalies();
        Assert.Single(anomalies);
        Assert.Equal("impossible_travel", anomalies[0].RiskType);
        Assert.Equal("alice", anomalies[0].UserId);
    }

    [Fact]
    public async Task EvaluateAccess_WhenNormalTravel_ReturnsNoAnomaly()
    {
        var service = new FraudDetectionService(maxVelocityKmH: 900.0);
        var t0 = DateTimeOffset.UtcNow;

        // 1. Access from Bratislava
        var access1 = new FraudAccessEvent(
            LicenseId: "lic_test_normal",
            FingerprintHash: "fp_hash_bob",
            FingerprintComponents: null,
            MachineId: "workstation-bob",
            UserId: "bob",
            IpAddress: "85.237.100.1", // Bratislava
            Location: null,
            Timestamp: t0);

        await service.EvaluateAccessAsync(access1);

        // 2. Access 90 minutes later from Vienna (~60 km -> ~40 km/h)
        var access2 = new FraudAccessEvent(
            LicenseId: "lic_test_normal",
            FingerprintHash: "fp_hash_bob",
            FingerprintComponents: null,
            MachineId: "workstation-bob",
            UserId: "bob",
            IpAddress: "84.115.1.1", // Vienna
            Location: null,
            Timestamp: t0.AddMinutes(90));

        var result2 = await service.EvaluateAccessAsync(access2);

        Assert.False(result2.IsSuspicious);
        Assert.Equal(FraudRiskLevel.None, result2.RiskLevel);
    }

    [Fact]
    public async Task EvaluateAccess_WhenVmCloningDetected_ReturnsCriticalRisk()
    {
        var service = new FraudDetectionService();
        var t0 = DateTimeOffset.UtcNow;
        string clonedFingerprint = "fp_hardware_cloned_vm_001";

        // 1. Machine A check-in from New York
        var access1 = new FraudAccessEvent(
            LicenseId: "lic_cad_pro",
            FingerprintHash: clonedFingerprint,
            FingerprintComponents: new Dictionary<string, string>
            {
                ["smbios_uuid"] = "4c4c4544-004a-4a10-8043-b2c04f505332",
                ["mac"] = "00:50:56:C0:00:08"
            },
            MachineId: "vm-primary",
            UserId: "engineer1",
            IpAddress: "198.51.100.42", // Public IP in New York
            Location: null,
            Timestamp: t0);

        var result1 = await service.EvaluateAccessAsync(access1);
        Assert.False(result1.IsSuspicious);

        // 2. Machine B check-in 2 minutes later with exact same hardware from Sydney
        var access2 = new FraudAccessEvent(
            LicenseId: "lic_cad_pro",
            FingerprintHash: clonedFingerprint,
            FingerprintComponents: new Dictionary<string, string>
            {
                ["smbios_uuid"] = "4c4c4544-004a-4a10-8043-b2c04f505332",
                ["mac"] = "00:50:56:C0:00:08"
            },
            MachineId: "vm-clone",
            UserId: "engineer2",
            IpAddress: "203.0.113.88", // Public IP in Sydney
            Location: null,
            Timestamp: t0.AddMinutes(2));

        var result2 = await service.EvaluateAccessAsync(access2);

        Assert.True(result2.IsSuspicious);
        Assert.Equal(FraudRiskLevel.Critical, result2.RiskLevel);
        Assert.Equal("vm_cloning", result2.RiskType);
        Assert.Contains("VM Cloning detected", result2.Description, StringComparison.OrdinalIgnoreCase);

        var anomalies = service.GetRecentAnomalies();
        Assert.Single(anomalies);
        Assert.Equal("vm_cloning", anomalies[0].RiskType);
        Assert.Equal("lic_cad_pro", anomalies[0].LicenseId);
    }

    [Fact]
    public async Task EvaluateAccess_WhenLocalOrIntranetIp_BypassesVmCloning()
    {
        var service = new FraudDetectionService();
        var t0 = DateTimeOffset.UtcNow;
        string fp = "fp_local_dev_1";

        var access1 = new FraudAccessEvent(
            LicenseId: "lic_dev",
            FingerprintHash: fp,
            FingerprintComponents: null,
            MachineId: "dev-box",
            UserId: "dev",
            IpAddress: "127.0.0.1",
            Location: null,
            Timestamp: t0);

        var access2 = new FraudAccessEvent(
            LicenseId: "lic_dev",
            FingerprintHash: fp,
            FingerprintComponents: null,
            MachineId: "dev-box",
            UserId: "dev",
            IpAddress: "192.168.1.15",
            Location: null,
            Timestamp: t0.AddMinutes(1));

        var result1 = await service.EvaluateAccessAsync(access1);
        var result2 = await service.EvaluateAccessAsync(access2);

        Assert.False(result1.IsSuspicious);
        Assert.False(result2.IsSuspicious);
        Assert.Empty(service.GetRecentAnomalies());
    }

    [Fact]
    public void HaversineFormula_CalculatesKnownDistanceAccurately()
    {
        // Bratislava (48.1486, 17.1077) to Vienna (48.2082, 16.3738) is approx ~55-65 km
        double distance = FraudDetectionService.CalculateDistanceKm(48.1486, 17.1077, 48.2082, 16.3738);
        Assert.InRange(distance, 50.0, 70.0);

        // London to New York is approx ~5560-5590 km
        double transatlantic = FraudDetectionService.CalculateDistanceKm(51.5074, -0.1278, 40.7128, -74.0060);
        Assert.InRange(transatlantic, 5500.0, 5700.0);
    }
}
