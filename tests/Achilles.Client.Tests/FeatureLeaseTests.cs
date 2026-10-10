using System.Net;
using System.Text.Json;
using FluentAssertions;
using Achilles.Format;
using Achilles.Protocol;
using Xunit;

namespace Achilles.Client.Tests;

public sealed class FeatureLeaseTests
{
    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _handler(request);
        }
    }

    [Fact]
    public async Task HasFeature_RecognizesInitialEntitlementsAndDynamicAcquisitions()
    {
        var activeRequests = new List<string>();

        using var handler = new MockHttpMessageHandler(req =>
        {
            activeRequests.Add($"{req.Method} {req.RequestUri}");

            if (req.RequestUri?.AbsolutePath.EndsWith("/features/acquire", StringComparison.Ordinal) == true)
            {
                var resp = new FeatureAcquisitionResponseDto
                {
                    Success = true,
                    FeatureCode = "FEA_SOLVER",
                    Version = "2026.1",
                    InUse = 1,
                    MaxSeats = 5
                };
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(resp, AchillesProtocolJsonContext.Default.FeatureAcquisitionResponseDto))
                };
            }

            if (req.RequestUri?.AbsolutePath.EndsWith("/features/release", StringComparison.Ordinal) == true)
            {
                var resp = new ReleaseFeatureResponseDto
                {
                    Success = true,
                    FeatureCode = "FEA_SOLVER"
                };
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(resp, AchillesProtocolJsonContext.Default.ReleaseFeatureResponseDto))
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080") };

        await using var lease = new SeatLease(
            acquired: true,
            reason: null,
            leaseId: "lse_test_123",
            token: "jwt.fake.token",
            seatNo: 1,
            expiresAt: DateTimeOffset.UtcNow.AddMinutes(10),
            entitlements: ["CORE_CAD", "RENDER_BASIC"],
            http: http,
            time: TimeProvider.System,
            heartbeatInterval: TimeSpan.FromMinutes(10),
            gracePeriod: TimeSpan.FromMinutes(2),
            fingerprint: new Dictionary<string, string> { ["machineId"] = "m1" });

        // Initial entitlements
        lease.HasFeature("CORE_CAD").Should().BeTrue();
        lease.HasFeature("RENDER_BASIC").Should().BeTrue();
        lease.HasFeature("FEA_SOLVER").Should().BeFalse();

        // Dynamically acquire FEA_SOLVER
        var featLease = await lease.AcquireFeatureAsync("FEA_SOLVER", "2026.1");
        featLease.Should().NotBeNull();
        featLease.FeatureCode.Should().Be("FEA_SOLVER");
        featLease.IsActive.Should().BeTrue();

        lease.HasFeature("FEA_SOLVER").Should().BeTrue();

        // Release feature
        bool released = await lease.ReleaseFeatureAsync("FEA_SOLVER");
        released.Should().BeTrue();

        lease.HasFeature("FEA_SOLVER").Should().BeFalse();
    }

    [Fact]
    public async Task UseFeatureAsync_RaiiPattern_ReleasesOnAsyncDispose()
    {
        int acquireCalls = 0;
        int releaseCalls = 0;

        using var handler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri?.AbsolutePath.EndsWith("/features/acquire", StringComparison.Ordinal) == true)
            {
                acquireCalls++;
                var resp = new FeatureAcquisitionResponseDto
                {
                    Success = true,
                    FeatureCode = "GPU_ACCELERATOR",
                    InUse = 1,
                    MaxSeats = 2
                };
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(resp, AchillesProtocolJsonContext.Default.FeatureAcquisitionResponseDto))
                };
            }

            if (req.RequestUri?.AbsolutePath.EndsWith("/features/release", StringComparison.Ordinal) == true)
            {
                releaseCalls++;
                var resp = new ReleaseFeatureResponseDto
                {
                    Success = true,
                    FeatureCode = "GPU_ACCELERATOR"
                };
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(resp, AchillesProtocolJsonContext.Default.ReleaseFeatureResponseDto))
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080") };

        await using var lease = new SeatLease(
            acquired: true,
            reason: null,
            leaseId: "lse_raii_test",
            token: "jwt.fake.token",
            seatNo: 1,
            expiresAt: DateTimeOffset.UtcNow.AddMinutes(10),
            entitlements: ["CORE"],
            http: http,
            time: TimeProvider.System,
            heartbeatInterval: TimeSpan.FromMinutes(10),
            gracePeriod: TimeSpan.FromMinutes(2),
            fingerprint: new Dictionary<string, string> { ["machineId"] = "m1" });

        // RAII scope with 'await using'
        await using (var feat = await lease.UseFeatureAsync("GPU_ACCELERATOR"))
        {
            feat.IsActive.Should().BeTrue();
            lease.HasFeature("GPU_ACCELERATOR").Should().BeTrue();
            acquireCalls.Should().Be(1);
            releaseCalls.Should().Be(0);
        }

        // Exited scope: release should have been executed
        releaseCalls.Should().Be(1);
        lease.HasFeature("GPU_ACCELERATOR").Should().BeFalse();
    }

    [Fact]
    public async Task AcquireFeatureAsync_WhenDenied_ThrowsSymbolonFeatureDeniedException()
    {
        using var handler = new MockHttpMessageHandler(req =>
        {
            var err = new FeatureAcquisitionResponseDto
            {
                Success = false,
                FeatureCode = "FEA_SOLVER",
                Reason = "feature-capacity-exceeded"
            };
            return new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = new StringContent(JsonSerializer.Serialize(err, AchillesProtocolJsonContext.Default.FeatureAcquisitionResponseDto))
            };
        });

        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080") };

        await using var lease = new SeatLease(
            acquired: true,
            reason: null,
            leaseId: "lse_denied_test",
            token: "jwt.fake.token",
            seatNo: 1,
            expiresAt: DateTimeOffset.UtcNow.AddMinutes(10),
            entitlements: ["CORE"],
            http: http,
            time: TimeProvider.System,
            heartbeatInterval: TimeSpan.FromMinutes(10),
            gracePeriod: TimeSpan.FromMinutes(2),
            fingerprint: new Dictionary<string, string> { ["machineId"] = "m1" });

        var act = async () => await lease.AcquireFeatureAsync("FEA_SOLVER");

        var ex = await act.Should().ThrowAsync<SymbolonFeatureDeniedException>();
        ex.Which.FeatureCode.Should().Be("FEA_SOLVER");
        ex.Which.Reason.Should().Be("feature-capacity-exceeded");
    }
}
