using Achilles.Crypto;
using Achilles.Domain;
using Achilles.Protocol;

namespace Achilles.ControlPlane;

public sealed class ControlPlaneLeaseTokenIssuer(ISignatureProvider signingKey, string issuer = "symbolon:control-plane") : ILeaseTokenIssuer
{
    private readonly LeaseTokenSigner _signer = new(signingKey);

    public string Issue(SeatAllocation allocation, IReadOnlyList<string>? entitlements = null)
    {
        ArgumentNullException.ThrowIfNull(allocation);

        var claims = new LeaseClaims
        {
            Iss = issuer,
            Sub = allocation.LicenseId,
            Jti = allocation.LeaseId ?? $"lse_{Guid.NewGuid():N}",
            Iat = allocation.AcquiredAt?.ToUnixTimeSeconds() ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Exp = allocation.ExpiresAt.ToUnixTimeSeconds(),
            Seat = allocation.SeatNo,
            Fp = allocation.HolderFingerprint ?? string.Empty,
            Ent = entitlements ?? ["core"],
            Gnt = null,
            Seq = allocation.LeaseSeq
        };

        return _signer.IssueToken(claims);
    }

    public IReadOnlyList<string> Issue(IReadOnlyList<SeatAllocation> allocations, IReadOnlyList<string>? entitlements = null)
    {
        ArgumentNullException.ThrowIfNull(allocations);
        var list = new List<string>(allocations.Count);
        foreach (var allocation in allocations)
        {
            list.Add(Issue(allocation, entitlements));
        }
        return list;
    }
}
