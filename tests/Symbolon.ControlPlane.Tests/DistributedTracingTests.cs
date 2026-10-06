using System.Net;
using System.Net.Http.Json;
using Symbolon.ControlPlane.Models;
using Symbolon.Protocol;
using Symbolon.Protocol.Tracing;
using Xunit;

namespace Symbolon.ControlPlane.Tests;

public sealed class DistributedTracingTests : IClassFixture<ControlPlaneFactory>
{
    private readonly HttpClient _client;

    public DistributedTracingTests(ControlPlaneFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetRecentTraces_ReturnsOkAndJsonList()
    {
        var res = await _client.GetAsync("/admin/v1/traces/recent");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var spans = await res.Content.ReadFromJsonAsync<List<TraceSpanDto>>();
        Assert.NotNull(spans);
    }

    [Fact]
    public async Task Checkout_And_Borrow_RecordSpansInTraceBuffer()
    {
        // 1. Vytvorenie licencie
        string slug = $"trace-{Guid.NewGuid():N}";
        var tenantRes = await _client.PostAsJsonAsync("/admin/v1/tenants", new CreateTenantDto(slug, "Tracing Corp"));
        Assert.Equal(HttpStatusCode.Created, tenantRes.StatusCode);
        var tenant = await tenantRes.Content.ReadFromJsonAsync<TenantDto>();
        Assert.NotNull(tenant);

        var prodReq = new HttpRequestMessage(HttpMethod.Post, "/admin/v1/products")
        {
            Content = JsonContent.Create(new CreateProductDto($"app-tr-{Guid.NewGuid():N}"[..12], "TracingApp", ["core"]))
        };
        prodReq.Headers.Add("X-Tenant-Id", tenant.Id);
        var prodRes = await _client.SendAsync(prodReq);
        Assert.Equal(HttpStatusCode.Created, prodRes.StatusCode);
        var product = await prodRes.Content.ReadFromJsonAsync<ProductDto>();
        Assert.NotNull(product);

        var polRes = await _client.PostAsJsonAsync("/admin/v1/policies", new CreatePolicyDto
        {
            ProductId = product.Id,
            Code = "floating-trace-10",
            Name = "Floating 10 seats",
            MaxSeats = 10,
            LicenseModel = "floating"
        });
        Assert.Equal(HttpStatusCode.Created, polRes.StatusCode);
        var policy = await polRes.Content.ReadFromJsonAsync<PolicyDto>();
        Assert.NotNull(policy);

        var licRes = await _client.PostAsJsonAsync("/admin/v1/licenses", new CreateLicenseDto
        {
            PolicyId = policy.Id,
            CustomerRef = "CUST-TRACE-01",
            MaxSeats = 10
        });
        Assert.Equal(HttpStatusCode.Created, licRes.StatusCode);
        var license = await licRes.Content.ReadFromJsonAsync<LicenseResponseDto>();
        Assert.NotNull(license);
        Assert.NotNull(license.LicenseKey);

        // 2. Checkout s W3C traceparent hlavičkou
        string traceId = "4bf92f3577b34da6a3ce929d0e0e4736";
        string spanId = "00f067aa0ba902b7";
        string incomingTraceparent = $"00-{traceId}-{spanId}-01";

        var checkoutReq = new HttpRequestMessage(HttpMethod.Post, "/v1/leases")
        {
            Content = JsonContent.Create(new CheckoutRequestDto
            {
                LicenseKey = license.LicenseKey,
                Quantity = 1,
                FingerprintComponents = new Dictionary<string, string> { ["smbios"] = "uuid-trace-1", ["os"] = "linux" }
            })
        };
        checkoutReq.Headers.Add("traceparent", incomingTraceparent);

        var checkoutRes = await _client.SendAsync(checkoutReq);
        Assert.Equal(HttpStatusCode.OK, checkoutRes.StatusCode);

        var checkoutBody = await checkoutRes.Content.ReadFromJsonAsync<CheckoutResponseDto>();
        Assert.NotNull(checkoutBody);

        // 3. Overenie prítomnosti stop vo vyrovnávacej pamäti traces
        var tracesRes = await _client.GetAsync("/admin/v1/traces/recent");
        Assert.Equal(HttpStatusCode.OK, tracesRes.StatusCode);
        var traces = await tracesRes.Content.ReadFromJsonAsync<List<TraceSpanDto>>();
        Assert.NotNull(traces);
        Assert.NotEmpty(traces);

        var checkoutSpan = traces.FirstOrDefault(s => s.Name == SymbolonTracing.OpCheckout && s.TraceId == traceId);
        Assert.NotNull(checkoutSpan);
        Assert.Equal("OK", checkoutSpan.Status);
        Assert.True(checkoutSpan.DurationMs >= 0);

        // 4. Overenie prítomnosti fraud check stopy
        var fraudSpan = traces.FirstOrDefault(s => s.Name == SymbolonTracing.OpFraudCheck && s.TraceId == traceId);
        Assert.NotNull(fraudSpan);
        Assert.Equal("OK", fraudSpan.Status);
    }
}
