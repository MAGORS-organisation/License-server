using Achilles.Protocol.Enforcement;

namespace Achilles.Domain.Enforcement;

/// <summary>
/// Contract for Linux eBPF kernel socket enforcement and BPF map management.
/// Gates network socket access at the kernel boundary based on active, unexpired license leases.
/// </summary>
public interface IEbpfEnforcementEngine
{
    bool IsActive { get; }
    string DriverMode { get; }

    void AttachCgroup(string cgroupPath, IEnumerable<int>? protectedPorts = null);
    bool DetachCgroup(string cgroupPath);
    IReadOnlyList<string> GetAttachedCgroups();

    void RegisterActiveLease(ulong cgroupId, int pid, string licenseId, DateTimeOffset expiresAt);
    bool RevokeLease(ulong cgroupId, int pid);
    EbpfLeaseEntry? GetLease(ulong cgroupId, int pid);

    bool EvaluateSocketConnect(ulong cgroupId, int pid, string destinationIp, int destinationPort, DateTimeOffset now);

    IReadOnlyList<EbpfViolationEvent> GetRecentViolations(int limit = 50);
    EbpfEnforcementStatus GetStatus();
}
