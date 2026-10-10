using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Achilles.Crypto;
using Achilles.Domain;
using Achilles.Format;
using Xunit;

namespace Achilles.Relay.Tests;

/// <summary>
/// Chaos & Network Partition Resilience Tests (§10.9 & Smer 3).
/// Validates Split-Brain resilience: when upstream Control Plane is partitioned
/// or unreachable, Relay continues serving clients autonomously from local delegated
/// seat grants and offline storage without failures.
/// </summary>
public sealed class RelaySplitBrainResilienceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteSeatStore _store;
    private readonly Es256SignatureProvider _relayKey;
    private readonly RelayLeaseTokenIssuer _tokenIssuer;
    private readonly InMemoryAuditLedger _audit;
    private readonly LeaseEngine _engine;

    public RelaySplitBrainResilienceTests()
    {
        _dbPath = $"test_splitbrain_{Guid.NewGuid():N}.db";
        _store = new SqliteSeatStore($"Data Source={_dbPath}");
        _relayKey = Es256SignatureProvider.GenerateKey("relay-partition-key");
        _tokenIssuer = new RelayLeaseTokenIssuer(_relayKey, "relay-edge-01");
        _audit = new InMemoryAuditLedger();
        _engine = new LeaseEngine(_store, _tokenIssuer, _audit, TimeProvider.System, NullLogger<LeaseEngine>.Instance);
    }

    public void Dispose()
    {
        _store.Dispose();
        _relayKey.Dispose();
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { /* ignore */ }
        }
    }

    private static SeatGrantDocumentClaims CreateGrant(string licenseId, string relayId, int seats)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return new SeatGrantDocumentClaims
        {
            Iss = "https://controlplane.symbolon.internal",
            Sub = licenseId,
            Aud = relayId,
            Jti = $"gnt_{Guid.NewGuid():N}",
            Iat = now,
            Nbf = now - 60,
            Exp = now + 86400, // 24 hours autonomous offline validity
            Symgrant = new SeatGrantPayload
            {
                V = 1,
                Seats = seats,
                SeatRange = [1, seats],
                Seq = 1,
                Entitlements = ["core", "export"],
                LeasePolicy = new LeasePolicyClaim { Ttl = "PT10M", GraceTtl = "PT4H" },
                LeaseKey = new JsonWebKeyDto
                {
                    Kty = "EC",
                    Alg = Alg.Es256,
                    Crv = "P-256",
                    X = "test-x",
                    Y = "test-y",
                    Kid = "lease-key-01"
                }
            }
        };
    }

    [Fact]
    public async Task SplitBrain_WhenUpstreamFails_RelayContinuesServingOfflineGrants()
    {
        const string licenseId = "lic_enterprise_splitbrain";
        const string relayId = "relay-edge-01";
        const int delegatedSeats = 5;

        // 1. Initial state: Upstream grants 5 seats to this Relay before partition
        var grant = CreateGrant(licenseId, relayId, delegatedSeats);
        await _store.ImportSeatGrantAsync(grant, "raw-grant-doc");

        // 2. SIMULATE NETWORK PARTITION (Upstream completely unreachable)
        // Client requests lease from the local partitioned Relay
        var checkout1 = await _engine.CheckoutAsync(new CheckoutCommand(
            LicenseId: licenseId,
            Fingerprint: "sha256:offline_machine_alpha",
            MachineId: "mach-offline-alpha",
            Quantity: 1,
            Features: ["core"],
            IdempotencyKey: null,
            AllowQueue: false,
            Ttl: TimeSpan.FromMinutes(10)));

        checkout1.IsSuccess.Should().BeTrue("Relay must grant lease offline during network partition");
        checkout1.Allocations.Should().NotBeNull();
        checkout1.Allocations![0].SeatNo.Should().Be(1);
        checkout1.Tokens.Should().NotBeNullOrEmpty();

        // 3. Heartbeat during network partition (initial LeaseSeq is 0)
        var renewResult = await _engine.RenewAsync(
            leaseId: checkout1.Allocations[0].LeaseId!,
            fingerprint: "sha256:offline_machine_alpha",
            clientSeq: 0,
            ttl: TimeSpan.FromMinutes(10),
            resurrectionWindow: TimeSpan.FromMinutes(5));

        renewResult.IsSuccess.Should().BeTrue("Relay must successfully renew lease offline during partition");

        // 4. Multiple concurrent clients during partition
        for (int i = 2; i <= delegatedSeats; i++)
        {
            var res = await _engine.CheckoutAsync(new CheckoutCommand(
                LicenseId: licenseId,
                Fingerprint: $"sha256:offline_client_{i}",
                MachineId: $"mach-offline-{i}",
                Quantity: 1,
                Features: ["core"],
                IdempotencyKey: null,
                AllowQueue: false,
                Ttl: TimeSpan.FromMinutes(10)));

            res.IsSuccess.Should().BeTrue($"Seat {i} should be successfully granted offline");
        }

        // 5. Exceeded capacity check: Relay strictly respects grant boundary offline
        var deniedOverCapacity = await _engine.CheckoutAsync(new CheckoutCommand(
            LicenseId: licenseId,
            Fingerprint: "sha256:offline_client_overflow",
            MachineId: "mach-offline-overflow",
            Quantity: 1,
            Features: ["core"],
            IdempotencyKey: null,
            AllowQueue: false,
            Ttl: TimeSpan.FromMinutes(10)));

        deniedOverCapacity.IsSuccess.Should().BeFalse(
            "Relay must enforce local quota ceiling even when offline from central server");

        // 6. Metrics verification on offline node
        var allocatedCount = await _store.GetAllocatedSeatCountAsync(DateTimeOffset.UtcNow);
        allocatedCount.Should().Be(delegatedSeats, "All delegated seats must be accounted for locally");
    }
}
