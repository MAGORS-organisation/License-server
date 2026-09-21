using System.Net;
using System.Text.Json;
using FluentAssertions;
using Symbolon.Crypto;
using Symbolon.Format;
using Symbolon.Protocol;
using Xunit;

namespace Symbolon.Client.Tests;

public sealed class SymbolonClientTests
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
    public void Constructor_WithMalformedLicenseKey_ThrowsArgumentException()
    {
        var action = () => new SymbolonClient(new SymbolonClientOptions
        {
            ServerUri = new Uri("https://licenses.example.com"),
            LicenseKey = "NOT-A-VALID-KEY",
            ProductCode = "test-prod"
        });

        action.Should().Throw<ArgumentException>()
            .WithMessage("*Preklep v licenčnom kľúči*");
    }

    [Fact]
    public async Task AcquireSeatAsync_WhenServerReturnsSuccess_ReturnsAcquiredSeat()
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
            Jti = "lse_123",
            Iat = now,
            Exp = now + 600,
            Seat = 0,
            Fp = fpHash,
            Ent = ["core", "export"],
            Seq = 0
        });

        var handler = new MockHttpMessageHandler(req =>
        {
            var responseDto = new CheckoutResponseDto
            {
                LeaseId = "lse_123",
                Token = token,
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
                Seat = 0,
                Entitlements = ["core", "export"]
            };

            var res = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(responseDto, SymbolonProtocolJsonContext.Default.CheckoutResponseDto),
                    System.Text.Encoding.UTF8,
                    "application/json")
            };
            return res;
        });

        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://licenses.example.com") };
        using var keyRing = new SymbolonKeyRing();
        keyRing.Add(signingKey);

        var client = new SymbolonClient(new SymbolonClientOptions
        {
            ServerUri = new Uri("https://licenses.example.com"),
            LicenseKey = validKey.Canonical,
            ProductCode = "test-prod",
            HttpClient = http,
            TrustedKeys = keyRing,
            CustomFingerprint = components
        });

        await using var seat = await client.AcquireSeatAsync(["core", "export"]);

        seat.Acquired.Should().BeTrue();
        seat.LeaseId.Should().Be("lse_123");
        seat.SeatNo.Should().Be(0);
        seat.Entitlements.Should().Contain(["core", "export"]);
        seat.State.Should().Be(SeatState.Active);
    }

    [Fact]
    public async Task AcquireSeatAsync_WhenServerReturnsConflict_ReturnsDeniedSeat()
    {
        var validKey = LicenseKey.Generate();

        var handler = new MockHttpMessageHandler(req =>
            new HttpResponseMessage(HttpStatusCode.Conflict));

        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://licenses.example.com") };

        var client = new SymbolonClient(new SymbolonClientOptions
        {
            ServerUri = new Uri("https://licenses.example.com"),
            LicenseKey = validKey.Canonical,
            ProductCode = "test-prod",
            HttpClient = http
        });

        await using var seat = await client.AcquireSeatAsync();

        seat.Acquired.Should().BeFalse();
        seat.Reason.Should().Contain("Denied");
        seat.State.Should().Be(SeatState.Lost);
    }

    [Fact]
    public async Task AcquireSeatAsync_WhenServerThrows_ReturnsOfflineSeat()
    {
        var validKey = LicenseKey.Generate();

        var handler = new MockHttpMessageHandler(req =>
            throw new HttpRequestException("Connection refused"));

        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://licenses.example.com") };

        var client = new SymbolonClient(new SymbolonClientOptions
        {
            ServerUri = new Uri("https://licenses.example.com"),
            LicenseKey = validKey.Canonical,
            ProductCode = "test-prod",
            HttpClient = http
        });

        await using var seat = await client.AcquireSeatAsync();

        seat.Acquired.Should().BeFalse();
        seat.Reason.Should().Contain("Offline");
        seat.State.Should().Be(SeatState.Lost);
    }
}
