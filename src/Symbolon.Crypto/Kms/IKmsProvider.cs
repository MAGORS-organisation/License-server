using System.Text.Json.Serialization;

namespace Symbolon.Crypto.Kms;

public enum KmsProviderType
{
    LocalEnvelope,
    AzureKeyVault,
    AwsKms,
    GcpKms,
    Pkcs11Hsm
}

public sealed record KmsKeyMetadata(
    [property: JsonPropertyName("keyId")] string KeyId,
    [property: JsonPropertyName("providerType")] KmsProviderType ProviderType,
    [property: JsonPropertyName("algorithm")] string Algorithm,
    [property: JsonPropertyName("keyLocation")] string KeyLocation,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("expiresAt")] DateTimeOffset? ExpiresAt,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("publicKey")] JsonWebKeyDto PublicKey);

public sealed record KmsHealthStatus(
    [property: JsonPropertyName("isHealthy")] bool IsHealthy,
    [property: JsonPropertyName("providerType")] KmsProviderType ProviderType,
    [property: JsonPropertyName("details")] string Details,
    [property: JsonPropertyName("timestamp")] DateTimeOffset Timestamp);

/// <summary>
/// Enterprise Cloud KMS & Hardware Security Module (HSM) provider abstraction.
/// Enables hardware-isolated asymmetric signing operations for NIST P-256 (ES256)
/// and post-quantum ML-DSA-65 in accordance with §9.3 normative security requirements.
/// </summary>
public interface IKmsProvider : IDisposable
{
    /// <summary>Gets the provider type identifier.</summary>
    KmsProviderType ProviderType { get; }

    /// <summary>Retrieves a signature provider for the specified key ID.</summary>
    Task<ISignatureProvider> GetSignatureProviderAsync(string keyId, CancellationToken ct = default);

    /// <summary>Retrieves metadata for the specified key ID.</summary>
    Task<KmsKeyMetadata> GetKeyMetadataAsync(string keyId, CancellationToken ct = default);

    /// <summary>Lists all managed asymmetric keys in this KMS / HSM partition.</summary>
    Task<IReadOnlyList<KmsKeyMetadata>> ListKeysAsync(CancellationToken ct = default);

    /// <summary>Performs a health check and verifies hardware isolation connectivity.</summary>
    Task<KmsHealthStatus> CheckHealthAsync(CancellationToken ct = default);
}
