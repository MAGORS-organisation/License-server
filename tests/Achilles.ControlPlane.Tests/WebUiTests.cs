using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Achilles.ControlPlane.Tests;

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

        string content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Achilles Control Plane", content, StringComparison.Ordinal);
        Assert.Contains("view-overview", content, StringComparison.Ordinal);
        Assert.Contains("view-licenses", content, StringComparison.Ordinal);
        Assert.Contains("view-keys", content, StringComparison.Ordinal);
        Assert.Contains("view-wasm", content, StringComparison.Ordinal);
        Assert.Contains("view-migrate", content, StringComparison.Ordinal);
        Assert.Contains("view-config", content, StringComparison.Ordinal);
        Assert.Contains("topbar-lang-switcher", content, StringComparison.Ordinal);
        Assert.Contains("topbar-theme-switcher", content, StringComparison.Ordinal);
        Assert.Contains("theme-card-cyberpunk", content, StringComparison.Ordinal);
        Assert.Contains("theme-card-apple", content, StringComparison.Ordinal);
        Assert.Contains("apple-regime-segmented", content, StringComparison.Ordinal);
        Assert.Contains("data-lang=\"de\"", content, StringComparison.Ordinal);
        Assert.Contains("lang-card-de", content, StringComparison.Ordinal);
        Assert.Contains("i18n.js", content, StringComparison.Ordinal);
        Assert.Contains("theme-manager.js", content, StringComparison.Ordinal);
        Assert.Contains("achilles-validator.js", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Static_Css_And_Js_Assets_Are_Served()
    {
        var cssResponse = await _client.GetAsync("/css/dashboard.css");
        Assert.Equal(HttpStatusCode.OK, cssResponse.StatusCode);
        string css = await cssResponse.Content.ReadAsStringAsync();
        Assert.Contains("--bg-primary", css, StringComparison.Ordinal);
        Assert.Contains(".lang-switcher", css, StringComparison.Ordinal);

        var themesCssResponse = await _client.GetAsync("/css/themes.css");
        Assert.Equal(HttpStatusCode.OK, themesCssResponse.StatusCode);
        string themesCss = await themesCssResponse.Content.ReadAsStringAsync();
        Assert.Contains("data-theme=\"cyberpunk\"", themesCss, StringComparison.Ordinal);
        Assert.Contains("data-theme=\"apple\"", themesCss, StringComparison.Ordinal);
        Assert.Contains("data-apple-effective-theme", themesCss, StringComparison.Ordinal);
        Assert.Contains(".segmented-control", themesCss, StringComparison.Ordinal);

        var jsResponse = await _client.GetAsync("/js/dashboard.js");
        Assert.Equal(HttpStatusCode.OK, jsResponse.StatusCode);
        string js = await jsResponse.Content.ReadAsStringAsync();
        Assert.Contains("refreshAllData", js, StringComparison.Ordinal);

        var themeManagerJs = await _client.GetAsync("/js/theme-manager.js");
        Assert.Equal(HttpStatusCode.OK, themeManagerJs.StatusCode);
        string themeManagerContent = await themeManagerJs.Content.ReadAsStringAsync();
        Assert.Contains("setAppTheme", themeManagerContent, StringComparison.Ordinal);
        Assert.Contains("setAppleRegime", themeManagerContent, StringComparison.Ordinal);
        Assert.Contains("cyberpunk", themeManagerContent, StringComparison.Ordinal);
        Assert.Contains("apple", themeManagerContent, StringComparison.Ordinal);

        var i18nJs = await _client.GetAsync("/js/i18n.js");
        Assert.Equal(HttpStatusCode.OK, i18nJs.StatusCode);
        string i18nJsContent = await i18nJs.Content.ReadAsStringAsync();
        Assert.Contains("translations", i18nJsContent, StringComparison.Ordinal);
        Assert.Contains("de:", i18nJsContent, StringComparison.Ordinal);
        Assert.Contains("setLanguage", i18nJsContent, StringComparison.Ordinal);
        Assert.Contains("getLanguage", i18nJsContent, StringComparison.Ordinal);
        Assert.Contains("config.themeCyberpunkTitle", i18nJsContent, StringComparison.Ordinal);
        Assert.Contains("config.themeAppleTitle", i18nJsContent, StringComparison.Ordinal);

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
        Assert.Contains("Achilles Control Plane", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task System_Telemetry_Endpoint_Returns_Valid_Resource_Metrics()
    {
        var response = await _client.GetAsync("/v1/system/telemetry");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var telemetry = await response.Content.ReadFromJsonAsync(
            Achilles.Protocol.AchillesProtocolJsonContext.Default.ServerTelemetryDto);

        Assert.NotNull(telemetry);
        Assert.True(telemetry.CpuPercent >= 0.0);
        Assert.True(telemetry.MemoryMb > 0.0);
        Assert.NotNull(telemetry.ServerIp);
        Assert.False(string.IsNullOrWhiteSpace(telemetry.CurrentUser));
    }
}
