using FluentAssertions;
using Achilles.Crypto;
using Achilles.Domain;
using Achilles.Format;
using Xunit;

namespace Achilles.Relay.Tests;

public sealed class RelaySeatGrantTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteSeatStore _store;

    public RelaySeatGrantTests()
    {
        _dbPath = $"test_grant_{Guid.NewGuid():N}.db";
        _store = new SqliteSeatStore($"Data Source={_dbPath}");
    }

    public void Dispose()
    {
        _store.Dispose();
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { /* ignore */ }
        }
    }

    private static SeatGrantDocumentClaims CreateSampleGrant(
        string licenseId,
        string relayId,
        int seats,
        int seatFrom,
        int seatTo,
        long seq,
        long? supersedes = null)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return new SeatGrantDocumentClaims
        {
            Iss = "https://licenses.acme.example",
            Sub = licenseId,
            Aud = relayId,
            Jti = $"gnt_{Guid.NewGuid():N}",
            Iat = now,
            Nbf = now - 60,
            Exp = now + 86400,
            Symgrant = new SeatGrantPayload
            {
                V = 1,
                Seats = seats,
                SeatRange = [seatFrom, seatTo],
                Seq = seq,
                Supersedes = supersedes,
                Entitlements = ["core"],
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
    public async Task ImportSeatGrantAsync_GNT7_EnforcesMonotonicSequence()
    {
        string licId = "lic_monotonic_seq";
        string rlyId = "rly_01";

        var grant1 = CreateSampleGrant(licId, rlyId, 3, 0, 2, seq: 10);
        await _store.ImportSeatGrantAsync(grant1, "raw-doc-1");

        long lastSeq = await _store.GetLastSeqAsync(licId);
        lastSeq.Should().Be(10);

        // Attempting to import same or lower sequence must throw (GNT-7)
        var grantReplay = CreateSampleGrant(licId, rlyId, 3, 0, 2, seq: 10);
        var actReplay = async () => await _store.ImportSeatGrantAsync(grantReplay, "raw-doc-replay");
        await actReplay.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*sequence-rollback-detected*");

        var grantRollback = CreateSampleGrant(licId, rlyId, 3, 0, 2, seq: 9);
        var actRollback = async () => await _store.ImportSeatGrantAsync(grantRollback, "raw-doc-rollback");
        await actRollback.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*sequence-rollback-detected*");

        // Higher sequence succeeds
        var grant2 = CreateSampleGrant(licId, rlyId, 3, 0, 2, seq: 11);
        await _store.ImportSeatGrantAsync(grant2, "raw-doc-2");

        lastSeq = await _store.GetLastSeqAsync(licId);
        lastSeq.Should().Be(11);
    }

    [Fact]
    public async Task ImportSeatGrantAsync_GNT10_RestrictsLeasesToGrantedSeatRange()
    {
        string licId = "lic_gnt10_test";
        string rlyId = "rly_01";

        // Grant delegates seats 10..12 (3 seats)
        var grant = CreateSampleGrant(licId, rlyId, 3, 10, 12, seq: 1);
        await _store.ImportSeatGrantAsync(grant, "raw-doc");

        var now = DateTimeOffset.UtcNow;
        var ttl = TimeSpan.FromMinutes(10);

        var seat1 = await _store.TryAcquireOneAsync(licId, "sha256:fp1", "pc1", now, ttl);
        seat1.Should().NotBeNull();
        seat1!.SeatNo.Should().BeInRange(10, 12);

        var seat2 = await _store.TryAcquireOneAsync(licId, "sha256:fp2", "pc2", now, ttl);
        seat2.Should().NotBeNull();
        seat2!.SeatNo.Should().BeInRange(10, 12);
        seat2.SeatNo.Should().NotBe(seat1.SeatNo);

        var seat3 = await _store.TryAcquireOneAsync(licId, "sha256:fp3", "pc3", now, ttl);
        seat3.Should().NotBeNull();
        seat3!.SeatNo.Should().BeInRange(10, 12);

        // 4th checkout must fail since only 3 seats are in granted range
        var seat4 = await _store.TryAcquireOneAsync(licId, "sha256:fp4", "pc4", now, ttl);
        seat4.Should().BeNull();
    }

    [Fact]
    public async Task RelaySeatGrantManager_CreateGrantRequest_FLT32_ProducesVerifiableSymreq()
    {
        using var relayKey = Es256SignatureProvider.GenerateKey("rly-enterprise-01");
        using var keyRing = new AchillesKeyRing();
        keyRing.Add(relayKey);

        var auditLedger = new InMemoryAuditLedger();
        await auditLedger.AppendAsync(new AuditEvent("checkout", "lic_airgap_demo", "lse_1", "sha256:fp", DateTimeOffset.UtcNow, "detail"));

        var manager = new RelaySeatGrantManager(_store, keyRing, relayKey, auditLedger);

        string symreqPem = await manager.CreateGrantRequestAsync("SYM1-DEMO-AIRGAP-KEY-001", "lic_airgap_demo", 5);

        symreqPem.Should().Contain("-----BEGIN SYMBOLON GRANT REQUEST-----");
        symreqPem.Should().Contain("-----END SYMBOLON GRANT REQUEST-----");

        // Verify with AirGapRequestVerifier
        var verifier = new AirGapRequestVerifier(relayKey);
        var result = verifier.Verify(symreqPem);

        result.IsValid.Should().BeTrue();
        result.Claims.Should().NotBeNull();
        result.Claims!.Symreq.RelayId.Should().Be("rly-enterprise-01");
        result.Claims.Symreq.LicenseKey.Should().Be("SYM1-DEMO-AIRGAP-KEY-001");
        result.Claims.Symreq.RequestedSeats.Should().Be(5);
        result.Claims.Symreq.LastSeq.Should().Be(0);
        result.Claims.Symreq.UsageDigest.Should().StartWith("sha256:");
        result.Claims.Symreq.Nonce.Should().StartWith("nonce_");
    }
}
