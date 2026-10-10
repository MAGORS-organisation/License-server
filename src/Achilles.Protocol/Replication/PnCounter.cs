using System.Text.Json.Serialization;

namespace Achilles.Protocol.Replication;

/// <summary>
/// State-based Positive-Negative Counter (PN-Counter) CRDT.
/// Guarantees eventual consistency, monotonicity, commutativity, and associativity across distributed regions.
/// </summary>
public sealed record PnCounter
{
    [JsonPropertyName("p")]
    public IReadOnlyDictionary<string, long> P { get; init; }

    [JsonPropertyName("n")]
    public IReadOnlyDictionary<string, long> N { get; init; }

    public static PnCounter Empty { get; } = new(
        new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase));

    [JsonIgnore]
    public long Value
    {
        get
        {
            long positive = 0;
            foreach (long val in P.Values)
            {
                positive += val;
            }

            long negative = 0;
            foreach (long val in N.Values)
            {
                negative += val;
            }

            return positive - negative;
        }
    }

    [JsonConstructor]
    public PnCounter(
        IReadOnlyDictionary<string, long>? p = null,
        IReadOnlyDictionary<string, long>? n = null)
    {
        P = p is not null
            ? new Dictionary<string, long>(p, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        N = n is not null
            ? new Dictionary<string, long>(n, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
    }

    public PnCounter Increment(string regionId, long amount = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(regionId);
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be positive.");
        }

        var newP = new Dictionary<string, long>(P, StringComparer.OrdinalIgnoreCase);
        long current = newP.TryGetValue(regionId, out long val) ? val : 0;
        newP[regionId] = current + amount;

        return new PnCounter(newP, N);
    }

    public PnCounter Decrement(string regionId, long amount = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(regionId);
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be positive.");
        }

        var newN = new Dictionary<string, long>(N, StringComparer.OrdinalIgnoreCase);
        long current = newN.TryGetValue(regionId, out long val) ? val : 0;
        newN[regionId] = current + amount;

        return new PnCounter(P, newN);
    }

    public PnCounter Merge(PnCounter other)
    {
        ArgumentNullException.ThrowIfNull(other);

        var mergedP = new Dictionary<string, long>(P, StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in other.P)
        {
            mergedP[k] = mergedP.TryGetValue(k, out long existing) ? Math.Max(existing, v) : v;
        }

        var mergedN = new Dictionary<string, long>(N, StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in other.N)
        {
            mergedN[k] = mergedN.TryGetValue(k, out long existing) ? Math.Max(existing, v) : v;
        }

        return new PnCounter(mergedP, mergedN);
    }
}
