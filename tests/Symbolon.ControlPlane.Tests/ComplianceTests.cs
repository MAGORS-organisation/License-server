using System.Net;
using System.Net.Http.Json;
using Symbolon.Protocol;
using Xunit;

namespace Symbolon.ControlPlane.Tests;

public sealed class ComplianceTests : IClassFixture<ControlPlaneFactory>
{
    private readonly HttpClient _client;

    public ComplianceTests(ControlPlaneFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Cra_Compliance_Endpoint_Returns_Valid_Report()
    {
        var res = await _client.GetAsync("/v1/compliance/cra");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var report = await res.Content.ReadFromJsonAsync<CraComplianceReportDto>();
        Assert.NotNull(report);
        Assert.Equal("Symbolon Floating License Server", report.ProductName);
        Assert.Equal("Compliant", report.ComplianceStatus);
        Assert.Equal("security@symbolon.dev", report.SecurityContact);
        Assert.NotEmpty(report.Standards);
        Assert.Contains(report.Standards, s => s.Contains("Cyber Resilience Act", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.Standards, s => s.Contains("ML-DSA-65", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(report.VulnerabilityReportingUri);
        Assert.True(report.PatchSupportUntil > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Sbom_Endpoint_Returns_Valid_CycloneDx_Document()
    {
        var res = await _client.GetAsync("/v1/compliance/sbom");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var sbom = await res.Content.ReadFromJsonAsync<CycloneDxSbomDto>();
        Assert.NotNull(sbom);
        Assert.Equal("CycloneDX", sbom.BomFormat);
        Assert.Equal("1.6", sbom.SpecVersion);
        Assert.NotEmpty(sbom.Components);

        Assert.Contains(sbom.Components, c => c.Name == "Symbolon.ControlPlane" && c.Licenses.Contains("AGPL-3.0-only"));
        Assert.Contains(sbom.Components, c => c.Name == "Symbolon.Crypto" && c.Licenses.Contains("Apache-2.0"));
        Assert.Contains(sbom.Components, c => c.Name == "Symbolon.Protocol" && c.Licenses.Contains("CC-BY-4.0"));
    }
}
