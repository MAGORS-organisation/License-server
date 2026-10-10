using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Achilles.ControlPlane.Models;
using Achilles.Crypto;
using Achilles.Protocol;
using Xunit;

namespace Achilles.ControlPlane.Tests;

public sealed class SecurityTests : IClassFixture<ControlPlaneFactory>
{
    private readonly HttpClient _client;

    public SecurityTests(ControlPlaneFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Relay_Endpoints_Require_Authentication()
    {
        // 1. Without credentials -> 401 Unauthorized
        var unauthReq = new HttpRequestMessage(HttpMethod.Post, "/relay/v1/grants:request")
        {
            Content = JsonContent.Create(new RequestSeatGrantDto("rly_fake", "lic_fake", 1, 10))
        };
        var unauthRes = await _client.SendAsync(unauthReq);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthRes.StatusCode);

        // 2. With invalid API key -> 401 Unauthorized
        var badKeyReq = new HttpRequestMessage(HttpMethod.Post, "/relay/v1/grants:request")
        {
            Content = JsonContent.Create(new RequestSeatGrantDto("rly_fake", "lic_fake", 1, 10))
        };
        badKeyReq.Headers.Add("X-Relay-Api-Key", "invalid_api_key_123");
        var badKeyRes = await _client.SendAsync(badKeyReq);
        Assert.Equal(HttpStatusCode.Unauthorized, badKeyRes.StatusCode);
    }

    [Fact]
    public async Task Relay_Cannot_Impersonate_Different_RelayId()
    {
        // Register Relay A
        var regA = await _client.PostAsJsonAsync("/relay/v1/register", new RegisterRelayDto("Relay A", null));
        Assert.Equal(HttpStatusCode.Created, regA.StatusCode);
        var relayA = await regA.Content.ReadFromJsonAsync<RegisterRelayResponseDto>();
        Assert.NotNull(relayA);

        // Use Relay A's API Key to request grant for Relay B
        var impReq = new HttpRequestMessage(HttpMethod.Post, "/relay/v1/grants:request")
        {
            Content = JsonContent.Create(new RequestSeatGrantDto("rly_different", "lic_fake", 1, 10))
        };
        impReq.Headers.Add("X-Relay-Api-Key", relayA.ApiKey);
        var impRes = await _client.SendAsync(impReq);
        Assert.Equal(HttpStatusCode.Forbidden, impRes.StatusCode);
    }

    [Fact]
    public async Task KeyRotation_And_Revocation_Lifecycle_Works_Correctly()
    {
        // 1. Initial JWKS contains at least 1 active key
        var initJwksRes = await _client.GetAsync("/v1/.well-known/symbolon-keys");
        Assert.Equal(HttpStatusCode.OK, initJwksRes.StatusCode);
        var initJwks = await initJwksRes.Content.ReadFromJsonAsync<JsonWebKeySetDto>();
        Assert.NotNull(initJwks);
        Assert.NotEmpty(initJwks.Keys);

        // 2. Rotate Key via Admin API
        var rotateRes = await _client.PostAsJsonAsync("/admin/v1/keys/rotate", new RotateKeyDto("default", "ES256"));
        Assert.Equal(HttpStatusCode.Created, rotateRes.StatusCode);

        // 3. JWKS now contains the newly rotated key
        var postRotateJwksRes = await _client.GetAsync("/v1/.well-known/symbolon-keys");
        Assert.Equal(HttpStatusCode.OK, postRotateJwksRes.StatusCode);
        var postRotateJwks = await postRotateJwksRes.Content.ReadFromJsonAsync<JsonWebKeySetDto>();
        Assert.NotNull(postRotateJwks);
        Assert.True(postRotateJwks.Keys.Count >= 2, "JWKS should contain both active and deprecated keys for verification overlap.");

        // 4. List keys via Admin API
        var listRes = await _client.GetAsync("/admin/v1/keys");
        Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);
        var keysList = await listRes.Content.ReadFromJsonAsync<List<SigningKeyDto>>();
        Assert.NotNull(keysList);
        Assert.Contains(keysList, k => k.State == "active");
        Assert.Contains(keysList, k => k.State == "deprecated");

        // 5. Revoke a deprecated key
        var keyToRevoke = keysList.First(k => k.State == "deprecated");
        var revokeRes = await _client.PostAsJsonAsync($"/admin/v1/keys/{keyToRevoke.Kid}/revoke", new RevokeKeyDto("Key compromise simulation"));
        Assert.Equal(HttpStatusCode.OK, revokeRes.StatusCode);

        // 6. Verify revoked key is EXCLUDED from public JWKS
        var finalJwksRes = await _client.GetAsync("/v1/.well-known/symbolon-keys");
        var finalJwks = await finalJwksRes.Content.ReadFromJsonAsync<JsonWebKeySetDto>();
        Assert.NotNull(finalJwks);
        Assert.DoesNotContain(finalJwks.Keys, k => k.Kid == keyToRevoke.Kid);

        // 7. Verify revocation appears in public revocation list
        var revListRes = await _client.GetAsync("/v1/revocations/latest");
        Assert.Equal(HttpStatusCode.OK, revListRes.StatusCode);
        string revContent = await revListRes.Content.ReadAsStringAsync();
        Assert.Contains(keyToRevoke.Kid, revContent, StringComparison.Ordinal);
    }
}
