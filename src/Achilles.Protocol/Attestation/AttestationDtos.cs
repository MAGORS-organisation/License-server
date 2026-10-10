using System.Text.Json.Serialization;
using Achilles.Format.Attestation;

namespace Achilles.Protocol.Attestation;

/// <summary>
/// Challenge request sent by client before submitting a hardware enclave quote.
/// </summary>
public sealed record AttestationChallengeRequest(
    [property: JsonPropertyName("clientId")] string ClientId,
    [property: JsonPropertyName("enclaveType")] EnclaveType EnclaveType);

/// <summary>
/// Server challenge containing a cryptographic anti-replay nonce.
/// </summary>
public sealed record AttestationChallengeResponse(
    [property: JsonPropertyName("challengeId")] string ChallengeId,
    [property: JsonPropertyName("nonce")] string Nonce,
    [property: JsonPropertyName("expiresAt")] DateTimeOffset ExpiresAt);

/// <summary>
/// Client submission carrying the signed hardware quote fulfilling the challenge.
/// </summary>
public sealed record AttestationSubmissionRequest(
    [property: JsonPropertyName("challengeId")] string ChallengeId,
    [property: JsonPropertyName("quote")] HardwareAttestationQuote Quote);

/// <summary>
/// Attestation verification outcome returned to caller.
/// </summary>
public sealed record AttestationVerificationResultDto(
    [property: JsonPropertyName("verified")] bool Verified,
    [property: JsonPropertyName("error")] string? Error,
    [property: JsonPropertyName("timestamp")] DateTimeOffset Timestamp);
