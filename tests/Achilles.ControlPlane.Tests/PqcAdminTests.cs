using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Achilles.ControlPlane.Models;
using Achilles.Crypto;
using Achilles.Domain.Pqc;
using Xunit;

namespace Achilles.ControlPlane.Tests;

public sealed class PqcAdminTests : IClassFixture<ControlPlaneFactory>
{
    private readonly HttpClient _client;

    public PqcAdminTests(ControlPlaneFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task AdminApi_GetPqcReadiness_Returns200AndValidReport()
    {
        var res = await _client.GetAsync(new Uri("/admin/v1/pqc/readiness", UriKind.Relative));
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var report = await res.Content.ReadFromJsonAsync<PqcReadinessReport>();
        report.Should().NotBeNull();
        report!.ReadinessScorePercent.Should().BeGreaterOrEqualTo(0.0).And.BeLessOrEqualTo(100.0);
        report.TotalKeysScanned.Should().BeGreaterThan(0);
        report.KeyAudits.Should().NotBeEmpty();
        report.Summary.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task AdminApi_SetPqcProfile_SwitchesProfileSuccessfully()
    {
        // Switch to pqc-strict
        var resStrict = await _client.PostAsJsonAsync(
            new Uri("/admin/v1/pqc/profile", UriKind.Relative),
            new SetPqcProfileDto(PqcProfiles.PqcStrict));
        resStrict.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify updated readiness report reflects pqc-strict
        var repRes = await _client.GetAsync(new Uri("/admin/v1/pqc/readiness", UriKind.Relative));
        var report = await repRes.Content.ReadFromJsonAsync<PqcReadinessReport>();
        report!.ActiveProfile.Should().Be(PqcProfiles.PqcStrict);

        // Reset back to hybrid-v1
        var resHybrid = await _client.PostAsJsonAsync(
            new Uri("/admin/v1/pqc/profile", UriKind.Relative),
            new SetPqcProfileDto(PqcProfiles.HybridV1));
        resHybrid.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AdminApi_EncryptDecryptPqcEnvelope_RoundTrip()
    {
        if (!MlKemKeyEncapsulationProvider.IsSupported)
        {
            return;
        }

        string originalText = "Quantum-Safe-License-Secret-Payload-2026";
        string base64Data = Convert.ToBase64String(Encoding.UTF8.GetBytes(originalText));

        // 1. Encrypt payload
        var encRes = await _client.PostAsJsonAsync(
            new Uri("/admin/v1/pqc/encrypt", UriKind.Relative),
            new PqcEncryptRequestDto("cp-kem-key", base64Data));
        encRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var envelope = await encRes.Content.ReadFromJsonAsync<PqcEncryptedEnvelope>();
        envelope.Should().NotBeNull();
        envelope!.Ciphertext.Should().NotBeNullOrWhiteSpace();

        // 2. Decrypt envelope
        var decRes = await _client.PostAsJsonAsync(
            new Uri("/admin/v1/pqc/decrypt", UriKind.Relative),
            new PqcDecryptRequestDto(envelope));
        decRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var decResult = await decRes.Content.ReadFromJsonAsync<PqcDecryptResponseDto>();
        decResult.Should().NotBeNull();

        string decryptedText = Encoding.UTF8.GetString(Convert.FromBase64String(decResult!.PlaintextBase64));
        decryptedText.Should().Be(originalText);
    }
}
