using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Achilles.Format.Wasm;

namespace Achilles.Format.Oci;

public sealed record OciDescriptor(
    [property: JsonPropertyName("mediaType")] string MediaType,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("digest")] string Digest,
    [property: JsonPropertyName("annotations")] IReadOnlyDictionary<string, string>? Annotations = null);

public sealed record OciManifest(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("mediaType")] string MediaType,
    [property: JsonPropertyName("config")] OciDescriptor Config,
    [property: JsonPropertyName("layers")] IReadOnlyList<OciDescriptor> Layers);

public sealed record OciBundleVerificationResult(
    bool IsValid,
    string? FailureReason,
    string? LicensePayload,
    string? JwksPayload,
    WasmValidationResult? LicenseDetails);

/// <summary>
/// Packages and validates OCI Cryptographic License Bundles conforming to Phase 3.0 specification.
/// Enables container registries and Kubernetes air-gapped clusters to pull verified license artifacts.
/// </summary>
public static class OciLicenseBundle
{
    public const string OciManifestMediaType = "application/vnd.oci.image.manifest.v1+json";
    public const string SymbolonConfigMediaType = "application/vnd.symbolon.config.v1+json";
    public const string AchillesLicenseMediaType = "application/vnd.symbolon.license.v1+jwt";
    public const string SymbolonJwksMediaType = "application/vnd.symbolon.jwks.v1+json";

    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

#pragma warning disable CA1308 // OCI spec requires lowercase sha256 digests
    public static byte[] CreateBundle(string licensePemOrJws, string jwksJson, string customer, string product)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(licensePemOrJws);
        ArgumentException.ThrowIfNullOrWhiteSpace(jwksJson);

        byte[] licenseBytes = Encoding.UTF8.GetBytes(licensePemOrJws.Trim());
        byte[] jwksBytes = Encoding.UTF8.GetBytes(jwksJson.Trim());

        var configObj = new
        {
            schema = "symbolon:oci:v1",
            customer,
            product,
            createdAt = DateTimeOffset.UtcNow
        };
        byte[] configBytes = JsonSerializer.SerializeToUtf8Bytes(configObj);

        string configDigest = $"sha256:{Convert.ToHexString(SHA256.HashData(configBytes)).ToLowerInvariant()}";
        string licenseDigest = $"sha256:{Convert.ToHexString(SHA256.HashData(licenseBytes)).ToLowerInvariant()}";
        string jwksDigest = $"sha256:{Convert.ToHexString(SHA256.HashData(jwksBytes)).ToLowerInvariant()}";

        var manifest = new OciManifest(
            SchemaVersion: 2,
            MediaType: OciManifestMediaType,
            Config: new OciDescriptor(SymbolonConfigMediaType, configBytes.Length, configDigest),
            Layers:
            [
                new OciDescriptor(AchillesLicenseMediaType, licenseBytes.Length, licenseDigest, new Dictionary<string, string> { { "org.opencontainers.image.title", "license.symlic" } }),
                new OciDescriptor(SymbolonJwksMediaType, jwksBytes.Length, jwksDigest, new Dictionary<string, string> { { "org.opencontainers.image.title", "keys.jwks" } })
            ]);

        byte[] manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, IndentedJson);

        // Package into a TAR archive in memory
        using var memoryStream = new MemoryStream();
        using (var tarWriter = new TarWriter(memoryStream, TarEntryFormat.Pax, leaveOpen: true))
        {
            // 1. oci-layout
            var layoutEntry = new PaxTarEntry(TarEntryType.RegularFile, "oci-layout");
            byte[] layoutBytes = Encoding.UTF8.GetBytes("{\"imageLayoutVersion\":\"1.0.0\"}\n");
            layoutEntry.DataStream = new MemoryStream(layoutBytes);
            tarWriter.WriteEntry(layoutEntry);

            // 2. manifest.json
            var manifestEntry = new PaxTarEntry(TarEntryType.RegularFile, "manifest.json");
            manifestEntry.DataStream = new MemoryStream(manifestBytes);
            tarWriter.WriteEntry(manifestEntry);

            // 3. blobs/sha256/<config>
            var configEntry = new PaxTarEntry(TarEntryType.RegularFile, $"blobs/sha256/{configDigest[7..]}");
            configEntry.DataStream = new MemoryStream(configBytes);
            tarWriter.WriteEntry(configEntry);

            // 4. blobs/sha256/<license>
            var licenseEntry = new PaxTarEntry(TarEntryType.RegularFile, $"blobs/sha256/{licenseDigest[7..]}");
            licenseEntry.DataStream = new MemoryStream(licenseBytes);
            tarWriter.WriteEntry(licenseEntry);

            // 5. blobs/sha256/<jwks>
            var jwksEntry = new PaxTarEntry(TarEntryType.RegularFile, $"blobs/sha256/{jwksDigest[7..]}");
            jwksEntry.DataStream = new MemoryStream(jwksBytes);
            tarWriter.WriteEntry(jwksEntry);
        }

        return memoryStream.ToArray();
    }

#pragma warning disable CA1031 // Resilient parsing of OCI tar entry formats
    public static OciBundleVerificationResult VerifyBundle(byte[] tarBytes, string? expectedFingerprint = null)
    {
        ArgumentNullException.ThrowIfNull(tarBytes);

        try
        {
            using var memoryStream = new MemoryStream(tarBytes);
            using var tarReader = new TarReader(memoryStream);

            var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            TarEntry? entry;
            while ((entry = tarReader.GetNextEntry()) is not null)
            {
                if (entry.DataStream is not null)
                {
                    using var entryMs = new MemoryStream();
                    entry.DataStream.CopyTo(entryMs);
                    files[entry.Name] = entryMs.ToArray();
                }
            }

            if (!files.TryGetValue("manifest.json", out var manifestBytes))
            {
                return new OciBundleVerificationResult(false, "Missing manifest.json in OCI bundle.", null, null, null);
            }

            var manifest = JsonSerializer.Deserialize<OciManifest>(manifestBytes);
            if (manifest is null || manifest.Layers.Count < 2)
            {
                return new OciBundleVerificationResult(false, "Invalid or incomplete OCI manifest layers.", null, null, null);
            }

            // Extract license and jwks layers
            var licenseDesc = manifest.Layers.FirstOrDefault(l => l.MediaType == AchillesLicenseMediaType);
            var jwksDesc = manifest.Layers.FirstOrDefault(l => l.MediaType == SymbolonJwksMediaType);

            if (licenseDesc is null || jwksDesc is null)
            {
                return new OciBundleVerificationResult(false, "Required Symbolon license and JWKS layers not found in manifest.", null, null, null);
            }

            string licenseBlobPath = $"blobs/sha256/{licenseDesc.Digest[7..]}";
            string jwksBlobPath = $"blobs/sha256/{jwksDesc.Digest[7..]}";

            if (!files.TryGetValue(licenseBlobPath, out var licenseBytes))
            {
                return new OciBundleVerificationResult(false, $"License blob {licenseBlobPath} missing from archive.", null, null, null);
            }

            if (!files.TryGetValue(jwksBlobPath, out var jwksBytes))
            {
                return new OciBundleVerificationResult(false, $"JWKS blob {jwksBlobPath} missing from archive.", null, null, null);
            }

            // Digest integrity check
            string actualLicDigest = $"sha256:{Convert.ToHexString(SHA256.HashData(licenseBytes)).ToLowerInvariant()}";
            if (!string.Equals(actualLicDigest, licenseDesc.Digest, StringComparison.OrdinalIgnoreCase))
            {
                return new OciBundleVerificationResult(false, "License blob digest integrity check failed.", null, null, null);
            }

            string actualJwksDigest = $"sha256:{Convert.ToHexString(SHA256.HashData(jwksBytes)).ToLowerInvariant()}";
            if (!string.Equals(actualJwksDigest, jwksDesc.Digest, StringComparison.OrdinalIgnoreCase))
            {
                return new OciBundleVerificationResult(false, "JWKS blob digest integrity check failed.", null, null, null);
            }

            string licenseString = Encoding.UTF8.GetString(licenseBytes);
            string jwksString = Encoding.UTF8.GetString(jwksBytes);

            // Verify license cryptographically
            var validation = WasmLicenseValidator.Validate(licenseString, jwksString, fingerprint: expectedFingerprint);

            return new OciBundleVerificationResult(
                validation.IsValid,
                validation.FailureReason,
                licenseString,
                jwksString,
                validation);
        }
        catch (Exception ex)
        {
            return new OciBundleVerificationResult(false, $"Failed to unpack OCI bundle: {ex.Message}", null, null, null);
        }
    }
#pragma warning restore CA1031
#pragma warning restore CA1308
}
