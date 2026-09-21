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
            b.Property(w => w.Url).HasMaxLength(1024).IsRequired();
            b.Property(w => w.Secret).HasMaxLength(256).IsRequired();
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
            b.HasIndex(q => new { q.LicenseId, q.Status, q.CreatedAt });
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
