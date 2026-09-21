using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Symbolon.Protocol;

/// <summary>
/// Air-gapped offline grant request (.symreq) according to spec/05-seat-grant.md and docs/07-floating-protokol.md Section 7.7.
/// </summary>
public sealed record OfflineGrantRequestDto
{
    [Required]
    [JsonPropertyName("relayId")]
    public required string RelayId { get; init; }

    [Required]
    [JsonPropertyName("licenseKey")]
    public required string LicenseKey { get; init; }

    [Range(1, 1000)]
    [JsonPropertyName("requestedSeats")]
    public required int RequestedSeats { get; init; }

    [JsonPropertyName("lastSeq")]
    public long LastSeq { get; init; }

    /// <summary>
    /// Cryptographic hash-chain root of the local audit ledger since last grant.
    /// Essential anti-abuse guard: ensures air-gapped relay cannot request grants indefinitely without reporting usage.
    /// </summary>
    [Required]
    [JsonPropertyName("usageDigest")]
    public required string UsageDigest { get; init; }

    [JsonPropertyName("nonce")]
    public string? Nonce { get; init; }

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record OfflineGrantResponseDto
{
    [JsonPropertyName("grantId")]
    public required string GrantId { get; init; }

    [JsonPropertyName("licenseId")]
    public required string LicenseId { get; init; }

    [JsonPropertyName("relayId")]
    public required string RelayId { get; init; }

    [JsonPropertyName("seatFrom")]
    public required int SeatFrom { get; init; }

    [JsonPropertyName("seatTo")]
    public required int SeatTo { get; init; }

    [JsonPropertyName("seq")]
    public required long Seq { get; init; }

    [JsonPropertyName("expiresAt")]
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>
    /// The PEM-armored .symgrant string (-----BEGIN SYMBOLON SEAT GRANT----- ...).
    /// </summary>
    [JsonPropertyName("symgrantPem")]
    public required string SymgrantPem { get; init; }
}

public sealed record OfflineActivationRequestDto
{
    [Required]
    [JsonPropertyName("licenseKey")]
    public required string LicenseKey { get; init; }

    [Required]
    [JsonPropertyName("fingerprint")]
    public required string Fingerprint { get; init; }

    [JsonPropertyName("machineName")]
    public string? MachineName { get; init; }

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record OfflineActivationResponseDto
{
    [JsonPropertyName("activationId")]
    public required string ActivationId { get; init; }

    [JsonPropertyName("licenseId")]
    public required string LicenseId { get; init; }

    [JsonPropertyName("fingerprint")]
    public required string Fingerprint { get; init; }

    [JsonPropertyName("expiresAt")]
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>
    /// The PEM-armored .symlic license file (-----BEGIN SYMBOLON LICENSE KEY----- ...).
    /// </summary>
    [JsonPropertyName("symlicPem")]
    public required string SymlicPem { get; init; }
}
