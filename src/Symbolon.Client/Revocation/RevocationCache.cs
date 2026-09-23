using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Json;
using System.Text.Json;
using Symbolon.Crypto;
using Symbolon.Format;

namespace Symbolon.Client.Revocation;

/// <summary>
/// Client-side revocation manager and cache enforcing spec/06-revocation-list.md.
/// Tracks revoked licenses, machines, compromised signing keys (kids), relays, and leases.
/// </summary>
public sealed class RevocationCache
{
    private readonly IKeyRing? _keyRing;
    private readonly ConcurrentDictionary<string, RevocationItem> _revokedLicenses = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, RevocationItem> _revokedMachines = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, RevocationItem> _revokedKids = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, RevocationItem> _revokedRelays = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, RevocationItem> _revokedLeases = new(StringComparer.Ordinal);

    private long _lastSeenSeq;

    public RevocationCache(IKeyRing? keyRing = null)
    {
        _keyRing = keyRing;
    }

    /// <summary>
    /// Gets the highest monotonic sequence number processed by this cache (RVL-7).
    /// </summary>
    public long LastSeenSeq => Interlocked.Read(ref _lastSeenSeq);

    public int TotalRevocationCount =>
        _revokedLicenses.Count + _revokedMachines.Count + _revokedKids.Count + _revokedRelays.Count + _revokedLeases.Count;

    /// <summary>
    /// Adds a single revocation item directly to the cache.
    /// </summary>
    public void Add(RevocationItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        switch (item.T)
        {
            case RevocationItem.TypeLicense:
                _revokedLicenses[item.Id] = item;
                break;
            case RevocationItem.TypeMachine:
                _revokedMachines[item.Id] = item;
                break;
            case RevocationItem.TypeKid:
                _revokedKids[item.Id] = item;
                if (_keyRing is SymbolonKeyRing skr)
                {
                    skr.Revoke(item.Id);
                }
                break;
            case RevocationItem.TypeRelay:
                _revokedRelays[item.Id] = item;
                break;
            case RevocationItem.TypeLease:
                _revokedLeases[item.Id] = item;
                break;
            default:
                // RVL-6: Unknown subject type MUST be ignored, not fail
                break;
        }
    }

    /// <summary>
    /// Applies a verified revocation list payload into the cache (RVL-7, RVL-8, RVL-9).
    /// Revocations are permanent (RVL-9).
    /// </summary>
    public bool ApplyRevocationList(RevocationListClaims claims)
    {
        ArgumentNullException.ThrowIfNull(claims);
        var symrl = claims.Symrl;

        long current = Interlocked.Read(ref _lastSeenSeq);
        if (symrl.Seq < current && current > 0)
        {
            return false; // Stale list (RVL-7)
        }

        foreach (var item in symrl.Revoked)
        {
            Add(item);
        }

        Interlocked.Exchange(ref _lastSeenSeq, symrl.Seq);
        return true;
    }

    /// <summary>
    /// Checks whether a license identifier or key is revoked.
    /// </summary>
    public bool IsLicenseRevoked(string licenseIdOrKey, [NotNullWhen(true)] out RevocationItem? item)
    {
        return _revokedLicenses.TryGetValue(licenseIdOrKey, out item);
    }

    public bool IsLicenseRevoked(string licenseIdOrKey) => _revokedLicenses.ContainsKey(licenseIdOrKey);

    /// <summary>
    /// Checks whether a machine fingerprint is revoked.
    /// </summary>
    public bool IsMachineRevoked(string machineFingerprint, [NotNullWhen(true)] out RevocationItem? item)
    {
        return _revokedMachines.TryGetValue(machineFingerprint, out item);
    }

    public bool IsMachineRevoked(string machineFingerprint) => _revokedMachines.ContainsKey(machineFingerprint);

    /// <summary>
    /// Checks whether a signing key ID (kid) is revoked (RVL-12).
    /// </summary>
    public bool IsKidRevoked(string kid, [NotNullWhen(true)] out RevocationItem? item)
    {
        return _revokedKids.TryGetValue(kid, out item);
    }

    public bool IsKidRevoked(string kid) => _revokedKids.ContainsKey(kid);

    /// <summary>
    /// Checks whether a relay node is revoked.
    /// </summary>
    public bool IsRelayRevoked(string relayId, [NotNullWhen(true)] out RevocationItem? item)
    {
        return _revokedRelays.TryGetValue(relayId, out item);
    }

    public bool IsRelayRevoked(string relayId) => _revokedRelays.ContainsKey(relayId);

    /// <summary>
    /// Checks whether a lease is revoked.
    /// </summary>
    public bool IsLeaseRevoked(string leaseId, [NotNullWhen(true)] out RevocationItem? item)
    {
        return _revokedLeases.TryGetValue(leaseId, out item);
    }

    public bool IsLeaseRevoked(string leaseId) => _revokedLeases.ContainsKey(leaseId);

    /// <summary>
    /// Fetches and synchronizes with the server's revocation endpoint (RVL-15, RVL-16).
    /// Supports delta synchronization via ?since={seq}.
    /// </summary>
    public async Task<bool> SyncAsync(
        HttpClient httpClient,
        Uri baseUri,
        RevocationListVerifier verifier,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(baseUri);
        ArgumentNullException.ThrowIfNull(verifier);

        long seq = LastSeenSeq;
        string relativePath = seq > 0
            ? $"v1/revocations/latest?since={seq}"
            : "v1/revocations/latest";

        var endpoint = new Uri(baseUri, relativePath);

        using var response = await httpClient.GetAsync(endpoint, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        // Parse JSON response containing pem or claims
        string content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        string pemToVerify = content;

        if (content.TrimStart().StartsWith('{'))
        {
            try
            {
                using var doc = JsonDocument.Parse(content);
                if (doc.RootElement.TryGetProperty("pem", out var pemProp))
                {
                    pemToVerify = pemProp.GetString() ?? content;
                }
            }
            catch (JsonException)
            {
                // Fallback to raw text
            }
        }

        var verification = verifier.Verify(pemToVerify);
        if (!verification.IsValid || verification.Claims is null)
        {
            return false;
        }

        return ApplyRevocationList(verification.Claims);
    }
}
