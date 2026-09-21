using Symbolon.Crypto;
using Symbolon.Domain;
using Symbolon.Protocol;

namespace Symbolon.Relay;

/// <summary>
/// Issuer producing compact JWS Lease Tokens for the relay using its assigned lease key.
/// </summary>
internal sealed class RelayLeaseTokenIssuer : ILeaseTokenIssuer
{
    private readonly LeaseTokenSigner _signer;
    private readonly string _issuerId;
    private readonly string? _grantId;

    public RelayLeaseTokenIssuer(ISignatureProvider key, string relayId, string? grantId = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(relayId);

        _signer = new LeaseTokenSigner(key);
        _issuerId = $"relay:{relayId}";
        _grantId = grantId;
    }

    public string Issue(SeatAllocation allocation, IReadOnlyList<string>? entitlements = null)
    {
        ArgumentNullException.ThrowIfNull(allocation);

        var claims = new LeaseClaims
        {
            Iss = _issuerId,
            Sub = allocation.LicenseId,
            Jti = allocation.LeaseId ?? $"lse_{Guid.NewGuid():N}",
            Iat = allocation.AcquiredAt?.ToUnixTimeSeconds() ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Exp = allocation.ExpiresAt.ToUnixTimeSeconds(),
            Seat = allocation.SeatNo,
            Fp = allocation.HolderFingerprint ?? string.Empty,
            Ent = entitlements ?? ["core"],
            Gnt = _grantId,
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
