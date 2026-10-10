using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Symbolon.ControlPlane.Models;
using Symbolon.Data;
using Symbolon.Data.Entities;
using Symbolon.Domain;
using Symbolon.Domain.Transparency;
using Symbolon.Protocol;
using Symbolon.Protocol.Reporting;
using Xunit;

namespace Symbolon.ControlPlane.Tests;

public sealed class AuditHashChainTests : IClassFixture<ControlPlaneFactory>
{
    private readonly ControlPlaneFactory _factory;
    private readonly HttpClient _client;

    public AuditHashChainTests(ControlPlaneFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task AuditChainStatus_WhenEventsExist_ReturnsIntactProof()
    {
        using var scope = _factory.Services.CreateScope();
        var ledger = scope.ServiceProvider.GetRequiredService<IAuditLedger>();
        var db = scope.ServiceProvider.GetRequiredService<SymbolonDbContext>();

        string tenantId = $"tenant_chain_{Guid.NewGuid():N}";
        db.Tenants.Add(new Tenant { Id = tenantId, Slug = tenantId, Name = "Chain Tenant", CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var ev = new AuditEvent("chain.test.event", "lic_chain", "lease_chain", "fp_chain", DateTimeOffset.UtcNow, "chain test payload", TenantId: tenantId);
        await ledger.AppendAsync(ev);

        // 2. Query chain status for isolated tenant
        var statusRes = await _client.GetAsync(new Uri($"/admin/v1/audit/chain-status?tenantId={tenantId}", UriKind.Relative));
        statusRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var proof = await statusRes.Content.ReadFromJsonAsync<AuditVerificationProofDto>();
        proof.Should().NotBeNull();
        proof!.TamperReason.Should().BeNull();
        proof.IsChainIntact.Should().BeTrue();
        proof.TotalEventsVerified.Should().Be(1);
        proof.RootHashHex.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task AuditProof_ForSpecificEvent_GeneratesValidMerkleInclusionProof()
    {
        // 1. Append a known event through IAuditLedger
        using var scope = _factory.Services.CreateScope();
        var ledger = scope.ServiceProvider.GetRequiredService<IAuditLedger>();
        var db = scope.ServiceProvider.GetRequiredService<SymbolonDbContext>();

        string tenantId = $"tenant_proof_{Guid.NewGuid():N}";
        db.Tenants.Add(new Tenant { Id = tenantId, Slug = tenantId, Name = "Proof Tenant", CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var now = DateTimeOffset.UtcNow;
        var ev = new AuditEvent("test.merkle.event", "lic_proof", "lease_test", "fp_test", now, "detail payload", TenantId: tenantId);
        await ledger.AppendAsync(ev);

        var lastEvent = await db.AuditEvents
            .OrderByDescending(a => a.TsServer)
            .FirstOrDefaultAsync(a => a.TenantId == tenantId && a.Type == "test.merkle.event");

        lastEvent.Should().NotBeNull();
        string targetId = lastEvent!.Id;

        // 2. Query proof endpoint
        var proofRes = await _client.GetAsync(new Uri($"/admin/v1/audit/{targetId}/proof?tenantId={tenantId}", UriKind.Relative));
        proofRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var proof = await proofRes.Content.ReadFromJsonAsync<TransparencyInclusionResponseDto>();
        proof.Should().NotBeNull();
        proof!.AuditId.Should().Be(targetId);
        proof.TreeSize.Should().BeGreaterThan(0);

        // 3. Cryptographically verify inclusion locally
        byte[] leafHash = Convert.FromHexString(proof.LeafHash);
        byte[] rootHash = Convert.FromHexString(proof.RootHash);
        var steps = proof.Path.Select(p => new MerkleProofStep(p.Hash, p.Direction)).ToList();

        bool isValid = MerkleTree.VerifyInclusion(leafHash, rootHash, steps);
        isValid.Should().BeTrue("Merkle inclusion proof for audit event must cryptographically verify");
    }

    [Fact]
    public async Task AuditChainStatus_WhenRecordIsTampered_DetectsCorruption()
    {
        // 1. Append two events
        using var scope = _factory.Services.CreateScope();
        var ledger = scope.ServiceProvider.GetRequiredService<IAuditLedger>();
        var db = scope.ServiceProvider.GetRequiredService<SymbolonDbContext>();

        string tenantId = $"tenant_tamper_{Guid.NewGuid():N}";
        db.Tenants.Add(new Tenant { Id = tenantId, Slug = tenantId, Name = "Tamper Tenant", CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var ev1 = new AuditEvent("tamper.test.1", "lic_tamper", null, null, DateTimeOffset.UtcNow, "detail 1", TenantId: tenantId);
        var ev2 = new AuditEvent("tamper.test.2", "lic_tamper", null, null, DateTimeOffset.UtcNow.AddSeconds(1), "detail 2", TenantId: tenantId);

        await ledger.AppendAsync(ev1);
        await ledger.AppendAsync(ev2);

        var eventToTamper = await db.AuditEvents
            .Where(a => a.TenantId == tenantId && a.Type == "tamper.test.1")
            .FirstOrDefaultAsync();

        eventToTamper.Should().NotBeNull();

        // 2. Tamper with the payload directly in the database
        eventToTamper!.PayloadJson = "{\"tampered\": true}";
        await db.SaveChangesAsync();

        // 3. Verify that chain status flags corruption for that tenant
        var statusRes = await _client.GetAsync(new Uri($"/admin/v1/audit/chain-status?tenantId={tenantId}", UriKind.Relative));
        statusRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var proof = await statusRes.Content.ReadFromJsonAsync<AuditVerificationProofDto>();
        proof.Should().NotBeNull();
        proof!.IsChainIntact.Should().BeFalse();
        proof.TamperedEventId.Should().Be(eventToTamper.Id);
        proof.TamperReason.Should().Contain("modified or tampered with");
    }
}
