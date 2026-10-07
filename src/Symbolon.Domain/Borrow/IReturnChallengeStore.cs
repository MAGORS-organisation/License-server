using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Symbolon.Domain.Borrow;

/// <summary>
/// Storage contract for single-use cryptographic return challenges (nonces) for early return proof-of-possession (FLT-21, FLT-22).
/// </summary>
public interface IReturnChallengeStore
{
    Task<(string Nonce, DateTimeOffset ExpiresAt)> CreateChallengeAsync(
        string leaseId,
        TimeSpan ttl,
        CancellationToken ct = default);

    Task<bool> TryConsumeChallengeAsync(
        string leaseId,
        string nonce,
        DateTimeOffset now,
        CancellationToken ct = default);
}

/// <summary>
/// Thread-safe in-memory challenge store with automatic expiration and single-use anti-replay protection.
/// </summary>
public sealed class InMemoryReturnChallengeStore : IReturnChallengeStore
{
    private sealed record ChallengeEntry(string Nonce, DateTimeOffset ExpiresAt);

    private readonly ConcurrentDictionary<string, ChallengeEntry> _challenges = new(StringComparer.Ordinal);

    public Task<(string Nonce, DateTimeOffset ExpiresAt)> CreateChallengeAsync(
        string leaseId,
        TimeSpan ttl,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseId);

        // Generate 32 cryptographically secure random bytes
        byte[] nonceBytes = RandomNumberGenerator.GetBytes(32);
        string nonce = Convert.ToHexString(nonceBytes);

        DateTimeOffset expiresAt = DateTimeOffset.UtcNow.Add(ttl);
        _challenges[leaseId] = new ChallengeEntry(nonce, expiresAt);

        // Clean up expired entries periodically
        CleanupExpired();

        return Task.FromResult((nonce, expiresAt));
    }

    public Task<bool> TryConsumeChallengeAsync(
        string leaseId,
        string nonce,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseId);
        ArgumentException.ThrowIfNullOrWhiteSpace(nonce);

        if (_challenges.TryRemove(leaseId, out var entry))
        {
            if (entry.ExpiresAt >= now)
            {
                try
                {
                    byte[] expected = Convert.FromHexString(entry.Nonce);
                    byte[] actual = Convert.FromHexString(nonce);
                    return Task.FromResult(CryptographicOperations.FixedTimeEquals(expected, actual));
                }
                catch (FormatException)
                {
                    return Task.FromResult(false);
                }
            }
        }

        return Task.FromResult(false);
    }

    private void CleanupExpired()
    {
        if (_challenges.Count > 1000)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var kvp in _challenges)
            {
                if (kvp.Value.ExpiresAt < now)
                {
                    _challenges.TryRemove(kvp.Key, out _);
                }
            }
        }
    }
}
