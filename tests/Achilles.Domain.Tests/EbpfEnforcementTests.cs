using FluentAssertions;
using Achilles.Domain.Enforcement;
using Xunit;

namespace Achilles.Domain.Tests;

public class EbpfEnforcementTests
{
    [Fact]
    public void EvaluateSocketConnect_AllowsLoopback_WithoutLease()
    {
        var engine = new EbpfEnforcementEngine();
        var now = DateTimeOffset.UtcNow;

        // Loopback IPv4
        bool allowedIpv4 = engine.EvaluateSocketConnect(cgroupId: 100, pid: 1234, "127.0.0.1", 8080, now);
        allowedIpv4.Should().BeTrue();

        // Loopback IPv6
        bool allowedIpv6 = engine.EvaluateSocketConnect(cgroupId: 100, pid: 1234, "::1", 8080, now);
        allowedIpv6.Should().BeTrue();

        // Loopback hostname
        bool allowedHost = engine.EvaluateSocketConnect(cgroupId: 100, pid: 1234, "localhost", 8080, now);
        allowedHost.Should().BeTrue();

        // Loopback bypass must never record violation events
        engine.GetRecentViolations().Should().BeEmpty();
        engine.GetStatus().BlockedAttemptsCount.Should().Be(0);
    }

    [Fact]
    public void EvaluateSocketConnect_WithActiveLease_AllowsExternalConnection()
    {
        var engine = new EbpfEnforcementEngine();
        var now = DateTimeOffset.UtcNow;

        engine.RegisterActiveLease(cgroupId: 501, pid: 2001, "lic_enterprise_ai", now.AddMinutes(30));

        bool allowed = engine.EvaluateSocketConnect(cgroupId: 501, pid: 2001, "198.51.100.25", 443, now);
        allowed.Should().BeTrue();

        engine.GetRecentViolations().Should().BeEmpty();
        engine.GetStatus().BlockedAttemptsCount.Should().Be(0);
    }

    [Fact]
    public void EvaluateSocketConnect_WithoutLease_DropsConnectionAndEmitsViolation()
    {
        var engine = new EbpfEnforcementEngine();
        var now = DateTimeOffset.UtcNow;

        bool allowed = engine.EvaluateSocketConnect(cgroupId: 999, pid: 9999, "198.51.100.50", 443, now);
        allowed.Should().BeFalse(); // Dropped by kernel (-EPERM)

        var violations = engine.GetRecentViolations();
        violations.Should().HaveCount(1);
        violations[0].Reason.Should().Be("no_lease_assigned");
        violations[0].Pid.Should().Be(9999);
        violations[0].DestinationIp.Should().Be("198.51.100.50");

        engine.GetStatus().BlockedAttemptsCount.Should().Be(1);
    }

    [Fact]
    public void EvaluateSocketConnect_WithExpiredLease_DropsConnectionAndEmitsViolation()
    {
        var engine = new EbpfEnforcementEngine();
        var now = DateTimeOffset.UtcNow;

        // Lease expired 5 minutes ago
        engine.RegisterActiveLease(cgroupId: 600, pid: 3001, "lic_expired", now.AddMinutes(-5));

        bool allowed = engine.EvaluateSocketConnect(cgroupId: 600, pid: 3001, "203.0.113.10", 8443, now);
        allowed.Should().BeFalse();

        var violations = engine.GetRecentViolations();
        violations.Should().HaveCount(1);
        violations[0].Reason.Should().Be("lease_expired");
        engine.GetStatus().BlockedAttemptsCount.Should().Be(1);
    }

    [Fact]
    public void EvaluateSocketConnect_AfterRevocation_ImmediatelyDropsConnection()
    {
        var engine = new EbpfEnforcementEngine();
        var now = DateTimeOffset.UtcNow;

        // 1. Initially active
        engine.RegisterActiveLease(cgroupId: 700, pid: 4001, "lic_revokable", now.AddMinutes(15));
        engine.EvaluateSocketConnect(cgroupId: 700, pid: 4001, "198.51.100.1", 443, now).Should().BeTrue();

        // 2. Revoke lease
        bool revoked = engine.RevokeLease(cgroupId: 700, pid: 4001);
        revoked.Should().BeTrue();

        // 3. Subsequent connection attempt dropped immediately
        engine.EvaluateSocketConnect(cgroupId: 700, pid: 4001, "198.51.100.1", 443, now).Should().BeFalse();

        var violations = engine.GetRecentViolations();
        violations.Should().HaveCount(1);
        violations[0].Reason.Should().Be("lease_revoked");
    }

    [Fact]
    public void AttachCgroup_And_DetachCgroup_ManagesLifecycle()
    {
        var engine = new EbpfEnforcementEngine();

        engine.IsActive.Should().BeFalse();

        engine.AttachCgroup("/sys/fs/cgroup/workloads/ml-training", [443, 8080]);
        engine.IsActive.Should().BeTrue();
        engine.GetAttachedCgroups().Should().Contain("/sys/fs/cgroup/workloads/ml-training");

        bool detached = engine.DetachCgroup("/sys/fs/cgroup/workloads/ml-training");
        detached.Should().BeTrue();
        engine.IsActive.Should().BeFalse();
        engine.GetAttachedCgroups().Should().BeEmpty();
    }
}
