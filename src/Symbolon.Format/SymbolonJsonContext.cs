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
[JsonSerializable(typeof(RevocationListClaims))]
[JsonSerializable(typeof(RevocationPayload))]
[JsonSerializable(typeof(RevocationItem))]
[JsonSerializable(typeof(List<RevocationItem>))]
[JsonSerializable(typeof(IReadOnlyList<RevocationItem>))]
[JsonSerializable(typeof(SymleaseClaims))]
[JsonSerializable(typeof(SymleaseBorrowPayload))]
[JsonSerializable(typeof(SeatGrantDocumentClaims))]
[JsonSerializable(typeof(SeatGrantPayload))]
[JsonSerializable(typeof(OfflineExtensionClaim))]
[JsonSerializable(typeof(AirGapRequestClaims))]
[JsonSerializable(typeof(AirGapRequestPayload))]
public sealed partial class SymbolonJsonContext : JsonSerializerContext
{
}
