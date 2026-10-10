using System.Net;
using System.Text.Json;
using FluentAssertions;
using Achilles.Cli.Commands;
using Xunit;

namespace Achilles.Cli.Tests;

public sealed class DoctorCliTests
{
    [Fact]
    public async Task Doctor_DefaultRun_ReturnsZero()
    {
        int exitCode = await Program.Main(["doctor"]);
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task Doctor_Help_ReturnsZero()
    {
        int exitCode = await Program.Main(["doctor", "--help"]);
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task Doctor_Json_OutputsValidJsonReport()
    {
        using var sw = new StringWriter();
        int exitCode = await DoctorCommands.HandleDoctorAsync(["--json"], outputWriter: sw);
        exitCode.Should().Be(0);

        string output = sw.ToString();
        using var doc = JsonDocument.Parse(output);
        var root = doc.RootElement;
        root.GetProperty("overallStatus").GetString().Should().NotBeNullOrEmpty();
        root.GetProperty("checks").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Doctor_UnreachableServer_ReturnsErrorCode()
    {
        int exitCode = await Program.Main(["doctor", "--server", "http://127.0.0.1:59999"]);
        exitCode.Should().Be(1);
    }

    [Fact]
    public async Task Doctor_UnreachableRelay_ReturnsErrorCode()
    {
        int exitCode = await Program.Main(["doctor", "--relay", "http://127.0.0.1:59999"]);
        exitCode.Should().Be(1);
    }

    [Fact]
    public async Task Doctor_WithMockHealthyServerAndRelay_ReturnsZero()
    {
        using var mockHandler = new MockHttpMessageHandler(req =>
        {
            string url = req.RequestUri!.ToString();
            if (url.Contains("health/live", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"status":"healthy"}""")
                };
            }
            if (url.Contains("health/ready", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"status":"ready"}""")
                };
            }
            if (url.Contains("pqc/readiness", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    {
                        "scannedAt": "2026-10-07T12:00:00Z",
                        "activeProfile": "hybrid-v1",
                        "readinessScorePercent": 100.0,
                        "totalKeysScanned": 2,
                        "quantumSafeKeys": 2,
                        "classicalKeys": 0,
                        "totalLicensesScanned": 5,
                        "quantumSafeLicenses": 5,
                        "atRiskLicenses": 0,
                        "isCnsa2Ready": true,
                        "isNis2Ready": true
                    }
                    """)
                };
            }
            if (url.Contains("health/grant", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    {
                        "status": "healthy",
                        "activeGrants": 3,
                        "minDaysUntilExpiration": 45.2
                    }
                    """)
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var client = new HttpClient(mockHandler);

        int exitCode = await DoctorCommands.HandleDoctorAsync(
            ["--server", "http://mock-cp.local", "--relay", "http://mock-relay.local", "--pqc"],
            client
        );

        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task Doctor_WithExpiringGrant_WarnsAndStrictFails()
    {
        using var mockHandler = new MockHttpMessageHandler(req =>
        {
            string url = req.RequestUri!.ToString();
            if (url.Contains("health/live", StringComparison.OrdinalIgnoreCase) ||
                url.Contains("health/ready", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"status":"ready"}""")
                };
            }
            if (url.Contains("health/grant", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    {
                        "status": "expiring_soon",
                        "activeGrants": 2,
                        "minDaysUntilExpiration": 1.8
                    }
                    """)
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var client = new HttpClient(mockHandler);

        // Non-strict: warning exists, but should not fail fatally (returns 0)
        int nonStrictCode = await DoctorCommands.HandleDoctorAsync(
            ["--relay", "http://mock-relay.local"],
            client
        );
        nonStrictCode.Should().Be(0);

        // Strict: warning must cause failure (returns 1)
        int strictCode = await DoctorCommands.HandleDoctorAsync(
            ["--relay", "http://mock-relay.local", "--strict"],
            client
        );
        strictCode.Should().Be(1);
    }

    private sealed class MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(handler(request));
    }
}
