using FluentAssertions;
using Xunit;

namespace Achilles.Protocol.Tests;

public sealed class FingerprintTests
{
    [Fact]
    public void Canonicalize_SortsKeysAlphabetically_AndNormalizes()
    {
        var components = new Dictionary<string, string>
        {
            ["machineId"] = "MAC-12345",
            ["board"] = "BOARD-XYZ",
            ["cpu"] = "CPU-INTEL"
        };

        string canonical = FingerprintHelper.Canonicalize(components);

        // Expect BOARD, then CPU, then MACHINEID sorted
        canonical.Should().Be("BOARD=BOARD-XYZ\nCPU=CPU-INTEL\nMACHINEID=MAC-12345\n");
    }

    [Fact]
    public void ComputeHash_ProducesExpectedSha256Format()
    {
        var components = new Dictionary<string, string>
        {
            ["machineId"] = "TEST-PC-1"
        };

        string hash = FingerprintHelper.ComputeHash(components);

        hash.Should().StartWith("sha256:");
        hash.Length.Should().Be(7 + 64); // "sha256:" (7) + 64 hex chars
    }

    [Fact]
    public void PseudonymizeHost_FPR2_ComputesDeterministicSaltedHash()
    {
        string host = "Dev-Workstation-01.corp.internal";
        string salt = "lic_01JQ8ZK4N9V2X6M0";

        string pseudo1 = FingerprintHelper.PseudonymizeHost(host, salt);
        string pseudo2 = FingerprintHelper.PseudonymizeHost("dev-workstation-01.corp.internal", salt);
        string diffSalt = FingerprintHelper.PseudonymizeHost(host, "lic_different_salt_123");

        pseudo1.Should().StartWith("sha256:");
        pseudo1.Should().Be(pseudo2); // Case-insensitive normalization
        pseudo1.Should().NotBe(diffSalt); // Salt changes output
        pseudo1.Should().NotContain("Dev-Workstation", because: "Raw hostname must never be leaked (FPR-2)");
    }

    [Fact]
    public void FilterValidComponents_FPR3_ExcludesPlaceholdersAndEmpty()
    {
        var raw = new Dictionary<string, string>
        {
            ["machineId"] = "4F5B7182-3A65-4309-8C2D-E71510B5E052",
            ["board"] = "None",
            ["cpu"] = "unknown",
            ["disk"] = "00000000-0000-0000-0000-000000000000",
            ["mac"] = "00:1A:2B:3C:4D:5E",
            ["empty"] = "   "
        };

        var filtered = FingerprintHelper.FilterValidComponents(raw);

        filtered.Should().ContainKey("machineId");
        filtered.Should().ContainKey("mac");
        filtered.Should().NotContainKey("board");
        filtered.Should().NotContainKey("cpu");
        filtered.Should().NotContainKey("disk");
        filtered.Should().NotContainKey("empty");
    }

    [Fact]
    public void MatchingEngine_FPR5_MatchAny_RequiresAtLeastOneMatch()
    {
        var stored = new Dictionary<string, string>
        {
            ["machineId"] = "ID-1",
            ["cpu"] = "CPU-1",
            ["board"] = "BOARD-1"
        };

        var incomingMatch = new Dictionary<string, string>
        {
            ["machineId"] = "ID-DIFF",
            ["cpu"] = "CPU-1", // Match 1
            ["board"] = "BOARD-DIFF"
        };

        var incomingFail = new Dictionary<string, string>
        {
            ["machineId"] = "ID-DIFF",
            ["cpu"] = "CPU-DIFF",
            ["board"] = "BOARD-DIFF"
        };

        var resPass = FingerprintMatchingEngine.EvaluateMatch(stored, incomingMatch, MatchingStrategies.MatchAny);
        resPass.IsMatch.Should().BeTrue();
        resPass.MatchedComponentsCount.Should().Be(1);

        var resFail = FingerprintMatchingEngine.EvaluateMatch(stored, incomingFail, MatchingStrategies.MatchAny);
        resFail.IsMatch.Should().BeFalse();
        resFail.MatchedComponentsCount.Should().Be(0);
    }

    [Fact]
    public void MatchingEngine_FPR5_MatchTwo_RequiresAtLeastTwoMatches()
    {
        var stored = new Dictionary<string, string>
        {
            ["machineId"] = "ID-1",
            ["cpu"] = "CPU-1",
            ["board"] = "BOARD-1",
            ["disk"] = "DISK-1"
        };

        var oneMatch = new Dictionary<string, string>
        {
            ["machineId"] = "ID-1",
            ["cpu"] = "CPU-DIFF",
            ["board"] = "BOARD-DIFF",
            ["disk"] = "DISK-DIFF"
        };

        var twoMatches = new Dictionary<string, string>
        {
            ["machineId"] = "ID-1",
            ["cpu"] = "CPU-1",
            ["board"] = "BOARD-DIFF",
            ["disk"] = "DISK-DIFF"
        };

        var resOne = FingerprintMatchingEngine.EvaluateMatch(stored, oneMatch, MatchingStrategies.MatchTwo);
        resOne.IsMatch.Should().BeFalse();

        var resTwo = FingerprintMatchingEngine.EvaluateMatch(stored, twoMatches, MatchingStrategies.MatchTwo);
        resTwo.IsMatch.Should().BeTrue();
        resTwo.MatchedComponentsCount.Should().Be(2);
    }

    [Fact]
    public void MatchingEngine_FPR6_MatchMost_IsDefaultStrategy()
    {
        var stored = new Dictionary<string, string>
        {
            ["machineId"] = "ID-1",
            ["cpu"] = "CPU-1",
            ["board"] = "BOARD-1"
        };

        var incoming = new Dictionary<string, string>
        {
            ["machineId"] = "ID-1",
            ["cpu"] = "CPU-1",
            ["board"] = "BOARD-DIFF"
        };

        // Strategy parameter is null -> defaults to match-most
        var res = FingerprintMatchingEngine.EvaluateMatch(stored, incoming, null);

        res.StrategyUsed.Should().Be(MatchingStrategies.MatchMost);
        res.IsMatch.Should().BeTrue(); // 2 out of 3 is majority (> 1.5)
        res.MatchedComponentsCount.Should().Be(2);
        res.CommonComponentsCount.Should().Be(3);
    }

    [Fact]
    public void MatchingEngine_FPR7_OnlyCommonComponentsAreEvaluated()
    {
        var stored = new Dictionary<string, string>
        {
            ["machineId"] = "ID-1",
            ["cpu"] = "CPU-1",
            ["extraStored"] = "EXTRA-1"
        };

        var incoming = new Dictionary<string, string>
        {
            ["machineId"] = "ID-1",
            ["cpu"] = "CPU-1",
            ["extraIncoming"] = "EXTRA-2"
        };

        var res = FingerprintMatchingEngine.EvaluateMatch(stored, incoming, MatchingStrategies.MatchAll);

        res.CommonComponentsCount.Should().Be(2, because: "extraStored and extraIncoming are not in common");
        res.MatchedComponentsCount.Should().Be(2);
        res.IsMatch.Should().BeTrue();
    }

    [Fact]
    public void MatchingEngine_FPR8_MatchMost_DegradesToMatchAll_WhenCommonCountLessThanTwo()
    {
        var stored = new Dictionary<string, string>
        {
            ["machineId"] = "ID-1"
        };

        var incomingMatch = new Dictionary<string, string>
        {
            ["machineId"] = "ID-1"
        };

        var incomingFail = new Dictionary<string, string>
        {
            ["machineId"] = "ID-2"
        };

        // Common count is 1 (< 2). Under FPR-8, match-most MUST behave as match-all!
        var resMatch = FingerprintMatchingEngine.EvaluateMatch(stored, incomingMatch, MatchingStrategies.MatchMost);
        resMatch.CommonComponentsCount.Should().Be(1);
        resMatch.IsMatch.Should().BeTrue();

        var resFail = FingerprintMatchingEngine.EvaluateMatch(stored, incomingFail, MatchingStrategies.MatchMost);
        resFail.CommonComponentsCount.Should().Be(1);
        resFail.IsMatch.Should().BeFalse();
        resFail.FailureReason.Should().Contain("FPR-8");
    }

    [Fact]
    public void MatchingEngine_NoCommonComponents_ReturnsFalse()
    {
        var stored = new Dictionary<string, string> { ["machineId"] = "ID-1" };
        var incoming = new Dictionary<string, string> { ["cpu"] = "CPU-1" };

        var res = FingerprintMatchingEngine.EvaluateMatch(stored, incoming, MatchingStrategies.MatchAny);

        res.IsMatch.Should().BeFalse();
        res.CommonComponentsCount.Should().Be(0);
        res.FailureReason.Should().Contain("FPR-7");
    }
}
