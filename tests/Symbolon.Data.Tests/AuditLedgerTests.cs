using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Symbolon.Data.Entities;
using Symbolon.Data.Stores;
using Symbolon.Domain;
using Xunit;

namespace Symbolon.Data.Tests;

public sealed class AuditLedgerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<SymbolonDbContext> _options;

    public AuditLedgerTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<SymbolonDbContext>()
            .UseSqlite(_connection)
            .Options;
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    [Fact]
    public async Task AuditLedger_Appends_And_Chains_Hashes()
    {
        await using var db = new SymbolonDbContext(_options);
        await db.Database.EnsureCreatedAsync();

        db.Tenants.Add(new Tenant { Id = "default", Slug = "default", Name = "Default", CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var ledger = new EfAuditLedger(db);
        var now = DateTimeOffset.UtcNow;

        var ev1 = new AuditEvent("checkout", "lic_1", "lse_1", "sha256:fp1", now, "Checkout OK");
        var ev2 = new AuditEvent("renew", "lic_1", "lse_1", "sha256:fp1", now.AddMinutes(2), "Renew OK");

        await ledger.AppendAsync(ev1);
        await ledger.AppendAsync(ev2);

        var events = await db.AuditEvents.OrderBy(a => a.TsServer).ToListAsync();
        Assert.Equal(2, events.Count);

        // First event has null or empty prev_hash
        Assert.Null(events[0].PrevHash);
        Assert.NotEmpty(events[0].Hash);

        // Second event's prev_hash must match first event's hash
        Assert.NotNull(events[1].PrevHash);
        Assert.Equal(events[0].Hash, events[1].PrevHash);
    }
}
