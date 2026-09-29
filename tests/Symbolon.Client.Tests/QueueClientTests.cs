using System.Net;
using System.Text.Json;
using FluentAssertions;
using Symbolon.Crypto;
using Symbolon.Format;
using Symbolon.Protocol;
using Xunit;

namespace Symbolon.Client.Tests;

public sealed class QueueClientTests
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
    }

    [Fact]
    public async Task AcquireSeatAsync_WhenServerReturns202_AndAutoWaitPollsReady_ReturnsAcquiredSeat()
    {
        var validKey = LicenseKey.Generate();
        using var signingKey = Es256SignatureProvider.GenerateKey("lease-key");
        var tokenSigner = new LeaseTokenSigner(signingKey);

        var components = DeviceFingerprint.Collect();
        string fpHash = FingerprintHelper.ComputeHash(components);

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string token = tokenSigner.IssueToken(new LeaseClaims
        {
            Iss = "relay:rly_1",
            Sub = "lic_1",
            Jti = "lse_promoted",
            Iat = now,
            Exp = now + 600,
            Seat = 1,
            Fp = fpHash,
            Ent = ["core"],
            Seq = 0
        });

        int pollCount = 0;
        using var handler = new MockHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Post && req.RequestUri?.AbsolutePath.EndsWith("/v1/leases", StringComparison.Ordinal) == true)
            {
                var queuedResp = new QueuedResponseDto
                {
                    Ticket = "q_test_123",
                    Position = 1,
                    RetryAfterSeconds = 1
                };
                var json = JsonSerializer.Serialize(queuedResp, SymbolonProtocolJsonContext.Default.QueuedResponseDto);
                var resp = new HttpResponseMessage(HttpStatusCode.Accepted)
                {
                    Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
                };
                resp.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(1));
                return resp;
            }

            if (req.Method == HttpMethod.Get && req.RequestUri?.AbsolutePath.Contains("/v1/queue/q_test_123", StringComparison.Ordinal) == true)
            {
                pollCount++;
                var statusResp = new QueueStatusResponseDto
                {
                    Ticket = "q_test_123",
                    Status = "ready",
                    LeaseId = "lse_promoted",
                    Token = token,
                    Seat = 1,
                    ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10)
                };
                var json = JsonSerializer.Serialize(statusResp, SymbolonProtocolJsonContext.Default.QueueStatusResponseDto);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
                };
            }

            if (req.Method == HttpMethod.Delete)
            {
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://licenses.example.com") };
        var keyRing = new SymbolonKeyRing();
        keyRing.Add(signingKey);

        var client = new SymbolonClient(new SymbolonClientOptions
        {
            ServerUri = new Uri("https://licenses.example.com"),
            LicenseKey = validKey.Canonical,
            ProductCode = "test-prod",
            HttpClient = httpClient,
            TrustedKeys = keyRing,
            CustomFingerprint = components,
            AllowQueue = true
        });

        await using var lease = await client.AcquireSeatAsync();

        lease.Acquired.Should().BeTrue();
        lease.LeaseId.Should().Be("lse_promoted");
        lease.Token.Should().Be(token);
        pollCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task AcquireSeatAsync_WhenAllowQueueIsFalse_AndServerReturns202_ReturnsDeniedSeat()
    {
        var validKey = LicenseKey.Generate();

        using var handler = new MockHttpMessageHandler(req =>
        {
            var queuedResp = new QueuedResponseDto
            {
                Ticket = "q_disabled_test",
                Position = 1,
                RetryAfterSeconds = 1
            };
            var json = JsonSerializer.Serialize(queuedResp, SymbolonProtocolJsonContext.Default.QueuedResponseDto);
            return new HttpResponseMessage(HttpStatusCode.Accepted)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };
        });

        using var httpClient = new HttpClient(handler);
        var client = new SymbolonClient(new SymbolonClientOptions
        {
            ServerUri = new Uri("https://licenses.example.com"),
            LicenseKey = validKey.Canonical,
            ProductCode = "test-prod",
            HttpClient = httpClient,
            AllowQueue = false
        });

        await using var lease = await client.AcquireSeatAsync();

        lease.Acquired.Should().BeFalse();
        lease.Reason.Should().Contain("client auto-wait is disabled");
    }

    [Fact]
    public async Task AcquireSeatAsync_WhenQueueTimesOut_SendsDeleteAndReturnsDenied()
    {
        var validKey = LicenseKey.Generate();
        bool deleteCalled = false;

        using var handler = new MockHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Post)
            {
                var queuedResp = new QueuedResponseDto
                {
                    Ticket = "q_timeout_test",
                    Position = 1,
                    RetryAfterSeconds = 1
                };
                var json = JsonSerializer.Serialize(queuedResp, SymbolonProtocolJsonContext.Default.QueuedResponseDto);
                return new HttpResponseMessage(HttpStatusCode.Accepted)
                {
                    Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
                };
            }

            if (req.Method == HttpMethod.Get)
            {
                var statusResp = new QueueStatusResponseDto
                {
                    Ticket = "q_timeout_test",
                    Status = "waiting",
                    Position = 1,
                    RetryAfterSeconds = 1
                };
                var json = JsonSerializer.Serialize(statusResp, SymbolonProtocolJsonContext.Default.QueueStatusResponseDto);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
                };
            }

            if (req.Method == HttpMethod.Delete)
            {
                deleteCalled = true;
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var httpClient = new HttpClient(handler);
        var client = new SymbolonClient(new SymbolonClientOptions
        {
            ServerUri = new Uri("https://licenses.example.com"),
            LicenseKey = validKey.Canonical,
            ProductCode = "test-prod",
            HttpClient = httpClient,
            AllowQueue = true,
            MaxQueueWait = TimeSpan.FromMilliseconds(50)
        });

        await using var lease = await client.AcquireSeatAsync();

        lease.Acquired.Should().BeFalse();
        lease.Reason.Should().Contain("Queue wait timeout exceeded");
        deleteCalled.Should().BeTrue();
    }
}
