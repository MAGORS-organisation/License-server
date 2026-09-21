namespace Symbolon.Data.Entities;

public sealed class Tenant
{
    public string Id { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? SigningKeySetId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<Product> Products { get; set; } = new List<Product>();
    public ICollection<RelayEntity> Relays { get; set; } = new List<RelayEntity>();
    public ICollection<SigningKeyEntity> SigningKeys { get; set; } = new List<SigningKeyEntity>();
    public ICollection<AuditEventEntity> AuditEvents { get; set; } = new List<AuditEventEntity>();
    public ICollection<RevocationEntity> Revocations { get; set; } = new List<RevocationEntity>();
}

public sealed class Product
{
    public string Id { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string PlatformsJson { get; set; } = "[]";
    public DateTimeOffset CreatedAt { get; set; }

    public Tenant? Tenant { get; set; }
    public ICollection<Policy> Policies { get; set; } = new List<Policy>();
    public ICollection<Entitlement> Entitlements { get; set; } = new List<Entitlement>();
}

public sealed class Entitlement
{
    public string Id { get; set; } = string.Empty;
    public string ProductId { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "boolean";
    public string? DefaultValue { get; set; }

    public Product? Product { get; set; }
}

public sealed class Policy
{
    public string Id { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string ProductId { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string LicenseModel { get; set; } = "floating";
    public string? Duration { get; set; } = "P1Y";
    public int MaxSeats { get; set; } = 1;
    public string SeatUnit { get; set; } = "machine";
    public string OverageStrategy { get; set; } = "no-overage";
    public string ExpirationStrategy { get; set; } = "restrict";
    public int LeaseTtlSeconds { get; set; } = 600;
    public int HeartbeatIntervalSeconds { get; set; } = 120;
    public int GraceTtlSeconds { get; set; } = 14400;
    public int ResurrectionWindowSeconds { get; set; } = 300;
    public bool BorrowEnabled { get; set; }
    public int BorrowMaxDurationDays { get; set; } = 7;
    public bool OfflineAllowed { get; set; } = true;
    public int OfflineFileTtlDays { get; set; } = 30;
    public string CryptoProfile { get; set; } = "hybrid-v1";
    public string EntitlementsJson { get; set; } = "[]";

    public Tenant? Tenant { get; set; }
    public Product? Product { get; set; }
    public ICollection<LicenseEntity> Licenses { get; set; } = new List<LicenseEntity>();
}

public sealed class LicenseEntity
{
    public string Id { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string PolicyId { get; set; } = string.Empty;
    public byte[] KeyLookup { get; set; } = [];
    public string KeyHash { get; set; } = string.Empty;
    public string? CustomerRef { get; set; }
    public string State { get; set; } = "active";
    public int MaxSeats { get; set; } = 1;
    public int OverageSeats { get; set; }
    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Tenant? Tenant { get; set; }
    public Policy? Policy { get; set; }
    public ICollection<SeatEntity> Seats { get; set; } = new List<SeatEntity>();
    public ICollection<MachineEntity> Machines { get; set; } = new List<MachineEntity>();
    public ICollection<SeatGrantEntity> SeatGrants { get; set; } = new List<SeatGrantEntity>();
}

public sealed class SeatEntity
{
    public long Id { get; set; }
    public string LicenseId { get; set; } = string.Empty;
    public int SeatNo { get; set; }
    public bool IsOverage { get; set; }
    public string? GrantId { get; set; }
    public string? LeaseId { get; set; }
    public byte[]? HolderFp { get; set; }
    public string? MachineId { get; set; }
    public DateTimeOffset? AcquiredAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? BorrowedUntil { get; set; }
    public long LeaseSeq { get; set; }
    public string? ReservedFor { get; set; }

    public LicenseEntity? License { get; set; }
    public SeatGrantEntity? SeatGrant { get; set; }
}

public sealed class SeatGrantEntity
{
    public string Id { get; set; } = string.Empty;
    public string LicenseId { get; set; } = string.Empty;
    public string RelayId { get; set; } = string.Empty;
    public int Seats { get; set; }
    public int SeatFrom { get; set; }
    public int SeatTo { get; set; }
    public long Seq { get; set; }
    public DateTimeOffset NotBefore { get; set; }
    public DateTimeOffset NotAfter { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string Document { get; set; } = string.Empty;

    public LicenseEntity? License { get; set; }
    public RelayEntity? Relay { get; set; }
    public ICollection<SeatEntity> DelegatedSeats { get; set; } = new List<SeatEntity>();
}

public sealed class MachineEntity
{
    public string Id { get; set; } = string.Empty;
    public string LicenseId { get; set; } = string.Empty;
    public string Fingerprint { get; set; } = string.Empty;
    public string ComponentsJson { get; set; } = "{}";
    public DateTimeOffset FirstSeen { get; set; }
    public DateTimeOffset LastHeartbeat { get; set; }
    public string State { get; set; } = "active";

    public LicenseEntity? License { get; set; }
}

public sealed class RelayEntity
{
    public string Id { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? MtlsThumbprint { get; set; }
    public string? ApiKey { get; set; }
    public DateTimeOffset? LastSync { get; set; }
    public string? Version { get; set; }

    public Tenant? Tenant { get; set; }
    public ICollection<SeatGrantEntity> SeatGrants { get; set; } = new List<SeatGrantEntity>();
}

public sealed class AuditEventEntity
{
    public string Id { get; set; } = string.Empty;
    public string? TenantId { get; set; }
    public DateTimeOffset TsServer { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? LicenseId { get; set; }
    public string? Subject { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public byte[]? PrevHash { get; set; }
    public byte[] Hash { get; set; } = [];

    public Tenant? Tenant { get; set; }
}

public sealed class RevocationEntity
{
    public string Id { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string SubjectType { get; set; } = string.Empty;
    public string SubjectId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset RevokedAt { get; set; }

    public Tenant? Tenant { get; set; }
}

public sealed class SigningKeyEntity
{
    public string Id { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string Kid { get; set; } = string.Empty;
    public string Alg { get; set; } = "ES256";
    public string Role { get; set; } = "product";
    public string PublicJwkJson { get; set; } = "{}";
    public string? EncryptedPrivateKey { get; set; }
    public DateTimeOffset NotBefore { get; set; }
    public DateTimeOffset NotAfter { get; set; }
    public string State { get; set; } = "active";

    public Tenant? Tenant { get; set; }
}
