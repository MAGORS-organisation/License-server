using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Achilles.Data;
using Achilles.Data.Entities;
using Achilles.Protocol.Reporting;
using Xunit;

namespace Achilles.ControlPlane.Tests;

public sealed class TopEndpointsTests : IClassFixture<ControlPlaneFactory>
{
    private readonly ControlPlaneFactory _factory;
    private readonly HttpClient _client;

    public TopEndpointsTests(ControlPlaneFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetTopStats_ReturnsSystemTelemetryAndSparkline()
    {
        var res = await _client.GetAsync(new Uri("/admin/v1/system/top-stats", UriKind.Relative));
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var stats = await res.Content.ReadFromJsonAsync<TopSystemStatsDto>();
        stats.Should().NotBeNull();
        stats!.ThroughputSparkline.Should().NotBeNull();
        stats.ThroughputSparkline.Count.Should().Be(10);
    }

    [Fact]
    public async Task GetActiveLeases_AndRevoke_OperatesCorrectly()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AchillesDbContext>();

        string tenantId = $"tenant_top_{Guid.NewGuid():N}";
        db.Tenants.Add(new Tenant { Id = tenantId, Slug = tenantId, Name = "Top Tenant", CreatedAt = DateTimeOffset.UtcNow });

        string prodId = $"prod_top_{Guid.NewGuid():N}";
        db.Products.Add(new Product { Id = prodId, TenantId = tenantId, Code = "PROD-TOP", Name = "Top Product", CreatedAt = DateTimeOffset.UtcNow });

        string policyId = $"pol_top_{Guid.NewGuid():N}";
        db.Policies.Add(new Policy { Id = policyId, TenantId = tenantId, Code = "TOP-POL", Name = "Top Policy", ProductId = prodId });

        string licId = $"lic_top_{Guid.NewGuid():N}";
        var license = new LicenseEntity
        {
            Id = licId,
            TenantId = tenantId,
            PolicyId = policyId,
            State = "active",
            MaxSeats = 5,
            IssuedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Licenses.Add(license);

        string leaseId = $"lease_top_{Guid.NewGuid():N}";
        var seat = new SeatEntity
        {
            LicenseId = licId,
            SeatNo = 1,
            LeaseId = leaseId,
            HolderFp = [0xAA, 0xBB, 0xCC, 0xDD],
            MachineId = "host-tui-1",
            AcquiredAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(20)
        };
        db.Seats.Add(seat);
        await db.SaveChangesAsync();

        // 1. GET /admin/v1/leases/active
        var listRes = await _client.GetAsync(new Uri("/admin/v1/leases/active", UriKind.Relative));
        listRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var leases = await listRes.Content.ReadFromJsonAsync<List<ActiveLeaseItemDto>>();
        leases.Should().NotBeNull();
        leases!.Any(l => l.LeaseId == leaseId).Should().BeTrue();

        // 2. POST /admin/v1/leases/{leaseId}/revoke
        var revokeRes = await _client.PostAsync(new Uri($"/admin/v1/leases/{leaseId}/revoke", UriKind.Relative), null);
        revokeRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Verify seat is released
        using (var verifyScope = _factory.Services.CreateScope())
        {
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AchillesDbContext>();
            var updatedSeat = await verifyDb.Seats.FindAsync(seat.Id);
            updatedSeat.Should().NotBeNull();
            updatedSeat!.LeaseId.Should().BeNull();
            updatedSeat.ExpiresAt.Should().BeNull();
            updatedSeat.HolderFp.Should().BeNull();
        }
    }
}
