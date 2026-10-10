using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Achilles.Data;
using Achilles.Data.Entities;
using Achilles.Domain;
using Achilles.Protocol.Scim;
using Achilles.Protocol.Sso;
using Xunit;

namespace Achilles.ControlPlane.Tests;

public sealed class ScimWorkerAndOidcTests : IClassFixture<ControlPlaneFactory>
{
    private static readonly string[] MockGroups = ["DevOps", "Architects"];

    private readonly ControlPlaneFactory _factory;
    private readonly HttpClient _client;

    public ScimWorkerAndOidcTests(ControlPlaneFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task DirectorySync_ReclaimsSeatsAndAssignments_ForDeprovisionedUsers()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AchillesDbContext>();
        var now = DateTimeOffset.UtcNow;

        string tenantId = $"tenant_scimsync_{Guid.NewGuid():N}";
        db.Tenants.Add(new Tenant { Id = tenantId, Slug = tenantId, Name = "SCIM Sync Tenant", CreatedAt = now });

        string prodId = $"prd_{Guid.NewGuid():N}";
        db.Products.Add(new Product
        {
            Id = prodId,
            TenantId = tenantId,
            Code = $"PRD_{Guid.NewGuid():N}",
            Name = "Sync Product",
            PlatformsJson = "[\"windows\"]",
            CreatedAt = now
        });

        string policyId = $"pol_{Guid.NewGuid():N}";
        db.Policies.Add(new Policy
        {
            Id = policyId,
            TenantId = tenantId,
            ProductId = prodId,
            Code = $"POL_{Guid.NewGuid():N}",
            Name = "Sync Policy",
            LicenseModel = "floating",
            MaxSeats = 10
        });

        string licenseId = $"lic_{Guid.NewGuid():N}";
        db.Licenses.Add(new LicenseEntity
        {
            Id = licenseId,
            TenantId = tenantId,
            PolicyId = policyId,
            MaxSeats = 10,
            State = "active",
            CreatedAt = now
        });

        string userEmail = $"departed.user_{Guid.NewGuid():N}@enterprise.com";
        string scimUserId = $"usr_scim_{Guid.NewGuid():N}";

        // Create inactive SCIM user
        db.ScimUsers.Add(new ScimUserEntity
        {
            Id = scimUserId,
            TenantId = tenantId,
            UserName = userEmail,
            Email = userEmail,
            Active = false, // Deprovisioned employee
            CreatedAt = now.AddDays(-30),
            UpdatedAt = now
        });

        // Create active floating seat lease assigned to this departed user
        string leaseId = $"lse_sync_{Guid.NewGuid():N}";
        db.Seats.Add(new SeatEntity
        {
            LicenseId = licenseId,
            SeatNo = 1,
            LeaseId = leaseId,
            UserId = userEmail,
            AcquiredAt = now.AddHours(-1),
            ExpiresAt = now.AddHours(2), // Active lease that should be reclaimed
            LeaseSeq = 1
        });

        // Create named user assignment for this departed user
        db.LicenseUsers.Add(new LicenseUserEntity
        {
            Id = $"lu_{Guid.NewGuid():N}",
            LicenseId = licenseId,
            UserId = userEmail,
            CreatedAt = now.AddDays(-10)
        });

        await db.SaveChangesAsync();

        // Trigger on-demand directory sync via Admin endpoint
        var syncReq = new HttpRequestMessage(HttpMethod.Post, $"/admin/v1/sso/directory-sync?tenantId={tenantId}");
        syncReq.Headers.Add("X-Tenant-Id", tenantId);
        var syncRes = await _client.SendAsync(syncReq);
        syncRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var resultDto = await syncRes.Content.ReadFromJsonAsync<DirectorySyncResultDto>();
        resultDto.Should().NotBeNull();
        resultDto!.IsSuccess.Should().BeTrue();
        resultDto.InactiveUsersScanned.Should().BeGreaterOrEqualTo(1);
        resultDto.ReclaimedSeatsCount.Should().Be(1);
        resultDto.ReclaimedAssignmentsCount.Should().Be(1);

        // Verify seat was freed
        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AchillesDbContext>();

        var seat = await verifyDb.Seats.FirstOrDefaultAsync(s => s.LicenseId == licenseId && s.SeatNo == 1);
        seat.Should().NotBeNull();
        seat!.LeaseId.Should().BeNull();

        // Verify named user assignment was removed
        var assignment = await verifyDb.LicenseUsers.FirstOrDefaultAsync(lu => lu.LicenseId == licenseId && lu.UserId == userEmail);
        assignment.Should().BeNull();

        // Verify audit ledger recorded the deprovisioning reclamation
        var audit = await verifyDb.AuditEvents
            .Where(a => a.TenantId == tenantId && a.Type == "scim.user.reconciled_deprovisioned")
            .FirstOrDefaultAsync();
        audit.Should().NotBeNull();
        audit!.LicenseId.Should().Be(licenseId);
        audit.PayloadJson.Should().Contain(userEmail);
    }

    [Fact]
    public async Task OidcExchange_And_SessionToken_AllowsAuthenticatedAccess()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AchillesDbContext>();
        var now = DateTimeOffset.UtcNow;

        string tenantId = $"tenant_oidc_{Guid.NewGuid():N}";
        db.Tenants.Add(new Tenant { Id = tenantId, Slug = tenantId, Name = "OIDC Tenant", CreatedAt = now });

        string providerId = $"sso_prov_{Guid.NewGuid():N}";
        db.SsoProviders.Add(new SsoProviderEntity
        {
            Id = providerId,
            TenantId = tenantId,
            ProviderType = "oidc",
            DisplayName = "Corporate Okta",
            Issuer = "https://corp.okta.com",
            ClientId = "symbolon_cli_client",
            DefaultRole = "admin:tenant",
            IsEnabled = true,
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();

        // Build a mock JWT ID Token with claims
        string sub = "corp_emp_12345";
        string email = "employee@corp.com";
        string name = "Alice Engineer";

        string jwtHeader = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"alg\":\"none\"}"));
        string jwtPayload = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            sub,
            email,
            name,
            groups = MockGroups
        })));
        string mockIdToken = $"{jwtHeader}.{jwtPayload}.dummy_signature";

        // Exchange OIDC ID Token for session token
        var exchangeRes = await _client.PostAsJsonAsync("/auth/sso/oidc/exchange", new OidcTokenExchangeRequestDto
        {
            ProviderId = providerId,
            IdToken = mockIdToken
        });
        exchangeRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var exchangeDto = await exchangeRes.Content.ReadFromJsonAsync<OidcTokenExchangeResponseDto>();
        exchangeDto.Should().NotBeNull();
        exchangeDto!.SessionToken.Should().NotBeNullOrEmpty();
        exchangeDto.User.UserId.Should().Be(sub);
        exchangeDto.User.Email.Should().Be(email);
        exchangeDto.User.UserName.Should().Be(name);

        // Call /auth/sso/me with session token in Authorization Bearer
        var meReq = new HttpRequestMessage(HttpMethod.Get, "/auth/sso/me");
        meReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", exchangeDto.SessionToken);
        var meRes = await _client.SendAsync(meReq);
        meRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var meProfile = await meRes.Content.ReadFromJsonAsync<SsoUserProfileDto>();
        meProfile.Should().NotBeNull();
        meProfile!.IsAuthenticated.Should().BeTrue();
        meProfile.UserId.Should().Be(sub);
        meProfile.Email.Should().Be(email);
    }
}
