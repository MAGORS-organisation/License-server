using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using Achilles.Protocol.Reporting;

namespace Achilles.Domain.Reporting;

[SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Cryptographic hash buffer matching EF Core byte array entity mapping.")]
public sealed record AuditRecordItem(
    string Id,
    DateTimeOffset TsServer,
    string Type,
    string? LicenseId,
    string? Subject,
    string PayloadJson,
    byte[]? PrevHash,
    byte[] Hash
);

/// <summary>
/// Cryptographic verifier for immutable audit ledger hash chains.
/// Conforms to spec/07-floating-protokol.md FLT-37 and docs/06-architektura.md.
/// </summary>
public static class AuditChainIntegrityVerifier
{
    public static AuditVerificationProofDto Verify(IReadOnlyList<AuditRecordItem> events, DateTimeOffset now)
    {
        if (events == null || events.Count == 0)
        {
            return new AuditVerificationProofDto(
                TotalEventsVerified: 0,
                FirstEventId: null,
                LastEventId: null,
                FirstEventTimestamp: null,
                LastEventTimestamp: null,
                RootHashHex: string.Empty,
                IsChainIntact: true,
                TamperedEventId: null,
                TamperReason: null,
                VerifiedAtUtc: now
            );
        }

        for (int i = 0; i < events.Count; i++)
        {
            var item = events[i];

            // 1. Verify link to previous event
            if (i > 0)
            {
                byte[] previousHash = events[i - 1].Hash;
                if (!CryptographicOperations.FixedTimeEquals(item.PrevHash ?? [], previousHash))
                {
                    return new AuditVerificationProofDto(
                        TotalEventsVerified: i,
                        FirstEventId: events[0].Id,
                        LastEventId: item.Id,
                        FirstEventTimestamp: events[0].TsServer,
                        LastEventTimestamp: item.TsServer,
                        RootHashHex: Convert.ToHexString(events[i - 1].Hash),
                        IsChainIntact: false,
                        TamperedEventId: item.Id,
                        TamperReason: $"Chain linkage broken: PrevHash does not match Hash of previous event '{events[i - 1].Id}'.",
                        VerifiedAtUtc: now
                    );
                }
            }

            // 2. Recompute item hash and verify payload integrity
            byte[] computedHash = ComputeHash(
                item.PrevHash,
                item.Id,
                item.Type,
                item.LicenseId,
                item.Subject,
                item.PayloadJson,
                item.TsServer
            );

            if (!CryptographicOperations.FixedTimeEquals(item.Hash, computedHash))
            {
                string raw = FormatRaw(item.PrevHash, item.Id, item.Type, item.LicenseId, item.Subject, item.PayloadJson, item.TsServer);
                return new AuditVerificationProofDto(
                    TotalEventsVerified: i,
                    FirstEventId: events[0].Id,
                    LastEventId: item.Id,
                    FirstEventTimestamp: events[0].TsServer,
                    LastEventTimestamp: item.TsServer,
                    RootHashHex: i > 0 ? Convert.ToHexString(events[i - 1].Hash) : string.Empty,
                    IsChainIntact: false,
                    TamperedEventId: item.Id,
                    TamperReason: $"Cryptographic digest mismatch: Data for audit event '{item.Id}' was modified or tampered with.",
                    VerifiedAtUtc: now
                );
            }
        }

        string rootHashHex = Convert.ToHexString(events[^1].Hash);

        return new AuditVerificationProofDto(
            TotalEventsVerified: events.Count,
            FirstEventId: events[0].Id,
            LastEventId: events[^1].Id,
            FirstEventTimestamp: events[0].TsServer,
            LastEventTimestamp: events[^1].TsServer,
            RootHashHex: rootHashHex,
            IsChainIntact: true,
            TamperedEventId: null,
            TamperReason: null,
            VerifiedAtUtc: now
        );
    }

    public static string FormatRaw(
        byte[]? prevHash,
        string id,
        string type,
        string? licenseId,
        string? subject,
        string payloadJson,
        DateTimeOffset timestamp)
    {
        var canonicalTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(timestamp.ToUnixTimeMilliseconds());
        return $"{Convert.ToHexString(prevHash ?? [])}:{id}:{type}:{licenseId}:{subject}:{payloadJson}:{canonicalTimestamp:O}";
    }

    public static byte[] ComputeHash(
        byte[]? prevHash,
        string id,
        string type,
        string? licenseId,
        string? subject,
        string payloadJson,
        DateTimeOffset timestamp)
    {
        string raw = FormatRaw(prevHash, id, type, licenseId, subject, payloadJson, timestamp);
        return SHA256.HashData(Encoding.UTF8.GetBytes(raw));
    }
}
