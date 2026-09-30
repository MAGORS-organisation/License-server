using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Symbolon.Data.Entities;
using Symbolon.Data.Stores;
using Symbolon.Domain.Experiments;
using Xunit;

namespace Symbolon.Data.Tests;

public sealed class EfExperimentStoreTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<SymbolonDbContext> _options;

    public EfExperimentStoreTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<SymbolonDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var db = new SymbolonDbContext(_options);
        db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    [Fact]
    public async Task SaveAsync_And_GetByIdAsync_ShouldPersistAndRetrieveExperiment()
    {
        using var db = new SymbolonDbContext(_options);
        var store = new EfExperimentStore(db);

        var experiment = new Experiment
        {
            Id = "exp_ttl_test",
            Name = "TTL 300s vs 60s",
            Description = "A/B test lease duration",
            Status = ExperimentStatus.Active,
            TrafficAllocation = 50,
            Targeting = new ExperimentTargeting
            {
                TenantIds = ["ten_01"],
                LicensePrefixes = ["SYM-PRO-"],
                SdkLanguages = ["dotnet", "python"],
                OsPlatforms = ["windows", "linux"]
            },
            Variants =
            [
                new ExperimentVariant
                {
                    VariantId = "ctrl",
                    Name = "Control 300s",
                    Weight = 50,
                    IsControl = true,
                    Overrides = new ExperimentOverrides { LeaseTtlSeconds = 300 }
                },
                new ExperimentVariant
                {
                    VariantId = "treat",
                    Name = "Treatment 60s",
                    Weight = 50,
                    IsControl = false,
                    Overrides = new ExperimentOverrides { LeaseTtlSeconds = 60 }
                }
            ],
            CircuitBreaker = new ExperimentCircuitBreaker
            {
                MaxErrorRate = 0.05,
                MinSamplesThreshold = 100,
                AutoRollback = true
            }
        };

        await store.SaveAsync(experiment);

        var retrieved = await store.GetByIdAsync("exp_ttl_test");

        retrieved.Should().NotBeNull();
        retrieved!.Id.Should().Be("exp_ttl_test");
        retrieved.Name.Should().Be("TTL 300s vs 60s");
        retrieved.Status.Should().Be(ExperimentStatus.Active);
        retrieved.TrafficAllocation.Should().Be(50);
        retrieved.Targeting.TenantIds.Should().Contain("ten_01");
        retrieved.Variants.Should().HaveCount(2);
        retrieved.Variants[0].Overrides.LeaseTtlSeconds.Should().Be(300);
        retrieved.Variants[1].Overrides.LeaseTtlSeconds.Should().Be(60);
        retrieved.CircuitBreaker.MaxErrorRate.Should().Be(0.05);
    }

    [Fact]
    public async Task GetAllAsync_And_GetActiveAsync_ShouldFilterByStatusAndTenant()
    {
        using var db = new SymbolonDbContext(_options);
        var store = new EfExperimentStore(db);

        var expActive = new Experiment
        {
            Id = "exp_act",
            TenantId = "ten_01",
            Name = "Active Exp",
            Status = ExperimentStatus.Active
        };
        var expDraft = new Experiment
        {
            Id = "exp_drf",
            TenantId = "ten_01",
            Name = "Draft Exp",
            Status = ExperimentStatus.Draft
        };
        var expOtherTenant = new Experiment
        {
            Id = "exp_oth",
            TenantId = "ten_02",
            Name = "Other Tenant Exp",
            Status = ExperimentStatus.Active
        };

        db.Tenants.AddRange(
            new Tenant { Id = "ten_01", Slug = "acme", Name = "Acme Corp", CreatedAt = DateTimeOffset.UtcNow },
            new Tenant { Id = "ten_02", Slug = "globex", Name = "Globex Corp", CreatedAt = DateTimeOffset.UtcNow }
        );
        await db.SaveChangesAsync();

        await store.SaveAsync(expActive, "ten_01");
        await store.SaveAsync(expDraft, "ten_01");
        await store.SaveAsync(expOtherTenant, "ten_02");

        var allTenant1 = await store.GetAllAsync("ten_01");
        allTenant1.Should().HaveCount(2);

        var activeTenant1 = await store.GetActiveAsync("ten_01");
        activeTenant1.Should().HaveCount(1);
        activeTenant1[0].Id.Should().Be("exp_act");
    }

    [Fact]
    public async Task DeleteAsync_ShouldRemoveExperiment()
    {
        using var db = new SymbolonDbContext(_options);
        var store = new EfExperimentStore(db);

        var exp = new Experiment
        {
            Id = "exp_del",
            Name = "To be deleted",
            Status = ExperimentStatus.Draft
        };
        await store.SaveAsync(exp);

        var deleted = await store.DeleteAsync("exp_del");
        deleted.Should().BeTrue();

        var retrieved = await store.GetByIdAsync("exp_del");
        retrieved.Should().BeNull();
    }

    [Fact]
    public async Task RecordMetricAsync_And_GetMetricsAsync_ShouldAccumulateCorrectly()
    {
        using var db = new SymbolonDbContext(_options);
        var store = new EfExperimentStore(db);

        var exp = new Experiment
        {
            Id = "exp_metrics",
            Name = "Metrics test",
            Status = ExperimentStatus.Active
        };
        await store.SaveAsync(exp);

        // Record 2 successful checkouts with 10ms and 20ms
        await store.RecordMetricAsync("exp_metrics", "ctrl", isSuccess: true, isRenewal: false, isDenial: false, isError: false, latencyMs: 10.0);
        await store.RecordMetricAsync("exp_metrics", "ctrl", isSuccess: true, isRenewal: false, isDenial: false, isError: false, latencyMs: 20.0);

        // Record 1 renewal and 1 error for treatment
        await store.RecordMetricAsync("exp_metrics", "treat", isSuccess: true, isRenewal: true, isDenial: false, isError: false, latencyMs: 15.0);
        await store.RecordMetricAsync("exp_metrics", "treat", isSuccess: false, isRenewal: false, isDenial: false, isError: true, latencyMs: 25.0);

        var metrics = await store.GetMetricsAsync("exp_metrics");
        metrics.Should().HaveCount(2);

        var ctrl = metrics.First(m => m.VariantId == "ctrl");
        ctrl.TotalRequests.Should().Be(2);
        ctrl.SuccessfulCheckouts.Should().Be(2);
        ctrl.Errors.Should().Be(0);
        ctrl.AverageLatencyMs.Should().Be(15.0);

        var treat = metrics.First(m => m.VariantId == "treat");
        treat.TotalRequests.Should().Be(2);
        treat.SuccessfulCheckouts.Should().Be(1);
        treat.Renewals.Should().Be(1);
        treat.Errors.Should().Be(1);
        treat.AverageLatencyMs.Should().Be(20.0);
        treat.ErrorRate.Should().Be(0.5);

        // Reset metrics
        await store.ResetMetricsAsync("exp_metrics");
        var afterReset = await store.GetMetricsAsync("exp_metrics");
        afterReset.Should().BeEmpty();
    }
}
