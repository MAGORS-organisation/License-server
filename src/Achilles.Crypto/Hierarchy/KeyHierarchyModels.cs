using System.Text;
using System.Text.Json.Serialization;

namespace Achilles.Crypto.Hierarchy;

public enum KeyTierRole
{
    None = 0,
    Root = 1,
    Product = 2,
    Lease = 3
}

/// <summary>
/// Cryptographic delegation certificate issued by a parent key authority to a subordinate key.
/// Implements §9.3 3-tier key hierarchy (Root ➜ Product ➜ Lease).
/// </summary>
public sealed record SignedKeyCertificate(
    [property: JsonPropertyName("subjectKid")] string SubjectKid,
    [property: JsonPropertyName("subjectRole")] KeyTierRole SubjectRole,
    [property: JsonPropertyName("issuerKid")] string IssuerKid,
    [property: JsonPropertyName("issuerRole")] KeyTierRole IssuerRole,
    [property: JsonPropertyName("publicKey")] JsonWebKeyDto PublicKey,
    [property: JsonPropertyName("validFrom")] DateTimeOffset ValidFrom,
    [property: JsonPropertyName("validUntil")] DateTimeOffset ValidUntil,
    [property: JsonPropertyName("signature")] string Signature)
{
    /// <summary>
    /// Computes deterministic canonical byte sequence for signing and verification.
    /// </summary>
    public byte[] GetSigningInput()
    {
        string canonical = $"{SubjectKid}|{SubjectRole}|{IssuerKid}|{IssuerRole}|" +
                           $"{PublicKey.Kty}|{PublicKey.Alg}|{PublicKey.Kid}|" +
                           $"{PublicKey.X ?? PublicKey.Pub ?? ""}|{PublicKey.Y ?? ""}|" +
                           $"{ValidFrom.ToUnixTimeSeconds()}|{ValidUntil.ToUnixTimeSeconds()}";

        return Encoding.UTF8.GetBytes(canonical);
    }
}

/// <summary>
/// Complete 3-tier trust chain linking a Root trust anchor to a Product authority and active Lease key.
/// </summary>
public sealed record KeyHierarchyChain(
    [property: JsonPropertyName("rootCertificate")] SignedKeyCertificate RootCertificate,
    [property: JsonPropertyName("productCertificate")] SignedKeyCertificate ProductCertificate,
    [property: JsonPropertyName("leaseCertificate")] SignedKeyCertificate? LeaseCertificate = null);

public sealed record KeyChainVerificationResult(
    [property: JsonPropertyName("isValid")] bool IsValid,
    [property: JsonPropertyName("failureReason")] string? FailureReason,
    [property: JsonPropertyName("rootKid")] string? RootKid,
    [property: JsonPropertyName("productKid")] string? ProductKid,
    [property: JsonPropertyName("leaseKid")] string? LeaseKid,
    [property: JsonPropertyName("checkedAt")] DateTimeOffset CheckedAt);
