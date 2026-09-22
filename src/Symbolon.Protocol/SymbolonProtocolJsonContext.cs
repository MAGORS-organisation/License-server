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
[JsonSerializable(typeof(Symbolon.Protocol.Scim.ScimUserDto))]
[JsonSerializable(typeof(List<Symbolon.Protocol.Scim.ScimUserDto>))]
[JsonSerializable(typeof(Symbolon.Protocol.Scim.ScimListResponseDto<Symbolon.Protocol.Scim.ScimUserDto>))]
[JsonSerializable(typeof(Symbolon.Protocol.Scim.ScimGroupDto))]
[JsonSerializable(typeof(List<Symbolon.Protocol.Scim.ScimGroupDto>))]
[JsonSerializable(typeof(Symbolon.Protocol.Scim.ScimListResponseDto<Symbolon.Protocol.Scim.ScimGroupDto>))]
[JsonSerializable(typeof(Symbolon.Protocol.Scim.ScimServiceProviderConfigDto))]
[JsonSerializable(typeof(Symbolon.Protocol.Scim.ScimPatchOpDto))]
[JsonSerializable(typeof(Symbolon.Protocol.Scim.ScimPatchOperation))]
[JsonSerializable(typeof(Symbolon.Protocol.Scim.ScimErrorDto))]
[JsonSerializable(typeof(Symbolon.Protocol.Scim.ScimUserNameDto))]
[JsonSerializable(typeof(Symbolon.Protocol.Scim.ScimEmailDto))]
[JsonSerializable(typeof(Symbolon.Protocol.Scim.ScimMemberDto))]
[JsonSerializable(typeof(Symbolon.Protocol.Scim.ScimMetaDto))]
[JsonSerializable(typeof(Symbolon.Protocol.Sso.SsoProviderSummaryDto))]
[JsonSerializable(typeof(List<Symbolon.Protocol.Sso.SsoProviderSummaryDto>))]
[JsonSerializable(typeof(Symbolon.Protocol.Sso.SsoProviderConfigDto))]
[JsonSerializable(typeof(List<Symbolon.Protocol.Sso.SsoProviderConfigDto>))]
[JsonSerializable(typeof(Symbolon.Protocol.Sso.SsoUserProfileDto))]
[JsonSerializable(typeof(Symbolon.Protocol.Sso.SsoLoginInitiateResponseDto))]
[JsonSerializable(typeof(Symbolon.Protocol.Sso.SsoSessionClaims))]
public sealed partial class SymbolonProtocolJsonContext : JsonSerializerContext
{
}
