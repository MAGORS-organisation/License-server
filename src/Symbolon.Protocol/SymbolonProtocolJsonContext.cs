using System.Text.Json.Serialization;

namespace Symbolon.Protocol;

[JsonSourceGenerationOptions(
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(LeaseClaims))]
[JsonSerializable(typeof(LeaseProtectedHeader))]
[JsonSerializable(typeof(SeatGrantDocumentClaims))]
[JsonSerializable(typeof(SeatGrantPayload))]
[JsonSerializable(typeof(OfflineExtensionClaim))]
[JsonSerializable(typeof(CheckoutRequestDto))]
[JsonSerializable(typeof(CheckoutResponseDto))]
[JsonSerializable(typeof(RenewRequestDto))]
[JsonSerializable(typeof(RenewResponseDto))]
[JsonSerializable(typeof(ReleaseRequestDto))]
[JsonSerializable(typeof(ReleaseResponseDto))]
[JsonSerializable(typeof(QueuedResponseDto))]
[JsonSerializable(typeof(ServerTelemetryDto))]
public sealed partial class SymbolonProtocolJsonContext : JsonSerializerContext
{
}
