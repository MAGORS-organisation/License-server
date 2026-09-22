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
[JsonSerializable(typeof(BorrowRequestDto))]
[JsonSerializable(typeof(BorrowResponseDto))]
[JsonSerializable(typeof(QueuedResponseDto))]
[JsonSerializable(typeof(ServerTelemetryDto))]
[JsonSerializable(typeof(Symbolon.Protocol.Tracing.TraceSpanDto))]
[JsonSerializable(typeof(List<Symbolon.Protocol.Tracing.TraceSpanDto>))]
public sealed partial class SymbolonProtocolJsonContext : JsonSerializerContext
{
}
