using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Symbolon.Data.Entities;
using Symbolon.Domain;

namespace Symbolon.Data.Stores;

public sealed class EfAuditLedger(SymbolonDbContext db) : IAuditLedger
{
    private static readonly SemaphoreSlim Lock = new(1, 1);

    public async Task AppendAsync(AuditEvent auditEvent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);

        await Lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Find the last audit event to get prev_hash
            var lastEvent = await db.AuditEvents
                .OrderByDescending(a => a.TsServer)
                .ThenByDescending(a => a.Id)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);

            byte[]? prevHash = lastEvent?.Hash;

            string id = $"aud_{Guid.NewGuid():N}";
            string payload = JsonSerializer.Serialize(new
            {
                auditEvent.LeaseId,
                auditEvent.Fingerprint,
                auditEvent.Detail
            });

            var canonicalTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(auditEvent.Timestamp.ToUnixTimeMilliseconds());

            // Hash = SHA256(prevHash || id || type || licenseId || payload || timestamp)
            var inputBytes = Encoding.UTF8.GetBytes($"{Convert.ToHexString(prevHash ?? [])}:{id}:{auditEvent.Type}:{auditEvent.LicenseId}:{auditEvent.Fingerprint}:{payload}:{canonicalTimestamp:O}");
            byte[] hash = SHA256.HashData(inputBytes);

            // Find tenantId from license if possible, or from seat by leaseId, or first tenant
            string? tenantId = null;
            if (!string.IsNullOrEmpty(auditEvent.LicenseId) && auditEvent.LicenseId != "unknown")
            {
                tenantId = await db.Licenses
                    .Where(l => l.Id == auditEvent.LicenseId)
                    .Select(l => l.TenantId)
                    .FirstOrDefaultAsync(ct)
                    .ConfigureAwait(false);
            }

            if (string.IsNullOrEmpty(tenantId) && !string.IsNullOrEmpty(auditEvent.LeaseId))
            {
                tenantId = await db.Seats
                    .Where(s => s.LeaseId == auditEvent.LeaseId)
                    .Select(s => s.License != null ? s.License.TenantId : null)
                    .FirstOrDefaultAsync(ct)
                    .ConfigureAwait(false);
            }

            if (string.IsNullOrEmpty(tenantId))
            {
                tenantId = await db.Tenants.Select(t => t.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            }

            var entity = new AuditEventEntity
            {
                Id = id,
                TenantId = tenantId,
                TsServer = canonicalTimestamp,
                Type = auditEvent.Type,
                LicenseId = auditEvent.LicenseId,
                Subject = auditEvent.Fingerprint,
                PayloadJson = payload,
                PrevHash = prevHash,
                Hash = hash
            };

            db.AuditEvents.Add(entity);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            Lock.Release();
        }
    }
}
