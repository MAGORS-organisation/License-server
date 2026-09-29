using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Symbolon.ControlPlane.Models;
using Symbolon.Protocol;
using Symbolon.Protocol.Reporting;
using Xunit;

namespace Symbolon.ControlPlane.Tests;

public sealed class ReportingApiTests : IClassFixture<ControlPlaneFactory>
{
    private readonly HttpClient _client;

    public ReportingApiTests(ControlPlaneFactory factory)
    {
        _client = factory.CreateClient();
    }

    private async Task<(string TenantId, string LicenseId, string LicenseKey)> CreateTestLicenseAsync(int seats = 2)
    {
        var tenantRes = await _client.PostAsJsonAsync("/admin/v1/tenants", new CreateTenantDto($"tenant-{Guid.NewGuid():N}", "Reporting Corp"));
        var tenant = await tenantRes.Content.ReadFromJsonAsync<TenantDto>();

        var prodReq = new HttpRequestMessage(HttpMethod.Post, "/admin/v1/products")
        {
            Content = JsonContent.Create(new CreateProductDto($"prod-{Guid.NewGuid():N}", "Reporting CAD", ["windows"]))
        };
        prodReq.Headers.Add("X-Tenant-Id", tenant!.Id);
        var prodRes = await _client.SendAsync(prodReq);
        var product = await prodRes.Content.ReadFromJsonAsync<ProductDto>();

        var polRes = await _client.PostAsJsonAsync("/admin/v1/policies", new CreatePolicyDto
        {
            ProductId = product!.Id,
            Code = $"policy-{Guid.NewGuid():N}",
            Name = "Floating Policy",
            MaxSeats = seats,
            LicenseModel = "floating",
            OverageStrategy = "allow-20pct"
        });
        var policy = await polRes.Content.ReadFromJsonAsync<PolicyDto>();

        var licRes = await _client.PostAsJsonAsync("/admin/v1/licenses", new CreateLicenseDto
        {
            PolicyId = policy!.Id,
            MaxSeats = seats
        });
        var license = await licRes.Content.ReadFromJsonAsync<LicenseResponseDto>();

        return (tenant.Id, license!.Id, license.LicenseKey!);
    }

    [Fact]
    public async Task Concurrency_Timeline_Endpoint_Calculates_Bucket_Peaks_And_Averages()
    {
        var (tenantId, licenseId, key) = await CreateTestLicenseAsync(seats: 3);

        // Checkout 1 seat
        var chkRes = await _client.PostAsJsonAsync("/v1/leases", new CheckoutRequestDto
        {
            LicenseKey = key,
            FingerprintComponents = new Dictionary<string, string> { ["machineId"] = "workstation-01" }
        });
        Assert.Equal(HttpStatusCode.OK, chkRes.StatusCode);
        var lease = await chkRes.Content.ReadFromJsonAsync<CheckoutResponseDto>();
        Assert.NotNull(lease);

        // 1. Concurrency report via query parameter bucket=hour
        var reportRes = await _client.GetAsync($"/admin/v1/reports/concurrency?bucket=hour&licenseId={licenseId}");
        Assert.Equal(HttpStatusCode.OK, reportRes.StatusCode);

        var timeline = await reportRes.Content.ReadFromJsonAsync<ConcurrencyAnalyticsResponseDto>(SymbolonProtocolJsonContext.Default.ConcurrencyAnalyticsResponseDto);
        Assert.NotNull(timeline);
        Assert.True(timeline.OverallPeak >= 1);
        Assert.True(timeline.TotalCheckouts >= 1);
        Assert.NotEmpty(timeline.Buckets);

        // 2. Concurrency timeline endpoint explicitly
        var directTimelineRes = await _client.GetAsync($"/admin/v1/reports/concurrency/timeline?bucket=day&licenseId={licenseId}");
        Assert.Equal(HttpStatusCode.OK, directTimelineRes.StatusCode);
        var dayTimeline = await directTimelineRes.Content.ReadFromJsonAsync<ConcurrencyAnalyticsResponseDto>(SymbolonProtocolJsonContext.Default.ConcurrencyAnalyticsResponseDto);
        Assert.NotNull(dayTimeline);
        Assert.Equal("DAY", dayTimeline.BucketSize);
    }

    [Fact]
    public async Task TrueUp_Report_And_Csv_Export_Endpoints_Work()
    {
        var (tenantId, licenseId, key) = await CreateTestLicenseAsync(seats: 5);

        // Checkout a seat to produce an audit record
        var chkRes = await _client.PostAsJsonAsync("/v1/leases", new CheckoutRequestDto
        {
            LicenseKey = key,
            FingerprintComponents = new Dictionary<string, string> { ["machineId"] = "workstation-alpha" }
        });
        Assert.Equal(HttpStatusCode.OK, chkRes.StatusCode);

        // 1. JSON report
        var jsonRes = await _client.GetAsync($"/admin/v1/reports/true-up?licenseId={licenseId}");
        Assert.Equal(HttpStatusCode.OK, jsonRes.StatusCode);
        var report = await jsonRes.Content.ReadFromJsonAsync<TrueUpReportDto>(SymbolonProtocolJsonContext.Default.TrueUpReportDto);
        Assert.NotNull(report);
        Assert.Equal(licenseId, report.LicenseId);
        Assert.Equal(5, report.LicensedSeats);
        Assert.True(report.PeakConcurrentSeats >= 1);
        Assert.True(report.ContractCompliance == "Compliant" || report.ContractCompliance == "OverageWarning" || report.ContractCompliance == "NonCompliant");

        // 2. CSV export
        var csvRes = await _client.GetAsync($"/admin/v1/reports/true-up/export?licenseId={licenseId}&format=csv");
        Assert.Equal(HttpStatusCode.OK, csvRes.StatusCode);
        Assert.Contains("text/csv", csvRes.Content.Headers.ContentType?.MediaType ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        string csvContent = await csvRes.Content.ReadAsStringAsync();
        Assert.Contains("TenantId,LicenseId,ProductName,LicensedSeats", csvContent, StringComparison.Ordinal);
        Assert.Contains(licenseId, csvContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Denials_Report_Endpoint_Returns_Spike_Analytics()
    {
        var (tenantId, licenseId, key) = await CreateTestLicenseAsync(seats: 1);

        // 1. Exhaust the pool
        var chk1 = await _client.PostAsJsonAsync("/v1/leases", new CheckoutRequestDto
        {
            LicenseKey = key,
            FingerprintComponents = new Dictionary<string, string> { ["machineId"] = "ws-1" }
        });
        Assert.Equal(HttpStatusCode.OK, chk1.StatusCode);

        // 2. Second checkout gets denied (FLT-39 audit record generated)
        var chk2 = await _client.PostAsJsonAsync("/v1/leases", new CheckoutRequestDto
        {
            LicenseKey = key,
            FingerprintComponents = new Dictionary<string, string> { ["machineId"] = "ws-2" }
        });
        Assert.Equal(HttpStatusCode.Conflict, chk2.StatusCode);

        // 3. Query denials report
        var denialsRes = await _client.GetAsync($"/admin/v1/reports/denials?licenseId={licenseId}");
        Assert.Equal(HttpStatusCode.OK, denialsRes.StatusCode);

        var denials = await denialsRes.Content.ReadFromJsonAsync<DenialsAnalyticsResponseDto>(SymbolonProtocolJsonContext.Default.DenialsAnalyticsResponseDto);
        Assert.NotNull(denials);
        Assert.True(denials.TotalDenials >= 1);
        Assert.NotEmpty(denials.DenialsByReason);
    }

    [Fact]
    public async Task Audit_Verify_Integrity_Endpoint_Validates_Cryptographic_Chain()
    {
        // Issue checkout to guarantee audit events exist in the database
        var (tenantId, licenseId, key) = await CreateTestLicenseAsync(seats: 2);
        await _client.PostAsJsonAsync("/v1/leases", new CheckoutRequestDto
        {
            LicenseKey = key,
            FingerprintComponents = new Dictionary<string, string> { ["machineId"] = "ws-audit-integrity" }
        });

        var verifyRes = await _client.PostAsync("/admin/v1/reports/audit/verify-integrity", null);
        Assert.Equal(HttpStatusCode.OK, verifyRes.StatusCode);

        var proof = await verifyRes.Content.ReadFromJsonAsync<AuditVerificationProofDto>(SymbolonProtocolJsonContext.Default.AuditVerificationProofDto);
        Assert.NotNull(proof);
        Assert.True(proof.IsChainIntact);
        Assert.True(proof.TotalEventsVerified >= 1);
        Assert.NotEmpty(proof.RootHashHex);
        Assert.Null(proof.TamperedEventId);
    }
}
