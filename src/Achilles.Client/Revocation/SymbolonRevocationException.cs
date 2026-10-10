using Achilles.Format;

namespace Achilles.Client.Revocation;

/// <summary>
/// Exception thrown when an active license, machine, signing key, or lease is rejected due to revocation.
/// </summary>
public class SymbolonRevocationException : Exception
{
    public string SubjectType { get; } = string.Empty;
    public string SubjectId { get; } = string.Empty;
    public string? Reason { get; }

    public SymbolonRevocationException()
        : base("Subjekt bol revokovaný na licenčnom serveri.")
    {
    }

    public SymbolonRevocationException(string message)
        : base(message)
    {
    }

    public SymbolonRevocationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public SymbolonRevocationException(string subjectType, string subjectId, string? reason)
        : base($"Subjekt '{subjectId}' ({subjectType}) bol revokovaný na licenčnom serveri. Dôvod: {reason ?? "nešpecifikovaný"}.")
    {
        SubjectType = subjectType;
        SubjectId = subjectId;
        Reason = reason;
    }

    public SymbolonRevocationException(RevocationItem item)
        : this(item?.T ?? "unknown", item?.Id ?? "unknown", item?.Reason)
    {
    }
}
