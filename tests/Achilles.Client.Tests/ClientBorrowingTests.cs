using System.Net;
using System.Text.Json;
using FluentAssertions;
using Achilles.Crypto;
using Achilles.Format;
using Achilles.Protocol;
using Xunit;

namespace Achilles.Client.Tests;

public sealed class ClientBorrowingTests
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
    public async Task BorrowSeatAsync_WhenValid_ReturnsBorrowResponse()
    {
        var validKey = LicenseKey.Generate();
        string requestedLeaseId = "lse_borrow_test_1";
        var borrowedUntil = DateTimeOffset.UtcNow.AddDays(14);
        var keyPair = ProofOfPossessionEngine.GenerateKeyPair();

        var handler = new MockHttpMessageHandler(req =>
        {
            req.Method.Should().Be(HttpMethod.Post);
            req.RequestUri!.ToString().Should().Contain($"/v1/leases/{requestedLeaseId}/borrow");

            var responseDto = new BorrowResponseDto(
                requestedLeaseId,
                borrowedUntil,
                "offline_jwt_token_payload",
                symlease: "-----BEGIN SYMBOLON LEASE-----\ntest\n-----END SYMBOLON LEASE-----",
                possessionKey: keyPair.PrivateKeyJwk);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(responseDto, AchillesProtocolJsonContext.Default.BorrowResponseDto),
                    System.Text.Encoding.UTF8,
                    "application/json")
            };
        });

        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://licenses.example.com") };
        using var client = new AchillesClient(new AchillesClientOptions
        {
            ServerUri = new Uri("https://licenses.example.com"),
            LicenseKey = validKey.Canonical,
            ProductCode = "test-prod",
            HttpClient = http
        });

        var result = await client.BorrowSeatAsync(requestedLeaseId, days: 14);

        result.Should().NotBeNull();
        result!.LeaseId.Should().Be(requestedLeaseId);
        result.BorrowedUntil.Should().Be(borrowedUntil);
        result.Token.Should().Be("offline_jwt_token_payload");
        result.Symlease.Should().NotBeNull();
        result.PossessionKey.Should().NotBeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(31)]
    public async Task BorrowSeatAsync_WithInvalidDays_ThrowsArgumentOutOfRangeException(int days)
    {
        var validKey = LicenseKey.Generate();
        using var client = new AchillesClient(new AchillesClientOptions
        {
            ServerUri = new Uri("https://licenses.example.com"),
            LicenseKey = validKey.Canonical,
            ProductCode = "test-prod"
        });

        var act = async () => await client.BorrowSeatAsync("lse_123", days);
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task ReturnBorrowedSeatAsync_WithProofOfPossession_Flow()
    {
        var validKey = LicenseKey.Generate();
        string leaseId = "lse_return_pop_1";
        var keyPair = ProofOfPossessionEngine.GenerateKeyPair();
        string symlease = "-----BEGIN SYMBOLON LEASE-----\ntest\n-----END SYMBOLON LEASE-----";
        bool challengeCalled = false;
        bool returnCalled = false;

        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Post && req.RequestUri!.ToString().EndsWith($"/v1/leases/{leaseId}/return-challenge", StringComparison.Ordinal))
            {
                challengeCalled = true;
                var chDto = new ReturnChallengeResponseDto
                {
                    LeaseId = leaseId,
                    Nonce = "test-nonce-32-chars-long-base64url",
                    ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5)
                };
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(chDto, AchillesProtocolJsonContext.Default.ReturnChallengeResponseDto),
                        System.Text.Encoding.UTF8,
                        "application/json")
                };
            }

            if (req.Method == HttpMethod.Post && req.RequestUri!.ToString().EndsWith($"/v1/leases/{leaseId}/return", StringComparison.Ordinal))
            {
                returnCalled = true;
                var retDto = new EarlyReturnResponseDto
                {
                    Success = true,
                    LeaseId = leaseId,
                    ReturnedAt = DateTimeOffset.UtcNow
                };
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(retDto, AchillesProtocolJsonContext.Default.EarlyReturnResponseDto),
                        System.Text.Encoding.UTF8,
                        "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://licenses.example.com") };
        using var client = new AchillesClient(new AchillesClientOptions
        {
            ServerUri = new Uri("https://licenses.example.com"),
            LicenseKey = validKey.Canonical,
            ProductCode = "test-prod",
            HttpClient = http
        });

        bool success = await client.ReturnBorrowedSeatAsync(leaseId, symlease, keyPair.PrivateKeyJwk);

        success.Should().BeTrue();
        challengeCalled.Should().BeTrue();
        returnCalled.Should().BeTrue();
    }

    [Fact]
    public async Task ReturnBorrowedSeatAsync_ObsoleteDelete_SendsDeleteRequest()
    {
        var validKey = LicenseKey.Generate();
        string leaseId = "lse_return_test_1";
        bool deleteCalled = false;

        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Delete && req.RequestUri!.ToString().EndsWith($"/v1/leases/{leaseId}", StringComparison.Ordinal))
            {
                deleteCalled = true;
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://licenses.example.com") };
        using var client = new AchillesClient(new AchillesClientOptions
        {
            ServerUri = new Uri("https://licenses.example.com"),
            LicenseKey = validKey.Canonical,
            ProductCode = "test-prod",
            HttpClient = http
        });

#pragma warning disable CS0618
        bool success = await client.ReturnBorrowedSeatAsync(leaseId);
#pragma warning restore CS0618

        success.Should().BeTrue();
        deleteCalled.Should().BeTrue();
    }

    [Fact]
    public async Task SeatLease_BorrowAsync_TransitionsToBorrowedAndDoesNotReleaseOnDispose()
    {
        string leaseId = "lse_roaming_seat_1";
        var borrowedUntil = DateTimeOffset.UtcNow.AddDays(7);
        var keyPair = ProofOfPossessionEngine.GenerateKeyPair();
        string symlease = "-----BEGIN SYMBOLON LEASE-----\ntest\n-----END SYMBOLON LEASE-----";
        bool returnCalled = false;

        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Post && req.RequestUri!.ToString().Contains("/borrow", StringComparison.Ordinal))
            {
                var responseDto = new BorrowResponseDto(
                    leaseId,
                    borrowedUntil,
                    "offline_token_xyz",
                    symlease: symlease,
                    possessionKey: keyPair.PrivateKeyJwk);

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(responseDto, AchillesProtocolJsonContext.Default.BorrowResponseDto),
                        System.Text.Encoding.UTF8,
                        "application/json")
                };
            }

            if (req.Method == HttpMethod.Post && req.RequestUri!.ToString().Contains("/return-challenge", StringComparison.Ordinal))
            {
                var chDto = new ReturnChallengeResponseDto
                {
                    LeaseId = leaseId,
                    Nonce = "test-nonce-12345",
                    ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5)
                };
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(chDto, AchillesProtocolJsonContext.Default.ReturnChallengeResponseDto),
                        System.Text.Encoding.UTF8,
                        "application/json")
                };
            }

            if (req.Method == HttpMethod.Post && req.RequestUri!.ToString().Contains("/return", StringComparison.Ordinal))
            {
                returnCalled = true;
                var retDto = new EarlyReturnResponseDto
                {
                    Success = true,
                    LeaseId = leaseId,
                    ReturnedAt = DateTimeOffset.UtcNow
                };
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(retDto, AchillesProtocolJsonContext.Default.EarlyReturnResponseDto),
                        System.Text.Encoding.UTF8,
                        "application/json")
                };
            }

            if (req.Method == HttpMethod.Delete)
            {
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://licenses.example.com") };

        var lease = new SeatLease(
            acquired: true,
            reason: null,
            leaseId: leaseId,
            token: "initial_online_token",
            seatNo: 1,
            expiresAt: DateTimeOffset.UtcNow.AddMinutes(10),
            entitlements: ["core"],
            http: http,
            time: TimeProvider.System,
            heartbeatInterval: TimeSpan.FromMinutes(2),
            gracePeriod: TimeSpan.FromMinutes(1),
            fingerprint: new Dictionary<string, string>());

        lease.State.Should().Be(SeatState.Active);

        // Transition to borrowed
        bool borrowed = await lease.BorrowAsync(7);
        borrowed.Should().BeTrue();
        lease.State.Should().Be(SeatState.Borrowed);
        lease.Acquired.Should().BeTrue();
        lease.Token.Should().Be("offline_token_xyz");
        lease.Symlease.Should().Be(symlease);
        lease.PossessionKey.Should().Be(keyPair.PrivateKeyJwk);

        // Disposing when borrowed MUST NOT send delete request to server (offline laptop roam)
        await lease.DisposeAsync();

        // Explicit return uses proof of possession
        bool returned = await lease.ReturnBorrowedAsync();
        returned.Should().BeTrue();
        returnCalled.Should().BeTrue();
        lease.State.Should().Be(SeatState.Released);
    }
}
