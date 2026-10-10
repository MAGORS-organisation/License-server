using System.Globalization;
using System.Text.Json;
using FluentAssertions;
using Achilles.Domain.Lifecycle;
using Achilles.Domain.Webhooks;
using Xunit;

namespace Achilles.Domain.Tests;

public sealed class WebhookSecurityAndLifecycleTests
{
    private const string TestSecret = "sym_sec_test_secret_key_1234567890abcdef";
    private const string SamplePayload = "{\"event\":\"lease.denied\",\"tenantId\":\"acme\",\"timestamp\":\"2026-09-22T12:00:00Z\"}";

    [Fact]
    public void ComputeSignature_ProducesValidHexHash()
    {
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string sig = WebhookSecurity.ComputeSignature(TestSecret, timestamp, SamplePayload);

        sig.Should().NotBeNullOrWhiteSpace();
        sig.Length.Should().Be(64); // SHA-256 hex string length
    }

    [Fact]
    public void BuildAndVerifySignatureHeader_SucceedsForValidSecretAndTimestamp()
    {
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string header = WebhookSecurity.BuildSignatureHeader(TestSecret, timestamp, SamplePayload);

        header.Should().StartWith("t=");
        header.Should().Contain(",v1=");

        bool isValid = WebhookSecurity.VerifySignatureHeader(TestSecret, header, SamplePayload);
        isValid.Should().BeTrue();
    }

    [Fact]
    public void VerifySignatureHeader_FailsForWrongSecret()
    {
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string header = WebhookSecurity.BuildSignatureHeader(TestSecret, timestamp, SamplePayload);

        bool isValid = WebhookSecurity.VerifySignatureHeader("wrong_secret_key", header, SamplePayload);
        isValid.Should().BeFalse();
    }

    [Fact]
    public void VerifySignatureHeader_FailsForTamperedPayload()
    {
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string header = WebhookSecurity.BuildSignatureHeader(TestSecret, timestamp, SamplePayload);
        string tamperedPayload = "{\"event\":\"lease.denied\",\"tenantId\":\"hacked\"}";

        bool isValid = WebhookSecurity.VerifySignatureHeader(TestSecret, header, tamperedPayload);
        isValid.Should().BeFalse();
    }

    [Fact]
    public void VerifySignatureHeader_FailsWhenTimestampExceedsDriftWindow()
    {
        // Timestamp 10 minutes ago
        long oldTimestamp = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds();
        string sig = WebhookSecurity.ComputeSignature(TestSecret, oldTimestamp, SamplePayload);
        string header = string.Create(CultureInfo.InvariantCulture, $"t={oldTimestamp},v1={sig}");

        // Allowed drift is 5 min (TimeSpan.FromMinutes(5))
        bool isValid = WebhookSecurity.VerifySignatureHeader(TestSecret, header, SamplePayload, tolerance: TimeSpan.FromMinutes(5));
        isValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalid_header")]
    [InlineData("t=not_a_number,v1=abc")]
    [InlineData("t=12345678")]
    public void VerifySignatureHeader_FailsForMalformedHeader(string? header)
    {
        bool isValid = WebhookSecurity.VerifySignatureHeader(TestSecret, header, SamplePayload);
        isValid.Should().BeFalse();
    }

    [Fact]
    public void WebhookAdapters_FormatSlack_ProducesExpectedAlertColor()
    {
        var deniedEvent = new WebhookEvent(
            Guid.NewGuid().ToString("N"),
            WebhookEventTypes.LeaseDenied,
            "tenant-corp",
            DateTimeOffset.UtcNow,
            JsonDocument.Parse("{\"licenseId\":\"lic-123\",\"reason\":\"seats_exhausted\"}").RootElement);

        string slackJson = WebhookAdapters.FormatSlack(deniedEvent);
        slackJson.Should().Contain("#E01E5A"); // Red warning color for denials
        slackJson.Should().Contain(WebhookEventTypes.LeaseDenied);
        slackJson.Should().Contain("tenant-corp");

        var createdEvent = new WebhookEvent(
            Guid.NewGuid().ToString("N"),
            WebhookEventTypes.LicenseCreated,
            "tenant-corp",
            DateTimeOffset.UtcNow,
            JsonDocument.Parse("{\"licenseId\":\"lic-456\"}").RootElement);

        string createdSlackJson = WebhookAdapters.FormatSlack(createdEvent);
        createdSlackJson.Should().Contain("#2EB67D"); // Green success color
    }

    [Fact]
    public void WebhookAdapters_FormatTeams_ProducesAdaptiveCard()
    {
        var evt = new WebhookEvent(
            Guid.NewGuid().ToString("N"),
            WebhookEventTypes.LicenseExpiringSoon,
            "tenant-dev",
            DateTimeOffset.UtcNow,
            JsonDocument.Parse("{\"licenseId\":\"lic-expiring\",\"daysRemaining\":5}").RootElement);

        string teamsJson = WebhookAdapters.FormatTeams(evt);
        teamsJson.Should().Contain("MessageCard");
        teamsJson.Should().Contain("tenant-dev");
        teamsJson.Should().Contain("5");
    }

    [Fact]
    public void LicenseLifecycleEngine_EvaluateLicense_DetectsPerpetualAndActive()
    {
        var now = DateTimeOffset.UtcNow;

        var perpetual = new LicenseLifecycleInfo("lic-perp", "tenant-1", "PROD", "cust-1", null, "active");
        var resPerp = LicenseLifecycleEngine.EvaluateLicense(perpetual, now, 14, 7);
        resPerp.EvaluatedState.Should().Be(LicenseLifecycleState.Perpetual);
        resPerp.GeneratedEvent.Should().BeNull();

        var farFuture = new LicenseLifecycleInfo("lic-act", "tenant-1", "PROD", "cust-1", now.AddDays(45), "active");
        var resFar = LicenseLifecycleEngine.EvaluateLicense(farFuture, now, 14, 7);
        resFar.EvaluatedState.Should().Be(LicenseLifecycleState.ActiveNormal);
        resFar.GeneratedEvent.Should().BeNull();
    }

    [Fact]
    public void LicenseLifecycleEngine_EvaluateLicense_DetectsExpiringSoon()
    {
        var now = DateTimeOffset.UtcNow;
        var expiring = new LicenseLifecycleInfo("lic-exp", "tenant-1", "CAD_PRO", "cust-1", now.AddDays(8), "active");

        var result = LicenseLifecycleEngine.EvaluateLicense(expiring, now, 14, 7);
        result.EvaluatedState.Should().Be(LicenseLifecycleState.ExpiringSoon);
        result.GeneratedEvent.Should().NotBeNull();
        result.GeneratedEvent!.EventType.Should().Be(WebhookEventTypes.LicenseExpiringSoon);
    }

    [Fact]
    public void LicenseLifecycleEngine_EvaluateLicense_DetectsSoftGrace()
    {
        var now = DateTimeOffset.UtcNow;

        // Expired 3 days ago, soft grace is 7 days -> SoftGrace
        var soft = new LicenseLifecycleInfo("lic-soft", "tenant-1", "CAD_PRO", "cust-1", now.AddDays(-3), "active", SoftGraceDays: 7);
        var resSoft = LicenseLifecycleEngine.EvaluateLicense(soft, now, 14, 7);
        resSoft.EvaluatedState.Should().Be(LicenseLifecycleState.SoftGrace);
        resSoft.GeneratedEvent.Should().NotBeNull();
        resSoft.GeneratedEvent!.EventType.Should().Be(WebhookEventTypes.LicenseGraceEntered);
    }

    [Fact]
    public void LicenseLifecycleEngine_EvaluateLicense_DetectsExpiredAfterGrace()
    {
        var now = DateTimeOffset.UtcNow;

        // Expired 15 days ago, soft grace 7 days -> Expired
        var exp = new LicenseLifecycleInfo("lic-dead", "tenant-1", "CAD_PRO", "cust-1", now.AddDays(-15), "active", SoftGraceDays: 7);
        var resExp = LicenseLifecycleEngine.EvaluateLicense(exp, now, 14, 7);
        resExp.EvaluatedState.Should().Be(LicenseLifecycleState.Expired);
        resExp.GeneratedEvent.Should().NotBeNull();
        resExp.GeneratedEvent!.EventType.Should().Be(WebhookEventTypes.LicenseExpired);
    }

    [Fact]
    public void LicenseLifecycleEngine_EvaluateAll_AggregatesReportCorrectly()
    {
        var now = DateTimeOffset.UtcNow;
        var licenses = new List<LicenseLifecycleInfo>
        {
            new("lic-1", "t1", "P1", "c1", null, "active"),                         // Perpetual
            new("lic-2", "t1", "P1", "c1", now.AddDays(50), "active"),             // ActiveNormal
            new("lic-3", "t1", "P1", "c1", now.AddDays(5), "active"),              // ExpiringSoon
            new("lic-4", "t1", "P1", "c1", now.AddDays(-2), "active", 7),          // SoftGrace
            new("lic-5", "t1", "P1", "c1", now.AddDays(-20), "active", 7)          // Expired
        };

        var report = LicenseLifecycleEngine.EvaluateAll(licenses, now, 14, 7);
        report.TotalEvaluated.Should().Be(5);
        report.PerpetualCount.Should().Be(1);
        report.ActiveNormalCount.Should().Be(1);
        report.ExpiringSoonCount.Should().Be(1);
        report.GraceEnteredCount.Should().Be(1);
        report.ExpiredCount.Should().Be(1);
        report.EventsToDispatch.Should().HaveCount(3);
        report.Items.Should().HaveCount(5);
    }
}
