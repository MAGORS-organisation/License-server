using System.Security.Cryptography;
using System.Text;

namespace Symbolon.Domain.Webhooks;

public static class WebhookSecurity
{
    private static readonly TimeSpan DefaultTolerance = TimeSpan.FromMinutes(5);

    public static string ComputeSignature(string secret, long timestamp, string payloadJson)
    {
        ArgumentNullException.ThrowIfNull(secret);
        ArgumentNullException.ThrowIfNull(payloadJson);

        string stringToSign = $"{timestamp}.{payloadJson}";
        byte[] keyBytes = Encoding.UTF8.GetBytes(secret);
        byte[] dataBytes = Encoding.UTF8.GetBytes(stringToSign);

        byte[] hash = HMACSHA256.HashData(keyBytes, dataBytes);
        return Convert.ToHexStringLower(hash);
    }

    public static string BuildSignatureHeader(string secret, long timestamp, string payloadJson)
    {
        string signature = ComputeSignature(secret, timestamp, payloadJson);
        return $"t={timestamp},v1={signature}";
    }

    public static bool VerifySignatureHeader(
        string secret,
        string? headerValue,
        string payloadJson,
        TimeSpan? tolerance = null,
        DateTimeOffset? now = null)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(headerValue) || payloadJson is null)
        {
            return false;
        }

        long? timestamp = null;
        string? signatureHex = null;

        var parts = headerValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            var kv = part.Split('=', 2, StringSplitOptions.TrimEntries);
            if (kv.Length != 2) continue;

            if (string.Equals(kv[0], "t", StringComparison.OrdinalIgnoreCase) && long.TryParse(kv[1], System.Globalization.CultureInfo.InvariantCulture, out long parsedTs))
            {
                timestamp = parsedTs;
            }
            else if (string.Equals(kv[0], "v1", StringComparison.OrdinalIgnoreCase) || string.Equals(kv[0], "sha256", StringComparison.OrdinalIgnoreCase))
            {
                signatureHex = kv[1];
            }
        }

        if (timestamp is null || string.IsNullOrWhiteSpace(signatureHex))
        {
            return false;
        }

        var currentTime = now ?? DateTimeOffset.UtcNow;
        var headerTime = DateTimeOffset.FromUnixTimeSeconds(timestamp.Value);
        var maxDrift = tolerance ?? DefaultTolerance;

        if (currentTime - headerTime > maxDrift || headerTime - currentTime > maxDrift)
        {
            return false;
        }

        string expectedSignature = ComputeSignature(secret, timestamp.Value, payloadJson);

        byte[] expectedBytes = Encoding.UTF8.GetBytes(expectedSignature);
        byte[] actualBytes = Encoding.UTF8.GetBytes(signatureHex);

        return CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }
}
