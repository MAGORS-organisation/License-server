using FluentAssertions;
using Symbolon.Domain.Chaos;
using Xunit;

namespace Symbolon.Domain.Tests.Chaos;

public sealed class ChaosMonkeyTests
{
    private readonly ChaosMonkeyRunner _runner = new();

    [Fact]
    public async Task RunNetworkPartition_Verifies_ZeroDoubleAllocations()
    {
        var result = await _runner.RunScenarioAsync("PARTITION", durationSeconds: 1);
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Score.Should().Be(100.0);
        result.Details.Should().Contain("Prienik: 0");
    }

    [Fact]
    public async Task RunClockSkew_Verifies_ToleranceAndExpiration()
    {
        var result = await _runner.RunScenarioAsync("CLOCK_SKEW", durationSeconds: 1);
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Score.Should().Be(100.0);
        result.Details.Should().Contain("Expirácia po TTL: OK");
    }

    [Fact]
    public async Task RunHsmDisconnection_Verifies_EnclaveFallback()
    {
        var result = await _runner.RunScenarioAsync("HSM", durationSeconds: 1);
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Score.Should().Be(100.0);
        result.Details.Should().Contain("softvérového enclave");
    }

    [Fact]
    public async Task RunSeatExhaustionStorm_Verifies_StrictCapacity()
    {
        var result = await _runner.RunScenarioAsync("STORM", durationSeconds: 1);
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Score.Should().Be(100.0);
        result.Details.Should().Contain("Nad-alokácia: 0");
    }

    [Fact]
    public async Task RunAllScenarios_Generates_ValidResilienceScorecard()
    {
        var scorecard = await _runner.RunAllScenariosAsync();
        scorecard.Should().NotBeNull();
        scorecard.Passed.Should().BeTrue();
        scorecard.OverallScore.Should().BeGreaterThanOrEqualTo(90.0);
        scorecard.Results.Should().HaveCount(4);
        scorecard.ReadinessAssessment.Should().Contain("Production");
    }
}
