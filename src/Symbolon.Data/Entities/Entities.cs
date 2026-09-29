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
    public ICollection<WebhookSubscriptionEntity> WebhookSubscriptions { get; set; } = new List<WebhookSubscriptionEntity>();
    public ICollection<TokenWalletEntity> TokenWallets { get; set; } = new List<TokenWalletEntity>();
    public ICollection<TokenRateEntity> TokenRates { get; set; } = new List<TokenRateEntity>();
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
    public ICollection<TokenRateEntity> TokenRates { get; set; } = new List<TokenRateEntity>();
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
    public string? RulesYaml { get; set; }

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
    public string? RulesYaml { get; set; }

    public Tenant? Tenant { get; set; }
    public Policy? Policy { get; set; }
    public ICollection<SeatEntity> Seats { get; set; } = new List<SeatEntity>();
    public ICollection<MachineEntity> Machines { get; set; } = new List<MachineEntity>();
    public ICollection<SeatGrantEntity> SeatGrants { get; set; } = new List<SeatGrantEntity>();
    public ICollection<LicenseUserEntity> LicenseUsers { get; set; } = new List<LicenseUserEntity>();
    public ICollection<QueueTicketEntity> QueueTickets { get; set; } = new List<QueueTicketEntity>();
    public ICollection<LicenseQuotaEntity> Quotas { get; set; } = new List<LicenseQuotaEntity>();
    public ICollection<TokenWalletEntity> TokenWallets { get; set; } = new List<TokenWalletEntity>();
    public ICollection<LicenseEntitlementEntity> Entitlements { get; set; } = new List<LicenseEntitlementEntity>();
    public ICollection<ActiveFeatureLeaseEntity> ActiveFeatureLeases { get; set; } = new List<ActiveFeatureLeaseEntity>();
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
    public string? UserId { get; set; }

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
    public long Sequence { get; set; }

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

public sealed class WebhookSubscriptionEntity
{
    public string Id { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
#pragma warning disable CA1056 // URI-like properties should not be strings (EF Core column mapping)
    public string Url { get; set; } = string.Empty;
#pragma warning restore CA1056
    public string Secret { get; set; } = string.Empty;
    public string Format { get; set; } = "json"; // json, slack, teams
    public string EventsJson { get; set; } = "[\"*\"]";
    public bool IsActive { get; set; } = true;
    public int FailureCount { get; set; }
    public DateTimeOffset? LastDeliveredAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Tenant? Tenant { get; set; }
    public ICollection<WebhookDeliveryEntity> Deliveries { get; set; } = new List<WebhookDeliveryEntity>();
}

public sealed class WebhookDeliveryEntity
{
    public string Id { get; set; } = string.Empty;
    public string SubscriptionId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public string Status { get; set; } = "pending"; // pending, delivered, failed, dead_letter
    public int? StatusCode { get; set; }
    public long DurationMs { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public WebhookSubscriptionEntity? Subscription { get; set; }
}

public sealed class LicenseUserEntity
{
    public string Id { get; set; } = string.Empty;
    public string LicenseId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string? GroupName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public LicenseEntity? License { get; set; }
}

public sealed class QueueTicketEntity
{
    public string Ticket { get; set; } = string.Empty;
    public string LicenseId { get; set; } = string.Empty;
    public string Fingerprint { get; set; } = string.Empty;
    public string? MachineId { get; set; }
    public string? UserId { get; set; }
    public int Quantity { get; set; } = 1;
    public int Priority { get; set; }
    public string FeaturesJson { get; set; } = "[]";
    public string Status { get; set; } = "waiting"; // waiting, ready, cancelled, expired
    public string? PromotedLeaseId { get; set; }
    public string? PromotedToken { get; set; }
    public DateTimeOffset? PromotedExpiresAt { get; set; }
    public int? PromotedSeatNo { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public LicenseEntity? License { get; set; }
}

public sealed class LicenseQuotaEntity
{
    public string Id { get; set; } = string.Empty;
    public string LicenseId { get; set; } = string.Empty;
    public string EntitlementCode { get; set; } = string.Empty;
    public long TotalUnits { get; set; }
    public long ConsumedUnits { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public LicenseEntity? License { get; set; }
}

public sealed class ApiKeyEntity
{
    public string Id { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string KeyHash { get; set; } = string.Empty;
    public string Prefix { get; set; } = string.Empty;
    public string Role { get; set; } = "admin:super"; // admin:super, admin:tenant, auditor
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Tenant? Tenant { get; set; }
}

public sealed class ScimUserEntity
{
    public string Id { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string? ExternalId { get; set; }
    public string? GivenName { get; set; }
    public string? FamilyName { get; set; }
    public string? FormattedName { get; set; }
    public string? Email { get; set; }
    public bool Active { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Tenant? Tenant { get; set; }
    public ICollection<ScimGroupMemberEntity> GroupMemberships { get; set; } = new List<ScimGroupMemberEntity>();
}

public sealed class ScimGroupEntity
{
    public string Id { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? ExternalId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Tenant? Tenant { get; set; }
    public ICollection<ScimGroupMemberEntity> Members { get; set; } = new List<ScimGroupMemberEntity>();
}

public sealed class ScimGroupMemberEntity
{
    public string GroupId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public DateTimeOffset AddedAt { get; set; }

    public ScimGroupEntity? Group { get; set; }
    public ScimUserEntity? User { get; set; }
}

public sealed class SsoProviderEntity
{
    public string Id { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string ProviderType { get; set; } = "oidc"; // "oidc" or "saml"
    public string DisplayName { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string? ClientSecret { get; set; }
    public string? MetadataEndpoint { get; set; }
    public string? AuthorizationEndpoint { get; set; }
    public string? TokenEndpoint { get; set; }
    public string? UserInfoEndpoint { get; set; }
    public string? IdpCertificate { get; set; }
    public string RoleMappingJson { get; set; } = "{}";
    public string DefaultRole { get; set; } = "admin:tenant";
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Tenant? Tenant { get; set; }
}

public sealed class TokenWalletEntity
{
    public string Id { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string? LicenseId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal TotalCredits { get; set; }
    public decimal Balance { get; set; }
    public decimal ReservedCredits { get; set; }
    public decimal OverdraftLimit { get; set; }
    public string State { get; set; } = "active";
    public DateTimeOffset? ExpiresAt { get; set; }
    public decimal? ThresholdLowAlert { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastRefillAt { get; set; }

    public Tenant? Tenant { get; set; }
    public LicenseEntity? License { get; set; }
    public ICollection<TokenReservationEntity> Reservations { get; set; } = new List<TokenReservationEntity>();
    public ICollection<TokenLedgerEntryEntity> LedgerEntries { get; set; } = new List<TokenLedgerEntryEntity>();
}

public sealed class TokenRateEntity
{
    public string Id { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string? ProductId { get; set; }
    public string FeatureCode { get; set; } = string.Empty;
    public decimal RatePerMinute { get; set; }
    public decimal RatePerUnit { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Tenant? Tenant { get; set; }
    public Product? Product { get; set; }
}

public sealed class TokenReservationEntity
{
    public string Id { get; set; } = string.Empty;
    public string WalletId { get; set; } = string.Empty;
    public string FeatureCode { get; set; } = string.Empty;
    public decimal ReservedAmount { get; set; }
    public decimal ConsumedAmount { get; set; }
    public string? MachineId { get; set; }
    public string? ClientRef { get; set; }
    public string Status { get; set; } = "pending";
    public string? IdempotencyKey { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public TokenWalletEntity? Wallet { get; set; }
    public ICollection<TokenLedgerEntryEntity> LedgerEntries { get; set; } = new List<TokenLedgerEntryEntity>();
}

public sealed class TokenLedgerEntryEntity
{
    public long Id { get; set; }
    public string WalletId { get; set; } = string.Empty;
    public string? ReservationId { get; set; }
    public string TransactionType { get; set; } = "consume";
    public decimal Amount { get; set; }
    public decimal BalanceAfter { get; set; }
    public string? FeatureCode { get; set; }
    public string? IdempotencyKey { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string MetadataJson { get; set; } = "{}";

    public TokenWalletEntity? Wallet { get; set; }
    public TokenReservationEntity? Reservation { get; set; }
}

public sealed class FeatureDefinitionEntity
{
    public string Id { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string? ProductId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? MinVersion { get; set; }
    public string? MaxVersion { get; set; }
    public bool IsFloating { get; set; } = true;
    public int? DefaultMaxSeats { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Tenant? Tenant { get; set; }
    public Product? Product { get; set; }
}

public sealed class PackageSuiteEntity
{
    public string Id { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string? ProductId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string FeatureCodesJson { get; set; } = "[]";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Tenant? Tenant { get; set; }
    public Product? Product { get; set; }
}

public sealed class LicenseEntitlementEntity
{
    public string Id { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string LicenseId { get; set; } = string.Empty;
    public string FeatureCode { get; set; } = string.Empty;
    public int? MaxSeats { get; set; }
    public string? AllowedVersionRange { get; set; }
    public bool IsEnabled { get; set; } = true;
    public string? ParametersJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Tenant? Tenant { get; set; }
    public LicenseEntity? License { get; set; }
}

public sealed class ActiveFeatureLeaseEntity
{
    public string Id { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string LicenseId { get; set; } = string.Empty;
    public string LeaseId { get; set; } = string.Empty;
    public string FeatureCode { get; set; } = string.Empty;
    public string? AcquiredVersion { get; set; }
    public DateTimeOffset AcquiredAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }

    public Tenant? Tenant { get; set; }
    public LicenseEntity? License { get; set; }
}



