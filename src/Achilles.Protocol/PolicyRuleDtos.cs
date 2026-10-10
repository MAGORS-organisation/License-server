using System.Text.Json.Serialization;

namespace Achilles.Protocol;

public sealed record PolicyRuleGroupDto
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("members")]
    public required IReadOnlyList<string> Members { get; init; }

    [JsonPropertyName("hosts")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Hosts { get; init; }
}

public sealed record PolicyRuleItemDto
{
    [JsonPropertyName("type")]
    public required string Type { get; init; } // "deny", "max", "reserve", "priority"

    [JsonPropertyName("value")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Value { get; init; }

    [JsonPropertyName("group")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Group { get; init; }

    [JsonPropertyName("user")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? User { get; init; }

    [JsonPropertyName("subnet")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Subnet { get; init; }

    [JsonPropertyName("host")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Host { get; init; }

    [JsonPropertyName("hosts")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Hosts { get; init; }

    [JsonPropertyName("users")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Users { get; init; }

    [JsonPropertyName("groups")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Groups { get; init; }

    [JsonPropertyName("subnets")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Subnets { get; init; }

    [JsonPropertyName("feature")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Feature { get; init; }

    [JsonPropertyName("reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; init; }
}

public sealed record PolicyRuleSetDto
{
    [JsonPropertyName("version")]
    public int Version { get; init; }

    [JsonPropertyName("licenseId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LicenseId { get; init; }

    [JsonPropertyName("rawYaml")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RawYaml { get; init; }

    [JsonPropertyName("groups")]
    public IReadOnlyList<PolicyRuleGroupDto> Groups { get; init; } = [];

    [JsonPropertyName("rules")]
    public IReadOnlyList<PolicyRuleItemDto> Rules { get; init; } = [];
}

public sealed record UpdatePolicyRulesRequestDto
{
    [JsonPropertyName("rulesYaml")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RulesYaml { get; init; }

    [JsonPropertyName("rulesJson")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RulesJson { get; init; }
}

public sealed record SimulateRuleEvaluationRequestDto
{
    [JsonPropertyName("userId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UserId { get; init; }

    [JsonPropertyName("machineId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MachineId { get; init; }

    [JsonPropertyName("hostName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? HostName { get; init; }

    [JsonPropertyName("clientIp")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ClientIp { get; init; }

    [JsonPropertyName("features")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Features { get; init; }
}

public sealed record SimulateRuleEvaluationResponseDto
{
    [JsonPropertyName("allowed")]
    public required bool Allowed { get; init; }

    [JsonPropertyName("denyReason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DenyReason { get; init; }

    [JsonPropertyName("denyType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DenyType { get; init; }

    [JsonPropertyName("resolvedPriority")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ResolvedPriority { get; init; }

    [JsonPropertyName("matchedReservation")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MatchedReservation { get; init; }

    [JsonPropertyName("maxLimit")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxLimit { get; init; }
}
