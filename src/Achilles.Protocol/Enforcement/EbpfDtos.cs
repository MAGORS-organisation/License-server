using System.Text.Json.Serialization;

namespace Achilles.Protocol.Enforcement;

/// <summary>
/// Kernel BPF map entry representing an active lease binding to a cgroup or process ID.
/// </summary>
public sealed record EbpfLeaseEntry(
    [property: JsonPropertyName("cgroupId")] ulong CgroupId,
    [property: JsonPropertyName("pid")] int Pid,
    [property: JsonPropertyName("licenseId")] string LicenseId,
    [property: JsonPropertyName("expiresAt")] DateTimeOffset ExpiresAt,
    [property: JsonPropertyName("isActive")] bool IsActive);

/// <summary>
/// Security violation event emitted by eBPF socket filter when an unlicensed network connection is dropped.
/// </summary>
public sealed record EbpfViolationEvent(
    [property: JsonPropertyName("eventId")] string EventId,
    [property: JsonPropertyName("cgroupId")] ulong CgroupId,
    [property: JsonPropertyName("pid")] int Pid,
    [property: JsonPropertyName("destinationIp")] string DestinationIp,
    [property: JsonPropertyName("destinationPort")] int DestinationPort,
    [property: JsonPropertyName("timestamp")] DateTimeOffset Timestamp,
    [property: JsonPropertyName("reason")] string Reason);

/// <summary>
/// Diagnostics status of the kernel eBPF enforcement subsystem.
/// </summary>
public sealed record EbpfEnforcementStatus(
    [property: JsonPropertyName("isActive")] bool IsActive,
    [property: JsonPropertyName("attachedCgroups")] IReadOnlyList<string> AttachedCgroups,
    [property: JsonPropertyName("activeLeaseCount")] int ActiveLeaseCount,
    [property: JsonPropertyName("blockedAttemptsCount")] long BlockedAttemptsCount,
    [property: JsonPropertyName("driverMode")] string DriverMode,
    [property: JsonPropertyName("lastViolationTime")] DateTimeOffset? LastViolationTime);
