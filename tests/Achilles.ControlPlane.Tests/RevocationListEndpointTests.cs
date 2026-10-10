using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Achilles.Crypto;
using Achilles.Format;
using Xunit;

namespace Achilles.ControlPlane.Tests;

public sealed class RevocationListEndpointTests : IClassFixture<ControlPlaneFactory>
{
    private readonly HttpClient _client;
    public RevocationListEndpointTests(ControlPlaneFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetRevocationsLatest_ReturnsSignedSymrlAndClaims()
    {
        var res = await _client.GetAsync("/v1/revocations/latest");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        json.TryGetProperty("pem", out var pemProp).Should().BeTrue();
        json.TryGetProperty("claims", out var claimsProp).Should().BeTrue();

        string pem = pemProp.GetString()!;
        pem.Should().Contain("-----BEGIN SYMBOLON REVOCATION LIST-----");
        pem.Should().Contain("-----END SYMBOLON REVOCATION LIST-----");

        claimsProp.GetProperty("symrl").GetProperty("v").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task GetRevocationsSymrl_ReturnsRawPemMime()
    {
        var res = await _client.GetAsync("/v1/revocations/latest.symrl");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        res.Content.Headers.ContentType?.MediaType.Should().Be("application/x-pem-file");

        string pem = await res.Content.ReadAsStringAsync();
        pem.Should().Contain("-----BEGIN SYMBOLON REVOCATION LIST-----");
    }

    [Fact]
    public async Task AdminRevoke_CreatesRevocation_AndDeltaReturnsIt()
    {
        // 1. Get initial sequence
        var initRes = await _client.GetAsync("/v1/revocations/latest");
        var initJson = await initRes.Content.ReadFromJsonAsync<JsonElement>();
        long initSeq = initJson.GetProperty("claims").GetProperty("symrl").GetProperty("seq").GetInt64();

        // 2. Admin creates a machine revocation
        string machineId = $"fp_test_{Guid.NewGuid():N}";
        var revokeRes = await _client.PostAsJsonAsync("/admin/v1/revocations", new
        {
            subjectType = "machine",
            subjectId = machineId,
            reason = "Test fraud detection"
        });
        revokeRes.StatusCode.Should().Be(HttpStatusCode.Created);

        var revEntity = await revokeRes.Content.ReadFromJsonAsync<JsonElement>();
        long newSeq = revEntity.GetProperty("sequence").GetInt64();
        newSeq.Should().BeGreaterThan(initSeq);

        // 3. Query delta with since = initSeq
        var deltaRes = await _client.GetAsync($"/v1/revocations/latest?since={initSeq}");
        deltaRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var deltaJson = await deltaRes.Content.ReadFromJsonAsync<JsonElement>();

        var symrl = deltaJson.GetProperty("claims").GetProperty("symrl");
        symrl.GetProperty("full").GetBoolean().Should().BeFalse();
        symrl.GetProperty("since").GetInt64().Should().Be(initSeq);
        symrl.GetProperty("seq").GetInt64().Should().Be(newSeq);

        var revokedArray = symrl.GetProperty("revoked");
        bool found = false;
        foreach (var item in revokedArray.EnumerateArray())
        {
            if (item.GetProperty("id").GetString() == machineId)
            {
                item.GetProperty("t").GetString().Should().Be("machine");
                found = true;
                break;
            }
        }
        found.Should().BeTrue();
    }
}
