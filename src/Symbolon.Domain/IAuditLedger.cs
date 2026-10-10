using System.Text.Json;
using Symbolon.Domain.Reporting;
using Symbolon.Domain.Transparency;
using Symbolon.Protocol;
using Symbolon.Protocol.Reporting;

namespace Symbolon.Domain;

public sealed record AuditEvent(
    string Type,
    string LicenseId,
    string? LeaseId,
    string? Fingerprint,
    DateTimeOffset Timestamp,
    string? Detail,
    string? TenantId = null);

/// <summary>
/// Append-only audit ledger for license events.
/// </summary>
public interface IAuditLedger
{
    Task AppendAsync(AuditEvent auditEvent, CancellationToken ct = default);
    Task<AuditVerificationProofDto> VerifyChainAsync(string? tenantId = null, CancellationToken ct = default);
    Task<TransparencyInclusionResponseDto?> GenerateProofForEventAsync(string eventId, string? tenantId = null, CancellationToken ct = default);
}

/// <summary>
/// Simple in-memory or no-op audit ledger implementation.
/// </summary>
public sealed class InMemoryAuditLedger : IAuditLedger
{
    private readonly List<AuditRecordItem> _records = [];
    private readonly List<AuditEvent> _events = [];
    private readonly object _lock = new();

    public IReadOnlyList<AuditEvent> Events
    {
        get
        {
            lock (_lock)
            {
                return [.. _events];
            }
        }
    }

    public Task AppendAsync(AuditEvent auditEvent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        lock (_lock)
        {
            _events.Add(auditEvent);
            string id = $"aud_{_records.Count + 1}";
            byte[]? prevHash = _records.Count > 0 ? _records[^1].Hash : null;
            string payload = JsonSerializer.Serialize(new
            {
                auditEvent.LeaseId,
                auditEvent.Fingerprint,
                auditEvent.Detail
            });
            var canonicalTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(auditEvent.Timestamp.ToUnixTimeMilliseconds());
            byte[] hash = AuditChainIntegrityVerifier.ComputeHash(
                prevHash,
                id,
                auditEvent.Type,
                auditEvent.LicenseId,
                auditEvent.Fingerprint,
                payload,
                canonicalTimestamp);

            _records.Add(new AuditRecordItem(id, canonicalTimestamp, auditEvent.Type, auditEvent.LicenseId, auditEvent.Fingerprint, payload, prevHash, hash));
        }
        return Task.CompletedTask;
    }

    public Task<AuditVerificationProofDto> VerifyChainAsync(string? tenantId = null, CancellationToken ct = default)
    {
        lock (_lock)
        {
            return Task.FromResult(AuditChainIntegrityVerifier.Verify(_records, DateTimeOffset.UtcNow));
        }
    }

    public Task<TransparencyInclusionResponseDto?> GenerateProofForEventAsync(string eventId, string? tenantId = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(eventId);
        lock (_lock)
        {
            int index = _records.FindIndex(r => r.Id == eventId);
            if (index < 0)
            {
                return Task.FromResult<TransparencyInclusionResponseDto?>(null);
            }

            var leafHashes = _records.Select(r => MerkleTree.HashLeaf(r.Hash)).ToList();
            byte[] root = MerkleTree.ComputeRootHash(leafHashes);
            var proof = MerkleTree.GenerateInclusionProof(leafHashes, index);
            var steps = proof.Select(p => new TransparencyProofStepDto(p.Hash, p.Direction)).ToList();

            return Task.FromResult<TransparencyInclusionResponseDto?>(new TransparencyInclusionResponseDto(
                eventId,
                index,
                leafHashes.Count,
                Convert.ToHexStringLower(leafHashes[index]),
                Convert.ToHexStringLower(root),
                steps));
        }
    }
}

