using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Achilles.Crypto;
using Achilles.Data;
using Achilles.Data.Entities;
using Achilles.Format;
using Achilles.Protocol;

namespace Achilles.ControlPlane.Security;

public sealed class KeyManager : IDisposable
{
    private readonly AchillesDbContext _db;
    private readonly TimeProvider _time;
    private static readonly ConcurrentDictionary<string, ISignatureProvider> ActiveProviders = new(StringComparer.Ordinal);
    private static readonly SemaphoreSlim Lock = new(1, 1);

    public KeyManager(AchillesDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    public async Task<ISignatureProvider> GetActiveSigningKeyAsync(CancellationToken ct = default)
    {
        await Lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var activeKeyEntity = await _db.SigningKeys
                .Where(k => k.State == "active" && k.Role == "product")
                .OrderByDescending(k => k.NotBefore)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);

            if (activeKeyEntity is null)
            {
                // Create initial default active key
                var now = _time.GetUtcNow();
                string kid = $"prd-default-{now:yyyyMMdd}-es256";
                var key = Es256SignatureProvider.GenerateKey(kid);
                var jwk = key.ExportPublicJwk();
                string jwkJson = JsonSerializer.Serialize(jwk, AchillesJsonContext.Default.JsonWebKeyDto);

                var tenant = await _db.Tenants.FirstOrDefaultAsync(ct).ConfigureAwait(false);
                if (tenant is null)
                {
                    tenant = new Tenant
                    {
                        Id = "ten_default",
                        Slug = "default",
                        Name = "Default Organization",
                        CreatedAt = now
                    };
                    _db.Tenants.Add(tenant);
                    await _db.SaveChangesAsync(ct).ConfigureAwait(false);
                }
                string tenantId = tenant.Id;

                var entity = new SigningKeyEntity
                {
                    Id = $"key_{Guid.NewGuid():N}",
                    TenantId = tenantId,
                    Kid = kid,
                    Alg = Alg.Es256,
                    Role = "product",
                    PublicJwkJson = jwkJson,
                    NotBefore = now,
                    NotAfter = now.AddYears(2),
                    State = "active"
                };

                _db.SigningKeys.Add(entity);
                await _db.SaveChangesAsync(ct).ConfigureAwait(false);

                ActiveProviders[kid] = key;
                return key;
            }

            // In-memory key provider matching active kid
            if (!ActiveProviders.TryGetValue(activeKeyEntity.Kid, out var provider))
            {
                provider = Es256SignatureProvider.GenerateKey(activeKeyEntity.Kid);
                ActiveProviders[activeKeyEntity.Kid] = provider;
                var jwk = provider.ExportPublicJwk();
                activeKeyEntity.PublicJwkJson = JsonSerializer.Serialize(jwk, AchillesJsonContext.Default.JsonWebKeyDto);
                await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            }

            return provider;
        }
        finally
        {
            Lock.Release();
        }
    }

    public async Task<(SigningKeyEntity Entity, JsonWebKeyDto Jwk)> RotateKeyAsync(
        string tenantId,
        string alg = Alg.Es256,
        CancellationToken ct = default)
    {
        await Lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var now = _time.GetUtcNow();

            // 1. Resolve actual tenant
            var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId || t.Slug == tenantId, ct).ConfigureAwait(false);
            if (tenant is null)
            {
                tenant = await _db.Tenants.FirstOrDefaultAsync(ct).ConfigureAwait(false);
                if (tenant is null)
                {
                    tenant = new Tenant
                    {
                        Id = "ten_default",
                        Slug = "default",
                        Name = "Default Organization",
                        CreatedAt = now
                    };
                    _db.Tenants.Add(tenant);
                    await _db.SaveChangesAsync(ct).ConfigureAwait(false);
                }
            }
            string actualTenantId = tenant.Id;

            // 2. Deprecate existing active keys for this tenant
            var existingActive = await _db.SigningKeys
                .Where(k => k.TenantId == actualTenantId && k.State == "active" && k.Role == "product")
                .ToListAsync(ct)
                .ConfigureAwait(false);

            foreach (var oldKey in existingActive)
            {
                oldKey.State = "deprecated";
            }

            // 3. Generate new key
            string kid = $"prd-{actualTenantId}-{now:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6]}-es256";
            var key = Es256SignatureProvider.GenerateKey(kid);
            var jwk = key.ExportPublicJwk();
            string jwkJson = JsonSerializer.Serialize(jwk, AchillesJsonContext.Default.JsonWebKeyDto);

            var newEntity = new SigningKeyEntity
            {
                Id = $"key_{Guid.NewGuid():N}",
                TenantId = actualTenantId,
                Kid = kid,
                Alg = alg,
                Role = "product",
                PublicJwkJson = jwkJson,
                NotBefore = now,
                NotAfter = now.AddYears(2),
                State = "active"
            };

            _db.SigningKeys.Add(newEntity);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

            ActiveProviders[kid] = key;

            return (newEntity, jwk);
        }
        finally
        {
            Lock.Release();
        }
    }

    public async Task<bool> RevokeKeyAsync(string kid, string reason, CancellationToken ct = default)
    {
        await Lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var keyEntity = await _db.SigningKeys
                .FirstOrDefaultAsync(k => k.Kid == kid, ct)
                .ConfigureAwait(false);

            if (keyEntity is null || keyEntity.State == "revoked")
            {
                return false;
            }

            keyEntity.State = "revoked";

            long nextSeq = (await _db.Revocations.MaxAsync(r => (long?)r.Sequence, ct).ConfigureAwait(false) ?? 0) + 1;
            var revocation = new RevocationEntity
            {
                Id = $"rev_{Guid.NewGuid():N}",
                TenantId = keyEntity.TenantId,
                SubjectType = "kid",
                SubjectId = kid,
                Reason = reason,
                RevokedAt = _time.GetUtcNow(),
                Sequence = nextSeq
            };

            _db.Revocations.Add(revocation);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

            if (ActiveProviders.TryRemove(kid, out var removed))
            {
                removed.Dispose();
            }

            return true;
        }
        finally
        {
            Lock.Release();
        }
    }

    public async Task<JsonWebKeySetDto> GetPublicJwksAsync(CancellationToken ct = default)
    {
        // Return all active and deprecated keys, excluding revoked keys (RFC 9964)
        var validKeys = await _db.SigningKeys
            .Where(k => k.State == "active" || k.State == "deprecated")
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var keysList = new List<JsonWebKeyDto>(validKeys.Count);
        foreach (var k in validKeys)
        {
            try
            {
                var jwk = JsonSerializer.Deserialize(k.PublicJwkJson, AchillesJsonContext.Default.JsonWebKeyDto);
                if (jwk is not null)
                {
                    keysList.Add(jwk);
                }
            }
            catch (JsonException)
            {
                // Ignore malformed individual JWK entries in test databases
            }
        }

        // If no keys in DB yet, ensure default active key is generated
        if (keysList.Count == 0)
        {
            var activeProvider = await GetActiveSigningKeyAsync(ct).ConfigureAwait(false);
            keysList.Add(activeProvider.ExportPublicJwk());
        }

        return new JsonWebKeySetDto { Keys = keysList };
    }

    public async Task<IReadOnlyList<SigningKeyEntity>> GetAllKeysAsync(CancellationToken ct = default)
    {
        return await _db.SigningKeys
            .OrderByDescending(k => k.NotBefore)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public void Dispose()
    {
        // KeyManager is a scoped service; process-wide key cache is managed across scopes.
    }
}
