using FluentAssertions;
using Achilles.Domain.Migration;
using Achilles.Domain.PolicyRules;
using Xunit;

namespace Achilles.Domain.Tests;

public sealed class PolicyRuleTests
{
    [Theory]
    [InlineData("alice", "alice", true)]
    [InlineData("alice", "Alice", true)]
    [InlineData("ing.novak", "ing.*", true)]
    [InlineData("mgr.kovac", "ing.*", false)]
    [InlineData("cad-ws-01", "cad-*", true)]
    [InlineData("srv-01", "cad-*", false)]
    [InlineData("alice", "?lice", true)]
    public void WildcardMatcher_MatchesCorrectly(string input, string pattern, bool expected)
    {
        WildcardMatcher.Matches(input, pattern).Should().Be(expected);
    }

    [Theory]
    [InlineData("192.168.100.42", "192.168.100.0/24", true)]
    [InlineData("192.168.101.42", "192.168.100.0/24", false)]
    [InlineData("192.168.100.55", "192.168.100.*", true)]
    [InlineData("10.0.1.5", "192.168.100.*", false)]
    [InlineData("10.0.0.1", "10.0.0.1", true)]
    [InlineData("10.0.0.2", "10.0.0.1", false)]
    [InlineData("2001:db8::1234", "2001:db8::/32", true)]
    [InlineData("2001:db9::1234", "2001:db8::/32", false)]
    public void SubnetMatcher_MatchesCorrectly(string ip, string pattern, bool expected)
    {
        SubnetMatcher.MatchesSubnet(ip, pattern).Should().Be(expected);
    }

    [Fact]
    public void PolicyRuleSerializer_YamlRoundtrip_WorksAccurately()
    {
        var ruleSet = new PolicyRuleSet(
            Version: 1,
            LicenseId: "LIC-TEST-001",
            Groups:
            [
                new PolicyRuleGroup("CAD_ENGINEERS", ["alice", "bob", "ing.*"]),
                new PolicyRuleGroup("OFFICE_NET", ["192.168.100.0/24"])
            ],
            Rules:
            [
                new PolicyRule(PolicyRuleType.Deny, null, new RuleTarget(Group: "CONTRACTORS"), Feature: "CAD_PRO", Reason: "No contractors on CAD_PRO"),
                new PolicyRule(PolicyRuleType.Max, 5, new RuleTarget(Group: "CAD_ENGINEERS")),
                new PolicyRule(PolicyRuleType.Reserve, 2, new RuleTarget(Group: "CAD_ENGINEERS")),
                new PolicyRule(PolicyRuleType.Priority, 99, new RuleTarget(User: "alice"))
            ]);

        string yaml = PolicyRuleSerializer.ToYaml(ruleSet);
        yaml.Should().Contain("CAD_ENGINEERS");
        yaml.Should().Contain("OFFICE_NET");
        yaml.Should().Contain("deny");
        yaml.Should().Contain("max");
        yaml.Should().Contain("reserve");
        yaml.Should().Contain("priority");

        var deserialized = PolicyRuleSerializer.Parse(yaml);
        deserialized.LicenseId.Should().Be("LIC-TEST-001");
        deserialized.Groups.Should().HaveCount(2);
        deserialized.Rules.Should().HaveCount(4);

        deserialized.Rules[0].Type.Should().Be(PolicyRuleType.Deny);
        deserialized.Rules[0].Feature.Should().Be("CAD_PRO");
        deserialized.Rules[1].Type.Should().Be(PolicyRuleType.Max);
        deserialized.Rules[1].Value.Should().Be(5);
        deserialized.Rules[2].Type.Should().Be(PolicyRuleType.Reserve);
        deserialized.Rules[2].Value.Should().Be(2);
        deserialized.Rules[3].Type.Should().Be(PolicyRuleType.Priority);
        deserialized.Rules[3].Value.Should().Be(99);
    }

    [Fact]
    public void PolicyRuleSerializer_JsonRoundtrip_WorksAccurately()
    {
        var ruleSet = new PolicyRuleSet(
            Version: 1,
            LicenseId: "LIC-JSON",
            Groups: [],
            Rules:
            [
                new PolicyRule(PolicyRuleType.Deny, null, new RuleTarget(User: "malicious_user"))
            ]);

        string json = PolicyRuleSerializer.ToJson(ruleSet);
        var deserialized = PolicyRuleSerializer.Parse(json);
        deserialized.LicenseId.Should().Be("LIC-JSON");
        deserialized.Rules.Should().HaveCount(1);
        deserialized.Rules[0].Target?.User.Should().Be("malicious_user");
    }

    [Fact]
    public void PolicyRuleEngine_DenyRule_HaltsImmediately_ConformsToFLT24()
    {
        var ruleSet = new PolicyRuleSet(
            Version: 1,
            LicenseId: "LIC-FLT24",
            Groups:
            [
                new PolicyRuleGroup("RESTRICTED", ["contractor_*"])
            ],
            Rules:
            [
                new PolicyRule(PolicyRuleType.Deny, null, new RuleTarget(Group: "RESTRICTED"), Reason: "Contractors are prohibited from floating licenses"),
                new PolicyRule(PolicyRuleType.Reserve, 1, new RuleTarget(User: "contractor_bob"))
            ]);

        var ctx = new RuleEvaluationContext(
            LicenseId: "LIC-FLT24",
            UserId: "contractor_bob",
            MachineId: "ws-contractor",
            HostName: "ws-contractor",
            ClientIp: "10.0.0.50");

        var result = PolicyRuleEngine.Evaluate(ruleSet, ctx);

        result.Allowed.Should().BeFalse();
        result.DenyReason.Should().Contain("Contractors are prohibited");
        result.MatchedReservationTarget.Should().BeNull();
    }

    [Fact]
    public void PolicyRuleEngine_MaxRule_EnforcesGroupUsageQuota()
    {
        var ruleSet = new PolicyRuleSet(
            Version: 1,
            LicenseId: "LIC-MAX",
            Groups:
            [
                new PolicyRuleGroup("ANALYSTS", ["analyst_*"])
            ],
            Rules:
            [
                new PolicyRule(PolicyRuleType.Max, 3, new RuleTarget(Group: "ANALYSTS"))
            ]);

        // Case 1: Under quota
        var ctxAllowed = new RuleEvaluationContext(
            LicenseId: "LIC-MAX",
            UserId: "analyst_carol",
            CurrentlyHeldByClient: 0,
            GetActiveCountForTarget: _ => 2);

        var resAllowed = PolicyRuleEngine.Evaluate(ruleSet, ctxAllowed);
        resAllowed.Allowed.Should().BeTrue();

        // Case 2: Exceeding quota
        var ctxExceeded = new RuleEvaluationContext(
            LicenseId: "LIC-MAX",
            UserId: "analyst_carol",
            CurrentlyHeldByClient: 0,
            GetActiveCountForTarget: _ => 3);

        var resExceeded = PolicyRuleEngine.Evaluate(ruleSet, ctxExceeded);
        resExceeded.Allowed.Should().BeFalse();
        resExceeded.DenyReason.Should().Contain("Maximum limit");
    }

    [Fact]
    public void PolicyRuleEngine_ReserveAndPriority_QualifiedCorrectly()
    {
        var ruleSet = new PolicyRuleSet(
            Version: 1,
            LicenseId: "LIC-VIP",
            Groups:
            [
                new PolicyRuleGroup("VIP_USERS", ["vip_alice", "ceo"])
            ],
            Rules:
            [
                new PolicyRule(PolicyRuleType.Reserve, 2, new RuleTarget(Group: "VIP_USERS")),
                new PolicyRule(PolicyRuleType.Priority, 95, new RuleTarget(Group: "VIP_USERS"))
            ]);

        var ctx = new RuleEvaluationContext(
            LicenseId: "LIC-VIP",
            UserId: "vip_alice");

        var result = PolicyRuleEngine.Evaluate(ruleSet, ctx);
        result.Allowed.Should().BeTrue();
        result.MatchedReservationTarget.Should().Be("VIP_USERS");
        result.ResolvedPriority.Should().Be(95);
    }

    [Fact]
    public void FlexNetOptionsTranspiler_ToPolicyRuleSet_MapsCorrectly()
    {
        string optContent = @"
GROUP DEV_TEAM alice bob charlie
HOST_GROUP CAD_HOSTS ws01 ws02
EXCLUDE FEATURE_A GROUP DEV_TEAM
MAX 4 FEATURE_A GROUP DEV_TEAM
RESERVE 2 FEATURE_A GROUP DEV_TEAM
";

        var report = FlexNetOptionsTranspiler.Transpile(optContent);
        report.TotalRulesParsed.Should().BeGreaterThan(0);

        var ruleSet = report.ToPolicyRuleSet("LIC-TRANSPILED");
        ruleSet.LicenseId.Should().Be("LIC-TRANSPILED");
        ruleSet.Groups.Should().HaveCount(2);
        ruleSet.Rules.Should().HaveCount(3);

        ruleSet.Rules.Should().Contain(r => r.Type == PolicyRuleType.Deny && r.Target != null && r.Target.Group == "DEV_TEAM");
        ruleSet.Rules.Should().Contain(r => r.Type == PolicyRuleType.Max && r.Value == 4);
        ruleSet.Rules.Should().Contain(r => r.Type == PolicyRuleType.Reserve && r.Value == 2);
    }
}
