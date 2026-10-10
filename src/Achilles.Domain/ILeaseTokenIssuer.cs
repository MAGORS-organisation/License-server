namespace Achilles.Domain;

/// <summary>
/// Issues cryptographically signed Lease Tokens for confirmed seat allocations.
/// </summary>
public interface ILeaseTokenIssuer
{
    string Issue(SeatAllocation allocation, IReadOnlyList<string>? entitlements = null);
    IReadOnlyList<string> Issue(IReadOnlyList<SeatAllocation> allocations, IReadOnlyList<string>? entitlements = null);
}
