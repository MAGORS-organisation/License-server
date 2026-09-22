using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Symbolon.ControlPlane.Security.Sso;
using Symbolon.Data.Entities;
using Symbolon.Protocol;
using Symbolon.Protocol.Sso;
using Xunit;

namespace Symbolon.ControlPlane.Tests;

public sealed class SsoApiTests : IClassFixture<ControlPlaneFactory>
{
    private readonly HttpClient _client;

    public SsoApiTests(ControlPlaneFactory factory)
    {
        _client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public void Pkce_GeneratesValidVerifierAndChallenge()
    {
        var (verifier, challenge) = SsoEngine.GeneratePkce();
        Assert.False(string.IsNullOrWhiteSpace(verifier));
        Assert.False(string.IsNullOrWhiteSpace(challenge));
        Assert.True(verifier.Length >= 43);

        // Verify SHA256 match
        byte[] expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(verifier));
        string expectedChallenge = Convert.ToBase64String(expectedHash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Equal(expectedChallenge, challenge);
    }

    [Fact]
    public void SsoSessionManager_CreateAndValidateToken_Succeeds()
    {
        var mgr = new SsoSessionManager();
        string token = mgr.CreateSessionToken(
            userId: "usr_alice",
            userName: "Alice Smith",
            email: "alice@example.com",
            role: "admin:tenant",
            tenantId: "ten_default",
            ttl: TimeSpan.FromMinutes(10));

        Assert.StartsWith("sym_sso_", token, StringComparison.Ordinal);

        var claims = mgr.ValidateSessionToken(token);
        Assert.NotNull(claims);
        Assert.Equal("usr_alice", claims.Sub);
        Assert.Equal("Alice Smith", claims.Name);
        Assert.Equal("alice@example.com", claims.Email);
        Assert.Equal("admin:tenant", claims.Role);
        Assert.Equal("ten_default", claims.TenantId);

        // Tampered token fails
        string tampered = token + "bad";
        Assert.Null(mgr.ValidateSessionToken(tampered));

        // Invalid prefix fails
        Assert.Null(mgr.ValidateSessionToken("bad_token"));
    }

    [Fact]
    public void SsoSessionManager_ProtectAndUnprotectState_Succeeds()
    {
        var mgr = new SsoSessionManager();
        var stateData = new SsoStateData(
            ProviderId: "prov_123",
            TenantId: "ten_default",
            CodeVerifier: "verifier_abc",
            Nonce: "nonce_xyz",
            Timestamp: DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        string state = mgr.ProtectState(stateData);
        Assert.False(string.IsNullOrWhiteSpace(state));

        var decoded = mgr.UnprotectState(state);
        Assert.NotNull(decoded);
        Assert.Equal("prov_123", decoded.ProviderId);
        Assert.Equal("ten_default", decoded.TenantId);
        Assert.Equal("verifier_abc", decoded.CodeVerifier);
        Assert.Equal("nonce_xyz", decoded.Nonce);

        // Tampered state fails
        Assert.Null(mgr.UnprotectState(state + "corrupted"));
    }

    [Fact]
    public void EnterpriseRbac_ResolveUserRole_HonorsPriorityHierarchy()
    {
        var provider = new SsoProviderEntity
        {
            DefaultRole = "admin:tenant",
            RoleMappingJson = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["Okta-SuperAdmins"] = "admin:super",
                ["Okta-DevOps"] = "admin:tenant",
                ["Okta-Compliance"] = "auditor"
            })
        };

        // 1. Multiple groups including SuperAdmin -> admin:super
        string role1 = SsoEngine.ResolveUserRole(provider, ["Okta-DevOps", "Okta-SuperAdmins", "Okta-Compliance"], null);
        Assert.Equal("admin:super", role1);

        // 2. Tenant admin group -> admin:tenant
        string role2 = SsoEngine.ResolveUserRole(provider, ["Okta-DevOps", "Okta-Compliance"], null);
        Assert.Equal("admin:tenant", role2);

        // 3. Auditor group -> auditor
        string role3 = SsoEngine.ResolveUserRole(provider, ["Okta-Compliance"], null);
        Assert.Equal("auditor", role3);

        // 4. Unknown group falls back to default role
        string role4 = SsoEngine.ResolveUserRole(provider, ["Unknown-Group"], null);
        Assert.Equal("admin:tenant", role4);

        // 5. Direct role takes priority if recognized
        string role5 = SsoEngine.ResolveUserRole(provider, ["Okta-Compliance"], "admin:super");
        Assert.Equal("admin:super", role5);
    }

    [Fact]
    public void SamlEngine_GenerateMetadataAndParseResponse_Succeeds()
    {
        string spEntityId = "https://license.corp.local/auth/sso/saml/metadata";
        string acsEndpoint = "https://license.corp.local/auth/sso/saml/acs";

        string metadataXml = SsoEngine.GenerateSpMetadataXml(spEntityId, acsEndpoint);
        Assert.Contains("md:EntityDescriptor", metadataXml, StringComparison.Ordinal);
        Assert.Contains(spEntityId, metadataXml, StringComparison.Ordinal);
        Assert.Contains(acsEndpoint, metadataXml, StringComparison.Ordinal);

        // Build valid SAML 2.0 Response XML
        string samlXml = $"""
        <samlp:Response xmlns:samlp="urn:oasis:names:tc:SAML:2.0:protocol"
                        xmlns:saml="urn:oasis:names:tc:SAML:2.0:assertion"
                        ID="_response_1" Version="2.0" IssueInstant="2026-09-22T12:00:00Z">
            <samlp:Status>
                <samlp:StatusCode Value="urn:oasis:names:tc:SAML:2.0:status:Success"/>
            </samlp:Status>
            <saml:Assertion ID="_assertion_1" Version="2.0" IssueInstant="2026-09-22T12:00:00Z">
                <saml:Issuer>https://idp.okta.com/exk123</saml:Issuer>
                <saml:Subject>
                    <saml:NameID Format="urn:oasis:names:tc:SAML:1.1:nameid-format:emailAddress">john.doe@enterprise.com</saml:NameID>
                </saml:Subject>
                <saml:Conditions>
                    <saml:AudienceRestriction>
                        <saml:Audience>{spEntityId}</saml:Audience>
                    </saml:AudienceRestriction>
                </saml:Conditions>
                <saml:AttributeStatement>
                    <saml:Attribute Name="displayName">
                        <saml:AttributeValue>John Doe</saml:AttributeValue>
                    </saml:Attribute>
                    <saml:Attribute Name="email">
                        <saml:AttributeValue>john.doe@enterprise.com</saml:AttributeValue>
                    </saml:Attribute>
                    <saml:Attribute Name="groups">
                        <saml:AttributeValue>Enterprise-Admins</saml:AttributeValue>
                        <saml:AttributeValue>DevOps-Team</saml:AttributeValue>
                    </saml:Attribute>
                </saml:AttributeStatement>
            </saml:Assertion>
        </samlp:Response>
        """;

        string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(samlXml));
        var res = SsoEngine.ParseSamlResponse(b64, spEntityId);

        Assert.True(res.IsSuccess);
        Assert.Equal("john.doe@enterprise.com", res.NameId);
        Assert.Equal("John Doe", res.DisplayName);
        Assert.Equal("john.doe@enterprise.com", res.Email);
        Assert.Contains("Enterprise-Admins", res.Groups);
        Assert.Contains("DevOps-Team", res.Groups);
    }

    [Fact]
    public async Task SsoFlow_EndToEnd_OidcConfiguration_Login_Callback_And_SessionAccess()
    {
        // 1. Configure OIDC provider via Admin API
        string providerId = $"sso_oidc_{Guid.NewGuid():N}";
        var configDto = new SsoProviderConfigDto
        {
            Id = providerId,
            TenantId = "ten_default",
            ProviderType = "oidc",
            DisplayName = "Okta Workforce OIDC",
            Issuer = "https://enterprise.okta.com",
            ClientId = "0oa1234567890",
            ClientSecret = "secret_abc_123",
            AuthorizationEndpoint = "https://enterprise.okta.com/oauth2/v1/authorize",
            TokenEndpoint = "https://enterprise.okta.com/oauth2/v1/token",
            RoleMappings = new Dictionary<string, string>
            {
                ["GlobalAdmins"] = "admin:super",
                ["Developers"] = "admin:tenant"
            },
            DefaultRole = "admin:tenant",
            IsEnabled = true
        };

        var postRes = await _client.PostAsJsonAsync(new Uri("/admin/v1/sso/configs", UriKind.Relative), configDto);
        Assert.Equal(HttpStatusCode.OK, postRes.StatusCode);

        // 2. Query public providers list
        var provsRes = await _client.GetAsync(new Uri("/auth/sso/providers", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, provsRes.StatusCode);
        var provs = await provsRes.Content.ReadFromJsonAsync<List<SsoProviderSummaryDto>>();
        Assert.NotNull(provs);
        Assert.Contains(provs, p => p.Id == providerId && p.DisplayName == "Okta Workforce OIDC");

        // 3. Initiate OIDC login -> 302 Redirect to IdP
        var loginRes = await _client.GetAsync(new Uri($"/auth/sso/oidc/login?providerId={providerId}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Redirect, loginRes.StatusCode);
        var targetUri = loginRes.Headers.Location;
        Assert.NotNull(targetUri);
        Assert.Contains("enterprise.okta.com/oauth2/v1/authorize", targetUri.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("code_challenge=", targetUri.Query, StringComparison.OrdinalIgnoreCase);

        // Extract state parameter
        string query = targetUri.Query;
        int stateIdx = query.IndexOf("state=", StringComparison.Ordinal);
        Assert.True(stateIdx >= 0);
        string stateVal = query[(stateIdx + "state=".Length)..];
        int ampIdx = stateVal.IndexOf('&', StringComparison.Ordinal);
        if (ampIdx >= 0) stateVal = stateVal[..ampIdx];
        string state = Uri.UnescapeDataString(stateVal);

        // 4. Simulate IdP Callback with mock authorization code mapping to GlobalAdmins
        string mockCode = "mock_code:admin@enterprise.com:Corp SuperAdmin:GlobalAdmins";
        var callbackReq = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri($"/auth/sso/oidc/callback?code={Uri.EscapeDataString(mockCode)}&state={Uri.EscapeDataString(state)}", UriKind.Relative));

        var callbackRes = await _client.SendAsync(callbackReq);
        Assert.Equal(HttpStatusCode.Redirect, callbackRes.StatusCode);

        // Verify Set-Cookie header contains symbolon_session
        var cookies = callbackRes.Headers.GetValues("Set-Cookie").ToList();
        Assert.NotEmpty(cookies);
        string? sessionCookie = cookies.FirstOrDefault(c => c.Contains("symbolon_session=", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(sessionCookie);
        Assert.Contains("HttpOnly", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sym_sso_", sessionCookie, StringComparison.OrdinalIgnoreCase);

        // Extract token from cookie
        int start = sessionCookie.IndexOf("symbolon_session=", StringComparison.Ordinal) + "symbolon_session=".Length;
        int end = sessionCookie.IndexOf(';', start);
        string token = end > start ? sessionCookie[start..end] : sessionCookie[start..];

        // 5. Use session token in cookie to access /auth/sso/me without API key
        var meReq = new HttpRequestMessage(HttpMethod.Get, new Uri("/auth/sso/me", UriKind.Relative));
        meReq.Headers.Add("Cookie", $"symbolon_session={token}");
        var meRes = await _client.SendAsync(meReq);
        Assert.Equal(HttpStatusCode.OK, meRes.StatusCode);

        var profile = await meRes.Content.ReadFromJsonAsync<SsoUserProfileDto>();
        Assert.NotNull(profile);
        Assert.True(profile.IsAuthenticated);
        Assert.Equal("Corp SuperAdmin", profile.UserName);
        Assert.Equal("admin@enterprise.com", profile.Email);
        Assert.Equal("admin:super", profile.Role);

        // 6. Use Bearer token authorization header with sym_sso_... to call Admin API
        var adminReq = new HttpRequestMessage(HttpMethod.Get, new Uri("/admin/v1/licenses", UriKind.Relative));
        adminReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var adminRes = await _client.SendAsync(adminReq);
        Assert.Equal(HttpStatusCode.OK, adminRes.StatusCode);

        // 7. Logout
        var logoutReq = new HttpRequestMessage(HttpMethod.Post, new Uri("/auth/sso/logout", UriKind.Relative));
        var logoutRes = await _client.SendAsync(logoutReq);
        Assert.Equal(HttpStatusCode.OK, logoutRes.StatusCode);

        // 8. Delete SSO Config
        var delRes = await _client.DeleteAsync(new Uri($"/admin/v1/sso/configs/{providerId}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NoContent, delRes.StatusCode);
    }

    [Fact]
    public async Task SamlFlow_Metadata_And_AcsAssertionProcessing_Succeeds()
    {
        // 1. Verify SAML SP Metadata endpoint
        var metaRes = await _client.GetAsync(new Uri("/auth/sso/saml/metadata", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, metaRes.StatusCode);
        string xml = await metaRes.Content.ReadAsStringAsync();
        Assert.Contains("md:EntityDescriptor", xml, StringComparison.Ordinal);
        Assert.Contains("/auth/sso/saml/acs", xml, StringComparison.Ordinal);

        // 2. Create SAML provider config
        string providerId = $"sso_saml_{Guid.NewGuid():N}";
        var samlConfig = new SsoProviderConfigDto
        {
            Id = providerId,
            TenantId = "ten_default",
            ProviderType = "saml",
            DisplayName = "Entra ID SAML SSO",
            Issuer = "https://sts.windows.net/tenant123/",
            ClientId = "entra_client_id",
            AuthorizationEndpoint = "https://login.microsoftonline.com/tenant123/saml2",
            RoleMappings = new Dictionary<string, string>
            {
                ["Auditors-Group"] = "auditor"
            },
            DefaultRole = "auditor",
            IsEnabled = true
        };

        var postRes = await _client.PostAsJsonAsync(new Uri("/admin/v1/sso/configs", UriKind.Relative), samlConfig);
        Assert.Equal(HttpStatusCode.OK, postRes.StatusCode);

        // 3. Prepare SAML Assertion response
        string host = _client.BaseAddress?.Authority ?? "localhost";
        string spEntity = $"http://{host}/auth/sso/saml/metadata";

        string samlXml = $"""
        <samlp:Response xmlns:samlp="urn:oasis:names:tc:SAML:2.0:protocol"
                        xmlns:saml="urn:oasis:names:tc:SAML:2.0:assertion"
                        ID="_saml_resp_99" Version="2.0" IssueInstant="2026-09-22T12:00:00Z">
            <samlp:Status>
                <samlp:StatusCode Value="urn:oasis:names:tc:SAML:2.0:status:Success"/>
            </samlp:Status>
            <saml:Assertion ID="_assertion_99" Version="2.0" IssueInstant="2026-09-22T12:00:00Z">
                <saml:Issuer>https://sts.windows.net/tenant123/</saml:Issuer>
                <saml:Subject>
                    <saml:NameID Format="urn:oasis:names:tc:SAML:1.1:nameid-format:emailAddress">auditor.jane@corp.com</saml:NameID>
                </saml:Subject>
                <saml:Conditions>
                    <saml:AudienceRestriction>
                        <saml:Audience>{spEntity}</saml:Audience>
                    </saml:AudienceRestriction>
                </saml:Conditions>
                <saml:AttributeStatement>
                    <saml:Attribute Name="displayName">
                        <saml:AttributeValue>Jane Auditor</saml:AttributeValue>
                    </saml:Attribute>
                    <saml:Attribute Name="groups">
                        <saml:AttributeValue>Auditors-Group</saml:AttributeValue>
                    </saml:Attribute>
                </saml:AttributeStatement>
            </saml:Assertion>
        </samlp:Response>
        """;

        string b64Response = Convert.ToBase64String(Encoding.UTF8.GetBytes(samlXml));

        // 4. POST to SAML ACS
        var formContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["SAMLResponse"] = b64Response
        });

        var acsRes = await _client.PostAsync(new Uri("/auth/sso/saml/acs", UriKind.Relative), formContent);
        Assert.Equal(HttpStatusCode.Redirect, acsRes.StatusCode);

        // Verify session cookie was set
        var cookies = acsRes.Headers.GetValues("Set-Cookie").ToList();
        string? cookie = cookies.FirstOrDefault(c => c.Contains("symbolon_session=", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(cookie);

        // 5. Clean up provider config
        await _client.DeleteAsync(new Uri($"/admin/v1/sso/configs/{providerId}", UriKind.Relative));
    }
}
