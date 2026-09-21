using System.Text.Json.Serialization;
using Symbolon.Crypto;

namespace Symbolon.Format;

/// <summary>
/// Source-generated JsonSerializerContext for Symbolon formats to support Native AOT and high throughput.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(LicenseClaims))]
[JsonSerializable(typeof(SymlicClaims))]
[JsonSerializable(typeof(LicenseMetadata))]
[JsonSerializable(typeof(CustomerMetadata))]
[JsonSerializable(typeof(LicenseLimits))]
[JsonSerializable(typeof(EntitlementClaim))]
[JsonSerializable(typeof(BindingClaim))]
[JsonSerializable(typeof(PolicyClaim))]
[JsonSerializable(typeof(LeasePolicyClaim))]
[JsonSerializable(typeof(RevocationPolicyClaim))]
[JsonSerializable(typeof(JwsGeneralJson))]
[JsonSerializable(typeof(JwsSignature))]
[JsonSerializable(typeof(JwsProtectedHeader))]
[JsonSerializable(typeof(JsonWebKeyDto))]
[JsonSerializable(typeof(JsonWebKeySetDto))]
public sealed partial class SymbolonJsonContext : JsonSerializerContext
{
}
