using System.Text.Json.Serialization;

namespace Achilles.Protocol.Replication;

public enum VectorClockRelation
{
    Equal,
    Dominates,
    DominatedBy,
    Concurrent
}

/// <summary>
/// Immutable, serializable Vector Clock implementation for causal ordering across distributed geo-replicated regions.
/// Follows Lamport &amp; Fidge/Mattern vector clock semantics.
/// </summary>
public sealed record VectorClock
{
    [JsonPropertyName("versions")]
    public IReadOnlyDictionary<string, long> Versions { get; init; }

    public static VectorClock Empty { get; } = new(new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase));

    [JsonConstructor]
    public VectorClock(IReadOnlyDictionary<string, long>? versions = null)
    {
        if (versions is null || versions.Count == 0)
        {
            Versions = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            Versions = new Dictionary<string, long>(versions, StringComparer.OrdinalIgnoreCase);
        }
    }

    public long GetClock(string regionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(regionId);
        return Versions.TryGetValue(regionId, out long clock) ? clock : 0;
    }

    public VectorClock Increment(string regionId, long step = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(regionId);
        if (step <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(step), "Increment step must be positive.");
        }

        var copy = new Dictionary<string, long>(Versions, StringComparer.OrdinalIgnoreCase);
        long current = copy.TryGetValue(regionId, out long val) ? val : 0;
        copy[regionId] = current + step;

        return new VectorClock(copy);
    }

    public VectorClock Merge(VectorClock other)
    {
        ArgumentNullException.ThrowIfNull(other);

        var merged = new Dictionary<string, long>(Versions, StringComparer.OrdinalIgnoreCase);

        foreach (var (k, v) in other.Versions)
        {
            if (merged.TryGetValue(k, out long existing))
            {
                merged[k] = Math.Max(existing, v);
            }
            else
            {
                merged[k] = v;
            }
        }

        return new VectorClock(merged);
    }

    public VectorClockRelation CompareToClock(VectorClock other)
    {
        ArgumentNullException.ThrowIfNull(other);

        var allKeys = new HashSet<string>(Versions.Keys, StringComparer.OrdinalIgnoreCase);
        allKeys.UnionWith(other.Versions.Keys);

        bool greaterOrEqual = true;
        bool lessOrEqual = true;

        foreach (string key in allKeys)
        {
            long v1 = GetClock(key);
            long v2 = other.GetClock(key);

            if (v1 < v2)
            {
                greaterOrEqual = false;
            }

            if (v1 > v2)
            {
                lessOrEqual = false;
            }
        }

        if (greaterOrEqual && lessOrEqual)
        {
            return VectorClockRelation.Equal;
        }

        if (greaterOrEqual)
        {
            return VectorClockRelation.Dominates;
        }

        if (lessOrEqual)
        {
            return VectorClockRelation.DominatedBy;
        }

        return VectorClockRelation.Concurrent;
    }
}
