using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Symbolon.ControlPlane.Tests;

public sealed class WebUiTests : IClassFixture<ControlPlaneFactory>
{
    private readonly HttpClient _client;

    public WebUiTests(ControlPlaneFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Root_Url_Serves_Dashboard_IndexHtml()
    {
        var response = await _client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Symbolon Control Plane", content, StringComparison.Ordinal);
        Assert.Contains("view-overview", content, StringComparison.Ordinal);
        Assert.Contains("view-licenses", content, StringComparison.Ordinal);
        Assert.Contains("view-keys", content, StringComparison.Ordinal);
        Assert.Contains("view-wasm", content, StringComparison.Ordinal);
        Assert.Contains("view-migrate", content, StringComparison.Ordinal);
        Assert.Contains("symbolon-validator.js", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Static_Css_And_Js_Assets_Are_Served()
    {
        var cssResponse = await _client.GetAsync("/css/dashboard.css");
        Assert.Equal(HttpStatusCode.OK, cssResponse.StatusCode);
        string css = await cssResponse.Content.ReadAsStringAsync();
        Assert.Contains("--bg-primary", css, StringComparison.Ordinal);

        var jsResponse = await _client.GetAsync("/js/dashboard.js");
        Assert.Equal(HttpStatusCode.OK, jsResponse.StatusCode);
        string js = await jsResponse.Content.ReadAsStringAsync();
        Assert.Contains("refreshAllData", js, StringComparison.Ordinal);

        var retroCss = await _client.GetAsync("/css/retro-tui.css");
        Assert.Equal(HttpStatusCode.OK, retroCss.StatusCode);
        string retroCssContent = await retroCss.Content.ReadAsStringAsync();
        Assert.Contains("--retro-desktop", retroCssContent, StringComparison.Ordinal);

        var retroJs = await _client.GetAsync("/js/retro-tui.js");
        Assert.Equal(HttpStatusCode.OK, retroJs.StatusCode);
        string retroJsContent = await retroJs.Content.ReadAsStringAsync();
        Assert.Contains("bindKeyboardListeners", retroJsContent, StringComparison.Ordinal);

        var wasmJs = await _client.GetAsync("/js/symbolon-validator.js");
        Assert.Equal(HttpStatusCode.OK, wasmJs.StatusCode);
        string wasmJsContent = await wasmJs.Content.ReadAsStringAsync();
        Assert.Contains("SymbolonOfflineValidator", wasmJsContent, StringComparison.Ordinal);
        Assert.Contains("generateBrowserFingerprint", wasmJsContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Fallback_Route_Serves_IndexHtml()
    {
        var response = await _client.GetAsync("/dashboard");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Symbolon Control Plane", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task System_Telemetry_Endpoint_Returns_Valid_Resource_Metrics()
    {
        var response = await _client.GetAsync("/v1/system/telemetry");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var telemetry = await response.Content.ReadFromJsonAsync(
            Symbolon.Protocol.SymbolonProtocolJsonContext.Default.ServerTelemetryDto);

        Assert.NotNull(telemetry);
        Assert.True(telemetry.CpuPercent >= 0.0);
        Assert.True(telemetry.MemoryMb > 0.0);
        Assert.NotNull(telemetry.ServerIp);
        Assert.False(string.IsNullOrWhiteSpace(telemetry.CurrentUser));
    }
}
