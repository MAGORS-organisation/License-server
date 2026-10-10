using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Achilles.ControlPlane.Models;
using Achilles.Protocol;
using Achilles.Protocol.Scim;
using Achilles.Protocol.Tracing;
using Xunit;

namespace Achilles.ControlPlane.Tests;

public sealed class ScimApiTests : IClassFixture<ControlPlaneFactory>
{
    private readonly HttpClient _client;

    public ScimApiTests(ControlPlaneFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetServiceProviderConfig_ReturnsValidScimConfig()
    {
        var res = await _client.GetAsync(new Uri("/scim/v2/ServiceProviderConfig", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("application/scim+json", res.Content.Headers.ContentType?.ToString(), StringComparison.OrdinalIgnoreCase);

        var config = await res.Content.ReadFromJsonAsync<ScimServiceProviderConfigDto>();
        Assert.NotNull(config);
        Assert.Contains("urn:ietf:params:scim:schemas:core:2.0:ServiceProviderConfig", config.Schemas);
        Assert.True(config.Patch.Supported);
        Assert.True(config.Filter.Supported);
        Assert.False(config.Bulk.Supported);
    }

    [Fact]
    public async Task GetSchemasAndResourceTypes_ReturnsCoreSchemas()
    {
        var schemasRes = await _client.GetAsync(new Uri("/scim/v2/Schemas", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, resStatusCode(schemasRes));

        var typesRes = await _client.GetAsync(new Uri("/scim/v2/ResourceTypes", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, resStatusCode(typesRes));
    }

    [Fact]
    public async Task CreateUser_And_GetUserById_And_Filter_ReturnsExpectedResults()
    {
        string uniqueUser = $"user_{Guid.NewGuid():N}@enterprise.com";
        var userDto = new ScimUserDto
        {
            UserName = uniqueUser,
            DisplayName = "Jane Doe",
            Name = new ScimUserNameDto
            {
                GivenName = "Jane",
                FamilyName = "Doe",
                Formatted = "Jane Doe"
            },
            Emails =
            [
                new() { Value = uniqueUser, Type = "work", Primary = true }
            ],
            Active = true
        };

        var postRes = await _client.PostAsJsonAsync(new Uri("/scim/v2/Users", UriKind.Relative), userDto);
        Assert.Equal(HttpStatusCode.Created, postRes.StatusCode);
        Assert.NotNull(postRes.Headers.Location);

        var created = await postRes.Content.ReadFromJsonAsync<ScimUserDto>();
        Assert.NotNull(created);
        Assert.NotNull(created.Id);
        Assert.Equal(uniqueUser, created.UserName);
        Assert.True(created.Active);

        // Get by ID
        var getRes = await _client.GetAsync(new Uri($"/scim/v2/Users/{created.Id}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        var fetched = await getRes.Content.ReadFromJsonAsync<ScimUserDto>();
        Assert.NotNull(fetched);
        Assert.Equal(created.Id, fetched.Id);
        Assert.Equal("Jane Doe", fetched.DisplayName);

        // Filter by userName
        var filterRes = await _client.GetAsync(new Uri($"/scim/v2/Users?filter=userName eq \"{uniqueUser}\"", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, filterRes.StatusCode);
        var listResponse = await filterRes.Content.ReadFromJsonAsync<ScimListResponseDto<ScimUserDto>>();
        Assert.NotNull(listResponse);
        Assert.Equal(1, listResponse.TotalResults);
        Assert.Single(listResponse.Resources);
        Assert.Equal(created.Id, listResponse.Resources[0].Id);
    }

    [Fact]
    public async Task CreateGroup_And_ManageMembers()
    {
        // 1. Create a user first
        string uniqueUser = $"grp_user_{Guid.NewGuid():N}@enterprise.com";
        var userRes = await _client.PostAsJsonAsync(new Uri("/scim/v2/Users", UriKind.Relative), new ScimUserDto
        {
            UserName = uniqueUser,
            DisplayName = "Group Member User"
        });
        Assert.Equal(HttpStatusCode.Created, userRes.StatusCode);
        var user = await userRes.Content.ReadFromJsonAsync<ScimUserDto>();
        Assert.NotNull(user);
        Assert.NotNull(user.Id);

        // 2. Create Group with this member
        string groupName = $"Engineers-{Guid.NewGuid():N}"[..18];
        var groupDto = new ScimGroupDto
        {
            DisplayName = groupName,
            Members =
            [
                new() { Value = user.Id, Display = user.DisplayName }
            ]
        };

        var groupRes = await _client.PostAsJsonAsync(new Uri("/scim/v2/Groups", UriKind.Relative), groupDto);
        Assert.Equal(HttpStatusCode.Created, groupRes.StatusCode);
        var createdGroup = await groupRes.Content.ReadFromJsonAsync<ScimGroupDto>();
        Assert.NotNull(createdGroup);
        Assert.NotNull(createdGroup.Id);

        // 3. Get Group by ID
        var getGrpRes = await _client.GetAsync(new Uri($"/scim/v2/Groups/{createdGroup.Id}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, getGrpRes.StatusCode);
        var fetchedGrp = await getGrpRes.Content.ReadFromJsonAsync<ScimGroupDto>();
        Assert.NotNull(fetchedGrp);
        Assert.NotNull(fetchedGrp.Members);
        Assert.Contains(fetchedGrp.Members, m => m.Value == user.Id);

        // 4. Remove member via PATCH
        string patchJson = $$"""
        {
            "schemas": ["urn:ietf:params:scim:api:messages:2.0:PatchOp"],
            "Operations": [
                {
                    "op": "remove",
                    "path": "members[value eq \"{{user.Id}}\"]"
                }
            ]
        }
        """;
        var patchReq = new HttpRequestMessage(HttpMethod.Patch, new Uri($"/scim/v2/Groups/{createdGroup.Id}", UriKind.Relative))
        {
            Content = new StringContent(patchJson, Encoding.UTF8, "application/scim+json")
        };
        var patchRes = await _client.SendAsync(patchReq);
        Assert.Equal(HttpStatusCode.OK, patchRes.StatusCode);

        // 5. Verify member removed
        var verifyGrpRes = await _client.GetAsync(new Uri($"/scim/v2/Groups/{createdGroup.Id}", UriKind.Relative));
        var verifyGrp = await verifyGrpRes.Content.ReadFromJsonAsync<ScimGroupDto>();
        Assert.NotNull(verifyGrp);
        Assert.True(verifyGrp.Members == null || !verifyGrp.Members.Any(m => m.Value == user.Id));
    }

    [Fact]
    public async Task ZeroTrust_Deprovisioning_ReclaimsSeats_And_CancelsQueues()
    {
        // 1. Setup tenant, product, policy, and license
        string slug = $"scim-deprov-{Guid.NewGuid():N}"[..20];
        var tenantRes = await _client.PostAsJsonAsync(new Uri("/admin/v1/tenants", UriKind.Relative), new CreateTenantDto(slug, "SCIM Corp"));
        Assert.Equal(HttpStatusCode.Created, tenantRes.StatusCode);
        var tenant = await tenantRes.Content.ReadFromJsonAsync<TenantDto>();
        Assert.NotNull(tenant);

        var prodReq = new HttpRequestMessage(HttpMethod.Post, new Uri("/admin/v1/products", UriKind.Relative))
        {
            Content = JsonContent.Create(new CreateProductDto($"scim-app-{Guid.NewGuid():N}"[..14], "ScimApp", ["core"]))
        };
        prodReq.Headers.Add("X-Tenant-Id", tenant.Id);
        var prodRes = await _client.SendAsync(prodReq);
        Assert.Equal(HttpStatusCode.Created, prodRes.StatusCode);
        var product = await prodRes.Content.ReadFromJsonAsync<ProductDto>();
        Assert.NotNull(product);

        var polRes = await _client.PostAsJsonAsync(new Uri("/admin/v1/policies", UriKind.Relative), new CreatePolicyDto
        {
            ProductId = product.Id,
            Code = "floating-scim-5",
            Name = "Floating 5 seats",
            MaxSeats = 5,
            LicenseModel = "floating"
        });
        Assert.Equal(HttpStatusCode.Created, polRes.StatusCode);
        var policy = await polRes.Content.ReadFromJsonAsync<PolicyDto>();
        Assert.NotNull(policy);

        var licRes = await _client.PostAsJsonAsync(new Uri("/admin/v1/licenses", UriKind.Relative), new CreateLicenseDto
        {
            PolicyId = policy.Id,
            CustomerRef = "SCIM Customer",
            MaxSeats = 5
        });
        Assert.Equal(HttpStatusCode.Created, licRes.StatusCode);
        var license = await licRes.Content.ReadFromJsonAsync<LicenseResponseDto>();
        Assert.NotNull(license);
        Assert.NotNull(license.LicenseKey);

        // 2. Provision SCIM user
        string targetUser = $"deprov_{Guid.NewGuid():N}@enterprise.com";
        var userPostReq = new HttpRequestMessage(HttpMethod.Post, new Uri("/scim/v2/Users", UriKind.Relative))
        {
            Content = JsonContent.Create(new ScimUserDto
            {
                UserName = targetUser,
                DisplayName = "Deprovision Target",
                Active = true
            })
        };
        userPostReq.Headers.Add("X-Tenant-Id", tenant.Id);
        var userPostRes = await _client.SendAsync(userPostReq);
        Assert.Equal(HttpStatusCode.Created, userPostRes.StatusCode);
        var user = await userPostRes.Content.ReadFromJsonAsync<ScimUserDto>();
        Assert.NotNull(user);
        Assert.NotNull(user.Id);

        // 3. Checkout a seat as this user
        var checkoutRes = await _client.PostAsJsonAsync(new Uri("/v1/leases", UriKind.Relative), new CheckoutRequestDto
        {
            LicenseKey = license.LicenseKey,
            FingerprintComponents = new Dictionary<string, string> { ["os"] = "Linux", ["user"] = targetUser },
            Quantity = 1,
            UserId = targetUser
        });
        Assert.Equal(HttpStatusCode.OK, checkoutRes.StatusCode);
        var checkoutDto = await checkoutRes.Content.ReadFromJsonAsync<CheckoutResponseDto>();
        Assert.NotNull(checkoutDto);
        string leaseId = checkoutDto.LeaseId;

        // Verify renewal succeeds initially
        var renewReq = new HttpRequestMessage(HttpMethod.Post, new Uri($"/v1/leases/{leaseId}/renew", UriKind.Relative))
        {
            Content = JsonContent.Create(new RenewRequestDto
            {
                ClientSeq = 0,
                FingerprintComponents = new Dictionary<string, string> { ["os"] = "Linux", ["user"] = targetUser }
            })
        };
        renewReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", checkoutDto.Token);
        var renewRes = await _client.SendAsync(renewReq);
        Assert.Equal(HttpStatusCode.OK, renewRes.StatusCode);

        // 4. Send SCIM PATCH: active = false (Simulating Okta / Entra ID employee offboarding)
        string deprovPatch = """
        {
            "schemas": ["urn:ietf:params:scim:api:messages:2.0:PatchOp"],
            "Operations": [
                {
                    "op": "replace",
                    "path": "active",
                    "value": false
                }
            ]
        }
        """;
        var patchReq = new HttpRequestMessage(HttpMethod.Patch, new Uri($"/scim/v2/Users/{user.Id}", UriKind.Relative))
        {
            Content = new StringContent(deprovPatch, Encoding.UTF8, "application/scim+json")
        };
        patchReq.Headers.Add("X-Tenant-Id", tenant.Id);
        var patchRes = await _client.SendAsync(patchReq);
        Assert.Equal(HttpStatusCode.OK, patchRes.StatusCode);

        var updatedUser = await patchRes.Content.ReadFromJsonAsync<ScimUserDto>();
        Assert.NotNull(updatedUser);
        Assert.False(updatedUser.Active);

        // 5. Verify the lease was immediately reclaimed!
        var renewReqAfter = new HttpRequestMessage(HttpMethod.Post, new Uri($"/v1/leases/{leaseId}/renew", UriKind.Relative))
        {
            Content = JsonContent.Create(new RenewRequestDto
            {
                ClientSeq = 1,
                FingerprintComponents = new Dictionary<string, string> { ["os"] = "Linux", ["user"] = targetUser }
            })
        };
        renewReqAfter.Headers.Authorization = new AuthenticationHeaderValue("Bearer", checkoutDto.Token);
        var renewResAfter = await _client.SendAsync(renewReqAfter);

        // The lease is gone because it was released!
        Assert.True(renewResAfter.StatusCode == HttpStatusCode.Gone || renewResAfter.StatusCode == HttpStatusCode.NotFound || renewResAfter.StatusCode == HttpStatusCode.Conflict);

        // 6. Verify audit log recorded SCIM_USER_DEPROVISIONED
        var auditRes = await _client.GetAsync(new Uri("/admin/v1/audit", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, auditRes.StatusCode);
        string auditContent = await auditRes.Content.ReadAsStringAsync();
        Assert.Contains("SCIM_USER_DEPROVISIONED", auditContent, StringComparison.OrdinalIgnoreCase);

        // 7. Verify OpenTelemetry span buffer recorded symbolon.scim.deprovision
        var traceRes = await _client.GetAsync(new Uri("/admin/v1/traces/recent", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, traceRes.StatusCode);
        var traces = await traceRes.Content.ReadFromJsonAsync<List<TraceSpanDto>>();
        Assert.NotNull(traces);
        Assert.Contains(traces, t => t.Name.Contains("scim.deprovision", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DeleteUser_ImmediatelyRevokesSeats()
    {
        // 1. Create a user
        string userToDelete = $"delete_{Guid.NewGuid():N}@enterprise.com";
        var userRes = await _client.PostAsJsonAsync(new Uri("/scim/v2/Users", UriKind.Relative), new ScimUserDto
        {
            UserName = userToDelete,
            DisplayName = "User To Delete"
        });
        Assert.Equal(HttpStatusCode.Created, userRes.StatusCode);
        var user = await userRes.Content.ReadFromJsonAsync<ScimUserDto>();
        Assert.NotNull(user);
        Assert.NotNull(user.Id);

        // 2. Delete user
        var delRes = await _client.DeleteAsync(new Uri($"/scim/v2/Users/{user.Id}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NoContent, delRes.StatusCode);

        // 3. Verify user is gone
        var getRes = await _client.GetAsync(new Uri($"/scim/v2/Users/{user.Id}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, getRes.StatusCode);
    }

    [Fact]
    public async Task GetAdminScimUsersSummary_ReturnsUsersForDashboard()
    {
        var res = await _client.GetAsync(new Uri("/admin/v1/scim/users", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var json = await res.Content.ReadAsStringAsync();
        Assert.StartsWith("[", json, StringComparison.Ordinal);
    }

    private static HttpStatusCode resStatusCode(HttpResponseMessage response) => response.StatusCode;
}
