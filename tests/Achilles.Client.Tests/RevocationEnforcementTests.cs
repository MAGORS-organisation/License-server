using System.Net;
using System.Text.Json;
using FluentAssertions;
using Achilles.Client.Revocation;
using Achilles.Crypto;
using Achilles.Format;
using Achilles.Protocol;
using Xunit;

namespace Achilles.Client.Tests;

public sealed class RevocationEnforcementTests
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
    public void RevocationCache_TracksAllSubjectTypes_AndMonotonicSeq()
    {
        using var keyRing = new AchillesKeyRing();
        var cache = new RevocationCache(keyRing);

        var claims = new RevocationListClaims(
            Iss: "https://symbolon.enterprise.local",
            Iat: 1700000000,
            Exp: 1700003600,
            Symrl: new RevocationPayload(
                V: 1,
                Seq: 5,
                Full: true,
                Since: null,
                Revoked:
                [
                    new RevocationItem("license", "lic_001", 1700000000, "fraud"),
                    new RevocationItem("machine", "mach_fp_hash", 1700000000, "stolen"),
                    new RevocationItem("kid", "key_compromised", 1700000000, "key-leak"),
                    new RevocationItem("relay", "rly_rogue", 1700000000, "decommissioned"),
                    new RevocationItem("lease", "lse_bad", 1700000000, "abused"),
                    new RevocationItem("unknown_future_type", "fut_1", 1700000000, "test")
                ]
            )
        );

        bool applied = cache.ApplyRevocationList(claims);
        applied.Should().BeTrue();
        cache.LastSeenSeq.Should().Be(5);
        cache.TotalRevocationCount.Should().Be(5);

        cache.IsLicenseRevoked("lic_001", out var rLic).Should().BeTrue();
        rLic!.Reason.Should().Be("fraud");

        cache.IsMachineRevoked("mach_fp_hash", out var rMach).Should().BeTrue();
        rMach!.Reason.Should().Be("stolen");

        cache.IsKidRevoked("key_compromised", out var rKid).Should().BeTrue();
        keyRing.IsRevoked("key_compromised").Should().BeTrue();

        cache.IsRelayRevoked("rly_rogue", out var rRly).Should().BeTrue();
        cache.IsLeaseRevoked("lse_bad", out var rLse).Should().BeTrue();

        // Stale sequence rejection (RVL-7)
        var staleClaims = new RevocationListClaims(
            Iss: "https://symbolon.enterprise.local",
            Iat: 1700000000,
            Exp: 1700003600,
            Symrl: new RevocationPayload(
                V: 1,
                Seq: 4,
                Full: true,
                Since: null,
                Revoked: []
            )
        );
        cache.ApplyRevocationList(staleClaims).Should().BeFalse();
        cache.LastSeenSeq.Should().Be(5);
    }

    [Fact]
    public async Task AcquireSeatAsync_ThrowsRevocationException_WhenLicenseRevoked()
    {
        var validKey = LicenseKey.Generate();
        var cache = new RevocationCache();
        cache.Add(new RevocationItem("license", validKey.Canonical, 1700000000, "non-payment"));

        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://licenses.example.com") };

        var client = new AchillesClient(new AchillesClientOptions
        {
            ServerUri = new Uri("https://licenses.example.com"),
            LicenseKey = validKey.Canonical,
            ProductCode = "test-prod",
            HttpClient = http,
            RevocationCache = cache
        });

        var action = async () => await client.AcquireSeatAsync();

        var ex = await action.Should().ThrowAsync<SymbolonRevocationException>();
        ex.Which.SubjectType.Should().Be("license");
        ex.Which.SubjectId.Should().Be(validKey.Canonical);
        ex.Which.Reason.Should().Be("non-payment");
    }

    [Fact]
    public async Task AcquireSeatAsync_ThrowsRevocationException_WhenMachineRevoked()
    {
        var validKey = LicenseKey.Generate();
        var components = DeviceFingerprint.Collect();
        string fpHash = FingerprintHelper.ComputeHash(components);

        var cache = new RevocationCache();
        cache.Add(new RevocationItem("machine", fpHash, 1700000000, "hardware-tamper"));

        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://licenses.example.com") };

        var client = new AchillesClient(new AchillesClientOptions
        {
            ServerUri = new Uri("https://licenses.example.com"),
            LicenseKey = validKey.Canonical,
            ProductCode = "test-prod",
            HttpClient = http,
            RevocationCache = cache,
            CustomFingerprint = components
        });

        var action = async () => await client.AcquireSeatAsync();

        var ex = await action.Should().ThrowAsync<SymbolonRevocationException>();
        ex.Which.SubjectType.Should().Be("machine");
        ex.Which.SubjectId.Should().Be(fpHash);
        ex.Which.Reason.Should().Be("hardware-tamper");
    }

    [Fact]
    public async Task AcquireSeatAsync_ThrowsRevocationException_WhenLeaseRevoked()
    {
        var validKey = LicenseKey.Generate();
        var cache = new RevocationCache();
        cache.Add(new RevocationItem("lease", "lse_revoked_123", 1700000000, "concurrent-violation"));

        var handler = new MockHttpMessageHandler(req =>
        {
            var responseDto = new CheckoutResponseDto
            {
                LeaseId = "lse_revoked_123",
                Token = "dummy.token.here",
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
                Seat = 0,
                Entitlements = ["core"]
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(responseDto, AchillesProtocolJsonContext.Default.CheckoutResponseDto),
                    System.Text.Encoding.UTF8,
                    "application/json")
            };
        });

        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://licenses.example.com") };

        var client = new AchillesClient(new AchillesClientOptions
        {
            ServerUri = new Uri("https://licenses.example.com"),
            LicenseKey = validKey.Canonical,
            ProductCode = "test-prod",
            HttpClient = http,
            RevocationCache = cache
        });

        var action = async () => await client.AcquireSeatAsync();

        var ex = await action.Should().ThrowAsync<SymbolonRevocationException>();
        ex.Which.SubjectType.Should().Be("lease");
        ex.Which.SubjectId.Should().Be("lse_revoked_123");
    }

    [Fact]
    public async Task AcquireSeatAsync_RejectsToken_WhenSigningKeyIsRevoked()
    {
        var validKey = LicenseKey.Generate();
        using var signingKey = Es256SignatureProvider.GenerateKey("compromised-key");
        var tokenSigner = new LeaseTokenSigner(signingKey);

        var components = DeviceFingerprint.Collect();
        string fpHash = FingerprintHelper.ComputeHash(components);

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string token = tokenSigner.IssueToken(new LeaseClaims
        {
            Iss = "relay:rly_1",
            Sub = "lic_1",
            Jti = "lse_ok",
            Iat = now,
            Exp = now + 600,
            Seat = 0,
            Fp = fpHash,
            Ent = ["core"],
            Seq = 0
        });

        var handler = new MockHttpMessageHandler(_ =>
        {
            var responseDto = new CheckoutResponseDto
            {
                LeaseId = "lse_ok",
                Token = token,
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
                Seat = 0,
                Entitlements = ["core"]
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(responseDto, AchillesProtocolJsonContext.Default.CheckoutResponseDto),
                    System.Text.Encoding.UTF8,
                    "application/json")
            };
        });

        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://licenses.example.com") };
        using var keyRing = new AchillesKeyRing();
        keyRing.Add(signingKey);

        var cache = new RevocationCache(keyRing);
        // Revoke the key in the cache
        cache.Add(new RevocationItem("kid", "compromised-key", now, "private-key-exposed"));

        var client = new AchillesClient(new AchillesClientOptions
        {
            ServerUri = new Uri("https://licenses.example.com"),
            LicenseKey = validKey.Canonical,
            ProductCode = "test-prod",
            HttpClient = http,
            TrustedKeys = keyRing,
            CustomFingerprint = components,
            RevocationCache = cache
        });

        await using var seat = await client.AcquireSeatAsync();

        seat.Acquired.Should().BeFalse();
        seat.Reason.Should().Contain("Token verification failed: revoked-kid:compromised-key");
    }

    [Fact]
    public void LicenseDocumentVerifier_RejectsRevokedLicenseId()
    {
        using var key1 = Es256SignatureProvider.GenerateKey("k1");
        using var keyRing = new AchillesKeyRing();
        keyRing.Add(key1);

        var signer = new LicenseDocumentSigner([key1]);
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var claims = new LicenseClaims
        {
            Iss = "https://auth.symbolon.dev",
            Sub = "LIC-REVOKED-1",
            Aud = "https://auth.symbolon.dev",
            Jti = "jti_123",
            Iat = now,
            Exp = now + 3600,
            Symlic = new SymlicClaims
            {
                V = 1,
                Profile = "hybrid-v1",
                License = new LicenseMetadata { Model = "floating", State = "active", Key = "KEY-123" },
                Limits = new LicenseLimits { MaxSeats = 10, SeatUnit = "user" },
                RequiredAlgs = [Alg.Es256]
            }
        };

        string pem = signer.Sign(claims);

        // Verifier configured to check revocation
        var verifier = new LicenseDocumentVerifier(keyRing, options: new AchillesVerifierOptions
        {
            IsLicenseRevoked = id => id == "LIC-REVOKED-1"
        });

        var result = verifier.Verify(pem);
        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be("revoked-license:LIC-REVOKED-1");
    }
}
