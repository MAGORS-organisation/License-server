using Microsoft.EntityFrameworkCore;
using Symbolon.Data.Entities;

namespace Symbolon.Data;

public class SymbolonDbContext : DbContext
{
    public SymbolonDbContext(DbContextOptions<SymbolonDbContext> options)
        : base(options)
    {
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Entitlement> Entitlements => Set<Entitlement>();
    public DbSet<Policy> Policies => Set<Policy>();
    public DbSet<LicenseEntity> Licenses => Set<LicenseEntity>();
    public DbSet<SeatEntity> Seats => Set<SeatEntity>();
    public DbSet<SeatGrantEntity> SeatGrants => Set<SeatGrantEntity>();
    public DbSet<AirGapNonceEntity> AirGapNonces => Set<AirGapNonceEntity>();
    public DbSet<MachineEntity> Machines => Set<MachineEntity>();
    public DbSet<RelayEntity> Relays => Set<RelayEntity>();
    public DbSet<AuditEventEntity> AuditEvents => Set<AuditEventEntity>();
    public DbSet<RevocationEntity> Revocations => Set<RevocationEntity>();
    public DbSet<SigningKeyEntity> SigningKeys => Set<SigningKeyEntity>();
    public DbSet<WebhookSubscriptionEntity> WebhookSubscriptions => Set<WebhookSubscriptionEntity>();
    public DbSet<WebhookDeliveryEntity> WebhookDeliveries => Set<WebhookDeliveryEntity>();
    public DbSet<LicenseUserEntity> LicenseUsers => Set<LicenseUserEntity>();
    public DbSet<QueueTicketEntity> QueueTickets => Set<QueueTicketEntity>();
    public DbSet<LicenseQuotaEntity> LicenseQuotas => Set<LicenseQuotaEntity>();
    public DbSet<ApiKeyEntity> ApiKeys => Set<ApiKeyEntity>();
    public DbSet<ScimUserEntity> ScimUsers => Set<ScimUserEntity>();
    public DbSet<ScimGroupEntity> ScimGroups => Set<ScimGroupEntity>();
    public DbSet<ScimGroupMemberEntity> ScimGroupMembers => Set<ScimGroupMemberEntity>();
    public DbSet<SsoProviderEntity> SsoProviders => Set<SsoProviderEntity>();
    public DbSet<TokenWalletEntity> TokenWallets => Set<TokenWalletEntity>();
    public DbSet<TokenRateEntity> TokenRates => Set<TokenRateEntity>();
    public DbSet<TokenReservationEntity> TokenReservations => Set<TokenReservationEntity>();
    public DbSet<TokenLedgerEntryEntity> TokenLedgerEntries => Set<TokenLedgerEntryEntity>();
    public DbSet<FeatureDefinitionEntity> FeatureDefinitions => Set<FeatureDefinitionEntity>();
    public DbSet<PackageSuiteEntity> PackageSuites => Set<PackageSuiteEntity>();
    public DbSet<LicenseEntitlementEntity> LicenseEntitlements => Set<LicenseEntitlementEntity>();
    public DbSet<ActiveFeatureLeaseEntity> ActiveFeatureLeases => Set<ActiveFeatureLeaseEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);

        // Tenant
        modelBuilder.Entity<Tenant>(b =>
        {
            b.ToTable("tenants");
            b.HasKey(t => t.Id);
            b.Property(t => t.Slug).HasMaxLength(64).IsRequired();
            b.Property(t => t.Name).HasMaxLength(256).IsRequired();
            b.HasIndex(t => t.Slug).IsUnique();
        });

        // Product
        modelBuilder.Entity<Product>(b =>
        {
            b.ToTable("products");
            b.HasKey(p => p.Id);
            b.Property(p => p.Code).HasMaxLength(64).IsRequired();
            b.Property(p => p.Name).HasMaxLength(256).IsRequired();
            b.HasIndex(p => new { p.TenantId, p.Code }).IsUnique();
            b.HasOne(p => p.Tenant)
             .WithMany(t => t.Products)
             .HasForeignKey(p => p.TenantId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // Entitlement
        modelBuilder.Entity<Entitlement>(b =>
        {
            b.ToTable("entitlements");
            b.HasKey(e => e.Id);
            b.Property(e => e.Code).HasMaxLength(128).IsRequired();
            b.Property(e => e.Name).HasMaxLength(256).IsRequired();
            b.HasIndex(e => new { e.ProductId, e.Code }).IsUnique();
            b.HasOne(e => e.Product)
             .WithMany(p => p.Entitlements)
             .HasForeignKey(e => e.ProductId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // Policy
        modelBuilder.Entity<Policy>(b =>
        {
            b.ToTable("policies");
            b.HasKey(p => p.Id);
            b.Property(p => p.Code).HasMaxLength(64).IsRequired();
            b.Property(p => p.Name).HasMaxLength(256).IsRequired();
            b.HasIndex(p => new { p.TenantId, p.Code }).IsUnique();
            b.HasOne(p => p.Tenant)
             .WithMany()
             .HasForeignKey(p => p.TenantId)
             .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(p => p.Product)
             .WithMany(pr => pr.Policies)
             .HasForeignKey(p => p.ProductId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // License
        modelBuilder.Entity<LicenseEntity>(b =>
        {
            b.ToTable("licenses");
            b.HasKey(l => l.Id);
            b.Property(l => l.KeyLookup).IsRequired();
            b.Property(l => l.KeyHash).IsRequired();
            b.Property(l => l.State).HasMaxLength(32).IsRequired();
            b.HasIndex(l => new { l.TenantId, l.KeyLookup }).IsUnique();
            b.HasOne(l => l.Tenant)
             .WithMany()
             .HasForeignKey(l => l.TenantId)
             .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(l => l.Policy)
             .WithMany(p => p.Licenses)
             .HasForeignKey(l => l.PolicyId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // Seat
        modelBuilder.Entity<SeatEntity>(b =>
        {
            b.ToTable("seats");
            b.HasKey(s => s.Id);
            b.HasIndex(s => new { s.LicenseId, s.SeatNo }).IsUnique();
            b.HasIndex(s => s.ExpiresAt);
            b.HasIndex(s => s.LeaseId);
            b.Property(s => s.LeaseId).IsConcurrencyToken();
            b.Property(s => s.UserId).HasMaxLength(128);
            b.Property(s => s.PossessionKey);
            b.HasIndex(s => s.BorrowedUntil);
            b.HasOne(s => s.License)
             .WithMany(l => l.Seats)
             .HasForeignKey(s => s.LicenseId)
             .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(s => s.SeatGrant)
             .WithMany(g => g.DelegatedSeats)
             .HasForeignKey(s => s.GrantId)
             .OnDelete(DeleteBehavior.SetNull);
        });

        // SeatGrant
        modelBuilder.Entity<SeatGrantEntity>(b =>
        {
            b.ToTable("seat_grants");
            b.HasKey(g => g.Id);
            b.HasIndex(g => new { g.LicenseId, g.RelayId, g.Seq }).IsUnique();
            b.HasOne(g => g.License)
             .WithMany(l => l.SeatGrants)
             .HasForeignKey(g => g.LicenseId)
             .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(g => g.Relay)
             .WithMany(r => r.SeatGrants)
             .HasForeignKey(g => g.RelayId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // AirGapNonce (FLT-34)
        modelBuilder.Entity<AirGapNonceEntity>(b =>
        {
            b.ToTable("air_gap_nonces");
            b.HasKey(n => n.Nonce);
            b.HasIndex(n => n.ExpiresAt);
        });

        // Machine
        modelBuilder.Entity<MachineEntity>(b =>
        {
            b.ToTable("machines");
            b.HasKey(m => m.Id);
            b.Property(m => m.Fingerprint).IsRequired();
            b.HasIndex(m => new { m.LicenseId, m.Fingerprint }).IsUnique();
            b.HasOne(m => m.License)
             .WithMany(l => l.Machines)
             .HasForeignKey(m => m.LicenseId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // Relay
        modelBuilder.Entity<RelayEntity>(b =>
        {
            b.ToTable("relays");
            b.HasKey(r => r.Id);
            b.Property(r => r.Name).HasMaxLength(256).IsRequired();
            b.HasIndex(r => r.ApiKey).IsUnique();
            b.HasOne(r => r.Tenant)
             .WithMany(t => t.Relays)
             .HasForeignKey(r => r.TenantId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // AuditEvent
        modelBuilder.Entity<AuditEventEntity>(b =>
        {
            b.ToTable("audit_events");
            b.HasKey(a => a.Id);
            b.Property(a => a.Type).HasMaxLength(64).IsRequired();
            b.HasIndex(a => new { a.LicenseId, a.TsServer });
            b.HasOne(a => a.Tenant)
             .WithMany(t => t.AuditEvents)
             .HasForeignKey(a => a.TenantId)
             .IsRequired(false)
             .OnDelete(DeleteBehavior.SetNull);
        });

        // Revocation
        modelBuilder.Entity<RevocationEntity>(b =>
        {
            b.ToTable("revocations");
            b.HasKey(r => r.Id);
            b.Property(r => r.SubjectType).HasMaxLength(32).IsRequired();
            b.Property(r => r.SubjectId).HasMaxLength(128).IsRequired();
            b.HasIndex(r => new { r.TenantId, r.SubjectType, r.SubjectId });
            b.HasIndex(r => new { r.TenantId, r.Sequence });
            b.HasOne(r => r.Tenant)
             .WithMany(t => t.Revocations)
             .HasForeignKey(r => r.TenantId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // SigningKey
        modelBuilder.Entity<SigningKeyEntity>(b =>
        {
            b.ToTable("signing_keys");
            b.HasKey(k => k.Id);
            b.Property(k => k.Kid).HasMaxLength(128).IsRequired();
            b.HasIndex(k => new { k.TenantId, k.Kid }).IsUnique();
            b.HasOne(k => k.Tenant)
             .WithMany(t => t.SigningKeys)
             .HasForeignKey(k => k.TenantId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // WebhookSubscription
        modelBuilder.Entity<WebhookSubscriptionEntity>(b =>
        {
            b.ToTable("webhook_subscriptions");
            b.HasKey(w => w.Id);
            b.Property(w => w.Name).HasMaxLength(128).IsRequired();
            b.Property(w => w.Url).HasMaxLength(1024).IsRequired();
            b.Property(w => w.Secret).HasMaxLength(256).IsRequired();
            b.Property(w => w.Format).HasMaxLength(32).IsRequired();
            b.HasIndex(w => new { w.TenantId, w.IsActive });
            b.HasOne(w => w.Tenant)
             .WithMany(t => t.WebhookSubscriptions)
             .HasForeignKey(w => w.TenantId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // WebhookDelivery
        modelBuilder.Entity<WebhookDeliveryEntity>(b =>
        {
            b.ToTable("webhook_deliveries");
            b.HasKey(d => d.Id);
            b.Property(d => d.EventType).HasMaxLength(128).IsRequired();
            b.Property(d => d.Status).HasMaxLength(32).IsRequired();
            b.HasIndex(d => new { d.SubscriptionId, d.CreatedAt });
            b.HasIndex(d => new { d.Status, d.NextAttemptAt });
            b.HasIndex(d => d.Status);
            b.HasOne(d => d.Subscription)
             .WithMany(s => s.Deliveries)
             .HasForeignKey(d => d.SubscriptionId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // LicenseUser
        modelBuilder.Entity<LicenseUserEntity>(b =>
        {
            b.ToTable("license_users");
            b.HasKey(u => u.Id);
            b.Property(u => u.UserId).HasMaxLength(256).IsRequired();
            b.Property(u => u.GroupName).HasMaxLength(128);
            b.HasIndex(u => new { u.LicenseId, u.UserId }).IsUnique();
            b.HasOne(u => u.License)
             .WithMany(l => l.LicenseUsers)
             .HasForeignKey(u => u.LicenseId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // QueueTicket
        modelBuilder.Entity<QueueTicketEntity>(b =>
        {
            b.ToTable("queue_tickets");
            b.HasKey(q => q.Ticket);
            b.Property(q => q.Fingerprint).HasMaxLength(128).IsRequired();
            b.Property(q => q.Status).HasMaxLength(32).IsRequired();
            b.HasIndex(q => new { q.LicenseId, q.Status, q.Priority, q.CreatedAt });
            b.HasOne(q => q.License)
             .WithMany(l => l.QueueTickets)
             .HasForeignKey(q => q.LicenseId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // LicenseQuota
        modelBuilder.Entity<LicenseQuotaEntity>(b =>
        {
            b.ToTable("license_quotas");
            b.HasKey(q => q.Id);
            b.Property(q => q.EntitlementCode).HasMaxLength(128).IsRequired();
            b.HasIndex(q => new { q.LicenseId, q.EntitlementCode }).IsUnique();
            b.HasOne(q => q.License)
             .WithMany(l => l.Quotas)
             .HasForeignKey(q => q.LicenseId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ApiKey
        modelBuilder.Entity<ApiKeyEntity>(b =>
        {
            b.ToTable("api_keys");
            b.HasKey(k => k.Id);
            b.Property(k => k.Name).HasMaxLength(256).IsRequired();
            b.Property(k => k.Prefix).HasMaxLength(32).IsRequired();
            b.Property(k => k.KeyHash).IsRequired();
            b.Property(k => k.Role).HasMaxLength(64).IsRequired();
            b.HasIndex(k => new { k.TenantId, k.Prefix });
            b.HasOne(k => k.Tenant)
             .WithMany()
             .HasForeignKey(k => k.TenantId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // SCIM 2.0 User
        modelBuilder.Entity<ScimUserEntity>(b =>
        {
            b.ToTable("scim_users");
            b.HasKey(u => u.Id);
            b.Property(u => u.UserName).HasMaxLength(256).IsRequired();
            b.Property(u => u.ExternalId).HasMaxLength(256);
            b.Property(u => u.Email).HasMaxLength(256);
            b.HasIndex(u => new { u.TenantId, u.UserName }).IsUnique();
            b.HasIndex(u => new { u.TenantId, u.ExternalId });
            b.HasOne(u => u.Tenant)
             .WithMany()
             .HasForeignKey(u => u.TenantId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // SCIM 2.0 Group
        modelBuilder.Entity<ScimGroupEntity>(b =>
        {
            b.ToTable("scim_groups");
            b.HasKey(g => g.Id);
            b.Property(g => g.DisplayName).HasMaxLength(256).IsRequired();
            b.Property(g => g.ExternalId).HasMaxLength(256);
            b.HasIndex(g => new { g.TenantId, g.DisplayName });
            b.HasOne(g => g.Tenant)
             .WithMany()
             .HasForeignKey(g => g.TenantId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // SCIM 2.0 Group Member
        modelBuilder.Entity<ScimGroupMemberEntity>(b =>
        {
            b.ToTable("scim_group_members");
            b.HasKey(m => new { m.GroupId, m.UserId });
            b.HasOne(m => m.Group)
             .WithMany(g => g.Members)
             .HasForeignKey(m => m.GroupId)
             .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(m => m.User)
             .WithMany(u => u.GroupMemberships)
             .HasForeignKey(m => m.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // SSO Provider
        modelBuilder.Entity<SsoProviderEntity>(b =>
        {
            b.ToTable("sso_providers");
            b.HasKey(p => p.Id);
            b.Property(p => p.DisplayName).HasMaxLength(256).IsRequired();
            b.Property(p => p.Issuer).HasMaxLength(512).IsRequired();
            b.Property(p => p.ClientId).HasMaxLength(256).IsRequired();
            b.Property(p => p.ProviderType).HasMaxLength(32).IsRequired();
            b.HasIndex(p => new { p.TenantId, p.ProviderType });
            b.HasOne(p => p.Tenant)
             .WithMany()
             .HasForeignKey(p => p.TenantId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // Token Wallet
        modelBuilder.Entity<TokenWalletEntity>(b =>
        {
            b.ToTable("token_wallets");
            b.HasKey(w => w.Id);
            b.Property(w => w.Code).HasMaxLength(64).IsRequired();
            b.Property(w => w.Name).HasMaxLength(256).IsRequired();
            b.Property(w => w.State).HasMaxLength(32).IsRequired();
            b.Property(w => w.TotalCredits).HasPrecision(18, 4);
            b.Property(w => w.Balance).HasPrecision(18, 4);
            b.Property(w => w.ReservedCredits).HasPrecision(18, 4);
            b.Property(w => w.OverdraftLimit).HasPrecision(18, 4);
            b.HasIndex(w => new { w.TenantId, w.Code }).IsUnique();
            b.HasOne(w => w.Tenant)
             .WithMany(t => t.TokenWallets)
             .HasForeignKey(w => w.TenantId)
             .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(w => w.License)
             .WithMany(l => l.TokenWallets)
             .HasForeignKey(w => w.LicenseId)
             .OnDelete(DeleteBehavior.SetNull);
        });

        // Token Rate
        modelBuilder.Entity<TokenRateEntity>(b =>
        {
            b.ToTable("token_rates");
            b.HasKey(r => r.Id);
            b.Property(r => r.FeatureCode).HasMaxLength(128).IsRequired();
            b.Property(r => r.RatePerMinute).HasPrecision(18, 4);
            b.Property(r => r.RatePerUnit).HasPrecision(18, 4);
            b.Property(r => r.Description).HasMaxLength(256);
            b.HasIndex(r => new { r.TenantId, r.ProductId, r.FeatureCode }).IsUnique();
            b.HasOne(r => r.Tenant)
             .WithMany(t => t.TokenRates)
             .HasForeignKey(r => r.TenantId)
             .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(r => r.Product)
             .WithMany(p => p.TokenRates)
             .HasForeignKey(r => r.ProductId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // Token Reservation
        modelBuilder.Entity<TokenReservationEntity>(b =>
        {
            b.ToTable("token_reservations");
            b.HasKey(r => r.Id);
            b.Property(r => r.FeatureCode).HasMaxLength(128).IsRequired();
            b.Property(r => r.ReservedAmount).HasPrecision(18, 4);
            b.Property(r => r.ConsumedAmount).HasPrecision(18, 4);
            b.Property(r => r.Status).HasMaxLength(32).IsRequired();
            b.Property(r => r.IdempotencyKey).HasMaxLength(128);
            b.HasIndex(r => r.WalletId);
            b.HasIndex(r => r.IdempotencyKey);
            b.HasOne(r => r.Wallet)
             .WithMany(w => w.Reservations)
             .HasForeignKey(r => r.WalletId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // Token Ledger Entry
        modelBuilder.Entity<TokenLedgerEntryEntity>(b =>
        {
            b.ToTable("token_ledger_entries");
            b.HasKey(e => e.Id);
            b.Property(e => e.TransactionType).HasMaxLength(32).IsRequired();
            b.Property(e => e.Amount).HasPrecision(18, 4);
            b.Property(e => e.BalanceAfter).HasPrecision(18, 4);
            b.Property(e => e.FeatureCode).HasMaxLength(128);
            b.Property(e => e.IdempotencyKey).HasMaxLength(128);
            b.HasIndex(e => e.WalletId);
            b.HasIndex(e => e.IdempotencyKey);
            b.HasOne(e => e.Wallet)
             .WithMany(w => w.LedgerEntries)
             .HasForeignKey(e => e.WalletId)
             .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(e => e.Reservation)
             .WithMany(r => r.LedgerEntries)
             .HasForeignKey(e => e.ReservationId)
             .OnDelete(DeleteBehavior.SetNull);
        });

        // FeatureDefinition
        modelBuilder.Entity<FeatureDefinitionEntity>(b =>
        {
            b.ToTable("feature_definitions");
            b.HasKey(f => f.Id);
            b.Property(f => f.TenantId).HasMaxLength(64).IsRequired();
            b.Property(f => f.ProductId).HasMaxLength(64);
            b.Property(f => f.Code).HasMaxLength(64).IsRequired();
            b.Property(f => f.Name).HasMaxLength(128).IsRequired();
            b.Property(f => f.Description).HasMaxLength(512);
            b.Property(f => f.MinVersion).HasMaxLength(32);
            b.Property(f => f.MaxVersion).HasMaxLength(32);
            b.HasIndex(f => new { f.TenantId, f.Code }).IsUnique();
            b.HasOne(f => f.Tenant).WithMany().HasForeignKey(f => f.TenantId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(f => f.Product).WithMany().HasForeignKey(f => f.ProductId).OnDelete(DeleteBehavior.SetNull);
        });

        // PackageSuite
        modelBuilder.Entity<PackageSuiteEntity>(b =>
        {
            b.ToTable("package_suites");
            b.HasKey(s => s.Id);
            b.Property(s => s.TenantId).HasMaxLength(64).IsRequired();
            b.Property(s => s.ProductId).HasMaxLength(64);
            b.Property(s => s.Code).HasMaxLength(64).IsRequired();
            b.Property(s => s.Name).HasMaxLength(128).IsRequired();
            b.Property(s => s.Description).HasMaxLength(512);
            b.Property(s => s.FeatureCodesJson).HasMaxLength(4000).IsRequired();
            b.HasIndex(s => new { s.TenantId, s.Code }).IsUnique();
            b.HasOne(s => s.Tenant).WithMany().HasForeignKey(s => s.TenantId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(s => s.Product).WithMany().HasForeignKey(s => s.ProductId).OnDelete(DeleteBehavior.SetNull);
        });

        // LicenseEntitlement
        modelBuilder.Entity<LicenseEntitlementEntity>(b =>
        {
            b.ToTable("license_entitlements");
            b.HasKey(e => e.Id);
            b.Property(e => e.TenantId).HasMaxLength(64).IsRequired();
            b.Property(e => e.LicenseId).HasMaxLength(64).IsRequired();
            b.Property(e => e.FeatureCode).HasMaxLength(64).IsRequired();
            b.Property(e => e.AllowedVersionRange).HasMaxLength(128);
            b.Property(e => e.ParametersJson).HasMaxLength(4000);
            b.HasIndex(e => new { e.LicenseId, e.FeatureCode }).IsUnique();
            b.HasIndex(e => new { e.TenantId, e.FeatureCode });
            b.HasOne(e => e.Tenant).WithMany().HasForeignKey(e => e.TenantId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(e => e.License).WithMany(l => l.Entitlements).HasForeignKey(e => e.LicenseId).OnDelete(DeleteBehavior.Cascade);
        });

        // ActiveFeatureLease
        modelBuilder.Entity<ActiveFeatureLeaseEntity>(b =>
        {
            b.ToTable("active_feature_leases");
            b.HasKey(a => a.Id);
            b.Property(a => a.TenantId).HasMaxLength(64).IsRequired();
            b.Property(a => a.LicenseId).HasMaxLength(64).IsRequired();
            b.Property(a => a.LeaseId).HasMaxLength(128).IsRequired();
            b.Property(a => a.FeatureCode).HasMaxLength(64).IsRequired();
            b.Property(a => a.AcquiredVersion).HasMaxLength(32);
            b.HasIndex(a => new { a.LeaseId, a.FeatureCode }).IsUnique();
            b.HasIndex(a => new { a.LicenseId, a.FeatureCode });
            b.HasIndex(a => a.ExpiresAt);
            b.HasOne(a => a.Tenant).WithMany().HasForeignKey(a => a.TenantId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(a => a.License).WithMany(l => l.ActiveFeatureLeases).HasForeignKey(a => a.LicenseId).OnDelete(DeleteBehavior.Cascade);
        });

        if (Database.IsSqlite())
        {
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                var properties = entityType.ClrType.GetProperties()
                    .Where(p => p.PropertyType == typeof(DateTimeOffset) || p.PropertyType == typeof(DateTimeOffset?));
                foreach (var property in properties)
                {
                    modelBuilder.Entity(entityType.ClrType)
                        .Property(property.Name)
                        .HasConversion<Microsoft.EntityFrameworkCore.Storage.ValueConversion.DateTimeOffsetToBinaryConverter>();
                }
            }
        }
    }
}
