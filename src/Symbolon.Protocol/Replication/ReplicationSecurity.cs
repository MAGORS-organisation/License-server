using System.Security.Cryptography;
using System.Text;

namespace Symbolon.Protocol.Replication;

/// <summary>
/// Cryptographic security utilities for inter-cluster replication messages.
/// Provides HMAC-SHA256 authentication and anti-replay timestamp verification.
/// </summary>
public static class ReplicationSecurity
{
    private static readonly TimeSpan DefaultMaxTimeDrift = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Computes an HMAC-SHA256 uppercase hex signature over the given canonical payload using a shared cluster secret.
    /// </summary>
    public static string ComputeHmac(byte[] payload, string secret)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);

        byte[] keyBytes = Encoding.UTF8.GetBytes(secret);
        byte[] hash = HMACSHA256.HashData(keyBytes, payload);
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// Computes canonical UTF-8 payload representation for a sync request envelope.
    /// </summary>
    public static byte[] CreateCanonicalPayload(string senderRegionId, string targetRegionId, string deltaId, DateTimeOffset timestamp)
    {
        string canonical = $"{senderRegionId}:{targetRegionId}:{deltaId}:{timestamp.ToUnixTimeMilliseconds()}";
        return Encoding.UTF8.GetBytes(canonical);
    }

    /// <summary>
    /// Verifies the HMAC-SHA256 signature in constant time.
    /// </summary>
    public static bool VerifyHmac(byte[] payload, string signature, string secret)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (string.IsNullOrWhiteSpace(signature) || string.IsNullOrWhiteSpace(secret))
        {
            return false;
        }

        string expectedHex = ComputeHmac(payload, secret);
        byte[] expectedBytes = Encoding.UTF8.GetBytes(expectedHex);
        byte[] providedBytes = Encoding.UTF8.GetBytes(signature.Trim().ToUpperInvariant());

        return CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
    }

    /// <summary>
    /// Checks if the message timestamp is within the acceptable time drift window to prevent replay attacks.
    /// </summary>
    public static bool IsTimestampFresh(DateTimeOffset timestamp, DateTimeOffset now, TimeSpan? maxDrift = null)
    {
        TimeSpan drift = maxDrift ?? DefaultMaxTimeDrift;
        TimeSpan diff = (now - timestamp).Duration();
        return diff <= drift;
    }
}
