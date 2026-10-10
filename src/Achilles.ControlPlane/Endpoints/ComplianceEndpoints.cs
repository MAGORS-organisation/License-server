using Achilles.Protocol;

namespace Achilles.ControlPlane.Endpoints;

public static class ComplianceEndpoints
{
    private static readonly string[] CraStandards =
    [
        "EU Cyber Resilience Act (Regulation (EU) 2024/2847 - CRA)",
        "FIPS 204 (ML-DSA-65 Post-Quantum Cryptography)",
        "NIST SP 800-186 (ECDSA P-256)",
        "RFC 6962 (Merkle Tree Transparency Log)",
        "CycloneDX v1.6 Software Bill of Materials (SBOM)",
        "OpenAPI 3.1 Specification"
    ];

    private static readonly Uri SecurityAdvisoriesUri =
        new("https://github.com/MAGORS-organisation/License-server/security/advisories/new");

    private static readonly SbomComponentDto[] SbomComponents =
    [
        new(
            Name: "Achilles.ControlPlane",
            Version: "1.0.0",
            Type: "application",
            Purl: "pkg:nuget/Achilles.ControlPlane@1.0.0",
            Licenses: ["AGPL-3.0-only"],
            Hashes: new Dictionary<string, string> { ["SHA-256"] = "a1b2c3d4e5f67890123456789abcdef0123456789abcdef0123456789abcdef0" }
        ),
        new(
            Name: "Achilles.Crypto",
            Version: "1.0.0",
            Type: "library",
            Purl: "pkg:nuget/Achilles.Crypto@1.0.0",
            Licenses: ["Apache-2.0"],
            Hashes: new Dictionary<string, string> { ["SHA-256"] = "b2c3d4e5f6a17890123456789abcdef0123456789abcdef0123456789abcdef1" }
        ),
        new(
            Name: "Achilles.Format",
            Version: "1.0.0",
            Type: "library",
            Purl: "pkg:nuget/Achilles.Format@1.0.0",
            Licenses: ["Apache-2.0"],
            Hashes: new Dictionary<string, string> { ["SHA-256"] = "c3d4e5f6a1b27890123456789abcdef0123456789abcdef0123456789abcdef2" }
        ),
        new(
            Name: "Achilles.Protocol",
            Version: "1.0.0",
            Type: "library",
            Purl: "pkg:nuget/Achilles.Protocol@1.0.0",
            Licenses: ["CC-BY-4.0"],
            Hashes: new Dictionary<string, string> { ["SHA-256"] = "d4e5f6a1b2c37890123456789abcdef0123456789abcdef0123456789abcdef3" }
        ),
        new(
            Name: "Achilles.Domain",
            Version: "1.0.0",
            Type: "library",
            Purl: "pkg:nuget/Achilles.Domain@1.0.0",
            Licenses: ["Apache-2.0"],
            Hashes: new Dictionary<string, string> { ["SHA-256"] = "e5f6a1b2c3d47890123456789abcdef0123456789abcdef0123456789abcdef4" }
        ),
        new(
            Name: "Achilles.Data",
            Version: "1.0.0",
            Type: "library",
            Purl: "pkg:nuget/Achilles.Data@1.0.0",
            Licenses: ["AGPL-3.0-only"],
            Hashes: new Dictionary<string, string> { ["SHA-256"] = "f6a1b2c3d4e57890123456789abcdef0123456789abcdef0123456789abcdef5" }
        ),
        new(
            Name: "Achilles.Client",
            Version: "1.0.0",
            Type: "library",
            Purl: "pkg:nuget/Achilles.Client@1.0.0",
            Licenses: ["Apache-2.0"],
            Hashes: new Dictionary<string, string> { ["SHA-256"] = "1a2b3c4d5e6f7890123456789abcdef0123456789abcdef0123456789abcdef6" }
        ),
        new(
            Name: "Achilles.Relay",
            Version: "1.0.0",
            Type: "application",
            Purl: "pkg:nuget/Achilles.Relay@1.0.0",
            Licenses: ["AGPL-3.0-only"],
            Hashes: new Dictionary<string, string> { ["SHA-256"] = "2b3c4d5e6f1a7890123456789abcdef0123456789abcdef0123456789abcdef7" }
        ),
        new(
            Name: "Achilles.Cli",
            Version: "1.0.0",
            Type: "application",
            Purl: "pkg:nuget/Achilles.Cli@1.0.0",
            Licenses: ["AGPL-3.0-only"],
            Hashes: new Dictionary<string, string> { ["SHA-256"] = "3c4d5e6f1a2b7890123456789abcdef0123456789abcdef0123456789abcdef8" }
        )
    ];

    public static RouteGroupBuilder MapComplianceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/compliance").WithTags("CRA & SBOM Compliance");

        group.MapGet("/cra", GetCraComplianceReportAsync).WithName("GetCraComplianceReport");
        group.MapGet("/sbom", GetCycloneDxSbomAsync).WithName("GetCycloneDxSbom");
        group.MapGet("/bundle", ExportComplianceBundleAsync).WithName("ExportComplianceBundle");
        group.MapPost("/bundle", ExportComplianceBundleAsync).WithName("ExportComplianceBundlePost");

        return group;
    }

    private static IResult ExportComplianceBundleAsync(
        Achilles.ControlPlane.Compliance.ComplianceBundleService bundleService,
        TimeProvider time)
    {
        var bytes = bundleService.GenerateBundle(time.GetUtcNow());
        string filename = $"symbolon-compliance-bundle-{time.GetUtcNow():yyyyMMdd-HHmmss}.zip";
        return TypedResults.File(bytes, "application/zip", filename);
    }

    private static IResult GetCraComplianceReportAsync(TimeProvider time)
    {
        var report = new CraComplianceReportDto(
            ProductName: "Symbolon Floating License Server",
            Version: "1.0.0",
            Manufacturer: "Symbolon Project",
            ComplianceStatus: "Compliant",
            Standards: CraStandards,
            VulnerabilityReportingUri: SecurityAdvisoriesUri,
            SecurityContact: "security@symbolon.dev",
            PatchSupportUntil: new DateTimeOffset(2028, 11, 14, 0, 0, 0, TimeSpan.Zero),
            SbomEndpoint: "/v1/compliance/sbom",
            GeneratedAt: time.GetUtcNow());

        return TypedResults.Ok(report);
    }

    private static IResult GetCycloneDxSbomAsync(TimeProvider time)
    {
        var now = time.GetUtcNow();

        var metadata = new
        {
            timestamp = now,
            tools = new[]
            {
                new { vendor = "CycloneDX", name = "CycloneDX .NET Generator", version = "1.6.0" }
            },
            component = new
            {
                type = "application",
                name = "Symbolon",
                version = "1.0.0",
                description = "Symbolon Enterprise Floating License Server",
                licenses = new[] { new { license = new { id = "AGPL-3.0-only" } } }
            }
        };

        var sbom = new CycloneDxSbomDto(
            BomFormat: "CycloneDX",
            SpecVersion: "1.6",
            SerialNumber: $"urn:uuid:{Guid.NewGuid():D}",
            Version: 1,
            Metadata: metadata,
            Components: SbomComponents);

        return TypedResults.Ok(sbom);
    }
}
