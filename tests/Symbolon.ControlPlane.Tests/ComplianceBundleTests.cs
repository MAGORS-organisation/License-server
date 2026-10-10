using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Symbolon.ControlPlane.Compliance;
using Symbolon.Crypto;
using Xunit;

namespace Symbolon.ControlPlane.Tests;

public sealed class ComplianceBundleTests : IClassFixture<ControlPlaneFactory>
{
    private readonly HttpClient _client;
    private const string AdminApiKey = "sym_adm_test_secret_key_12345";

    public ComplianceBundleTests(ControlPlaneFactory factory)
    {
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Add("X-Api-Key", AdminApiKey);
    }

    [Fact]
    public void ComplianceBundleService_Generates_Valid_Zip_With_All_Standards()
    {
        using var signer = Es256SignatureProvider.GenerateKey("test-compliance-key");
        var service = new ComplianceBundleService(signer);

        byte[] zipBytes = service.GenerateBundle(DateTimeOffset.UtcNow);
        zipBytes.Should().NotBeEmpty();

        using var ms = new MemoryStream(zipBytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

        var expectedEntries = new[]
        {
            "manifest.json",
            "audit_trail_merkle_verified.json",
            "sbom_cyclonedx.json",
            "soc2_iso27001_nis2_mapping.json",
            "pqc_readiness_assessment.json",
            "concurrency_trueup_report.json",
            "access_and_scim_audit.json",
            "checksums.sha256",
            "signature.pqc.sig"
        };

        foreach (string expected in expectedEntries)
        {
            zip.GetEntry(expected).Should().NotBeNull($"Entry '{expected}' must exist in compliance ZIP");
        }

        // Validate manifest contents
        var manifestEntry = zip.GetEntry("manifest.json")!;
        using (var manifestStream = manifestEntry.Open())
        using (var doc = JsonDocument.Parse(manifestStream))
        {
            var standards = doc.RootElement.GetProperty("standards");
            standards.GetArrayLength().Should().Be(5);
            standards.ToString().Should().Contain("SOC 2");
            standards.ToString().Should().Contain("ISO/IEC 27001");
            standards.ToString().Should().Contain("NIS 2");
        }

        // Validate checksums.sha256 matches actual entries
        var checksumEntry = zip.GetEntry("checksums.sha256")!;
        using (var r = new StreamReader(checksumEntry.Open(), Encoding.UTF8))
        {
            string line;
            while ((line = r.ReadLine()!) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split("  ", StringSplitOptions.RemoveEmptyEntries);
                parts.Length.Should().Be(2);

                string expectedHex = parts[0];
                string filename = parts[1];

                var fileEntry = zip.GetEntry(filename);
                fileEntry.Should().NotBeNull();

                using var fileStream = fileEntry!.Open();
                using var fileMs = new MemoryStream();
                fileStream.CopyTo(fileMs);
                byte[] actualHash = SHA256.HashData(fileMs.ToArray());
                Convert.ToHexStringLower(actualHash).Should().Be(expectedHex);
            }
        }
    }

    [Fact]
    public async Task Compliance_Bundle_Endpoint_Returns_Valid_Zip_Download()
    {
        var response = await _client.GetAsync("/v1/compliance/bundle");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/zip");

        byte[] bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Length.Should().BeGreaterThan(500);

        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        zip.Entries.Should().HaveCount(9);
    }

    [Fact]
    public async Task Hsm_Status_Endpoints_Return_Healthy_Status()
    {
        var res1 = await _client.GetAsync("/admin/v1/kms/hsm-status");
        res1.StatusCode.Should().Be(HttpStatusCode.OK);

        var res2 = await _client.GetAsync("/admin/v1/keys/hsm-status");
        res2.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = await JsonDocument.ParseAsync(await res1.Content.ReadAsStreamAsync());
        doc.RootElement.GetProperty("isHealthy").GetBoolean().Should().BeTrue();
    }
}
