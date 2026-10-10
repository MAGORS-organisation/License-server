using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Achilles.ControlPlane.Models;
using Achilles.Crypto;
using Achilles.Format;
using Achilles.Protocol;
using Xunit;

namespace Achilles.ControlPlane.Tests;

public sealed class FraudAndBorrowingApiTests : IClassFixture<ControlPlaneFactory>
{
    private readonly HttpClient _client;

    public FraudAndBorrowingApiTests(ControlPlaneFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetFraudRadar_ReturnsOk()
    {
        var res = await _client.GetAsync("/admin/v1/fraud/radar");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var radar = await res.Content.ReadFromJsonAsync<List<FraudRadarAdminDto>>();
        Assert.NotNull(radar);
    }

    [Fact]
    public async Task GetBorrowedSeats_ReturnsOk()
    {
        var res = await _client.GetAsync("/admin/v1/leases/borrowed");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var seats = await res.Content.ReadFromJsonAsync<List<BorrowedSeatAdminDto>>();
        Assert.NotNull(seats);
    }

    [Fact]
    public async Task Checkout_WithImpossibleTravel_RecordsAnomalyInFraudRadar()
    {
        // 1. Create Tenant, Product, Policy, License
        string slug = $"tenant-fraud-{Guid.NewGuid():N}";
        var tenantRes = await _client.PostAsJsonAsync("/admin/v1/tenants", new CreateTenantDto(slug, "Fraud Detection Corp"));
        Assert.Equal(HttpStatusCode.Created, tenantRes.StatusCode);
        var tenant = await tenantRes.Content.ReadFromJsonAsync<TenantDto>();
        Assert.NotNull(tenant);

        var prodReq = new HttpRequestMessage(HttpMethod.Post, "/admin/v1/products")
        {
            Content = JsonContent.Create(new CreateProductDto($"app-fraud-{Guid.NewGuid():N}"[..12], "App", ["windows"]))
        };
        prodReq.Headers.Add("X-Tenant-Id", tenant.Id);
        var prodRes = await _client.SendAsync(prodReq);
        Assert.Equal(HttpStatusCode.Created, prodRes.StatusCode);
        var product = await prodRes.Content.ReadFromJsonAsync<ProductDto>();
        Assert.NotNull(product);

        var polRes = await _client.PostAsJsonAsync("/admin/v1/policies", new CreatePolicyDto
        {
            ProductId = product.Id,
            Code = "floating-fraud-10",
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
            CustomerRef = "CUST-FRAUD-01",
            MaxSeats = 10
        });
        Assert.Equal(HttpStatusCode.Created, licRes.StatusCode);
        var license = await licRes.Content.ReadFromJsonAsync<LicenseResponseDto>();
        Assert.NotNull(license);
        Assert.NotNull(license.LicenseKey);

        // 2. First checkout from Bratislava
        var req1 = new HttpRequestMessage(HttpMethod.Post, "/v1/leases")
        {
            Content = JsonContent.Create(new CheckoutRequestDto
            {
                LicenseKey = license.LicenseKey!,
                FingerprintComponents = new Dictionary<string, string> { ["host"] = "host1" },
                MachineId = "host-bts",
                UserId = "traveler_joe",
                Quantity = 1
            })
        };
        req1.Headers.Add("X-Forwarded-For", "85.237.100.1"); // Bratislava
        var res1 = await _client.SendAsync(req1);
        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);

        // 3. Second checkout 1 second later from Tokyo (Impossible Travel!)
        var req2 = new HttpRequestMessage(HttpMethod.Post, "/v1/leases")
        {
            Content = JsonContent.Create(new CheckoutRequestDto
            {
                LicenseKey = license.LicenseKey!,
                FingerprintComponents = new Dictionary<string, string> { ["host"] = "host2" },
                MachineId = "host-tyo",
                UserId = "traveler_joe",
                Quantity = 1
            })
        };
        req2.Headers.Add("X-Forwarded-For", "133.242.1.1"); // Tokyo
        var res2 = await _client.SendAsync(req2);
        Assert.Equal(HttpStatusCode.OK, res2.StatusCode);

        // 4. Verify fraud radar captured the anomaly
        var radarRes = await _client.GetAsync("/admin/v1/fraud/radar");
        Assert.Equal(HttpStatusCode.OK, radarRes.StatusCode);
        var radar = await radarRes.Content.ReadFromJsonAsync<List<FraudRadarAdminDto>>();
        Assert.NotNull(radar);
        Assert.Contains(radar, r => r.RiskType == "impossible_travel" && r.UserId == "traveler_joe");
    }

    [Fact]
    public async Task BorrowSeat_ThenAdminReturnsSeat_Succeeds()
    {
        // 1. Create Tenant, Product, Policy, License
        string slug = $"tenant-borrow-{Guid.NewGuid():N}";
        var tenantRes = await _client.PostAsJsonAsync("/admin/v1/tenants", new CreateTenantDto(slug, "Borrow Corp"));
        Assert.Equal(HttpStatusCode.Created, tenantRes.StatusCode);
        var tenant = await tenantRes.Content.ReadFromJsonAsync<TenantDto>();
        Assert.NotNull(tenant);

        var prodReq = new HttpRequestMessage(HttpMethod.Post, "/admin/v1/products")
        {
            Content = JsonContent.Create(new CreateProductDto($"app-borrow-{Guid.NewGuid():N}"[..12], "Borrow App", ["windows"]))
        };
        prodReq.Headers.Add("X-Tenant-Id", tenant.Id);
        var prodRes = await _client.SendAsync(prodReq);
        Assert.Equal(HttpStatusCode.Created, prodRes.StatusCode);
        var product = await prodRes.Content.ReadFromJsonAsync<ProductDto>();
        Assert.NotNull(product);

        var polRes = await _client.PostAsJsonAsync("/admin/v1/policies", new CreatePolicyDto
        {
            ProductId = product.Id,
            Code = "floating-borrow-5",
            Name = "Floating 5 seats",
            MaxSeats = 5,
            LicenseModel = "floating",
            BorrowEnabled = true,
            BorrowMaxDurationDays = 14
        });
        Assert.Equal(HttpStatusCode.Created, polRes.StatusCode);
        var policy = await polRes.Content.ReadFromJsonAsync<PolicyDto>();
        Assert.NotNull(policy);

        var licRes = await _client.PostAsJsonAsync("/admin/v1/licenses", new CreateLicenseDto
        {
            PolicyId = policy.Id,
            CustomerRef = "CUST-BORROW-01",
            MaxSeats = 5
        });
        Assert.Equal(HttpStatusCode.Created, licRes.StatusCode);
        var license = await licRes.Content.ReadFromJsonAsync<LicenseResponseDto>();
        Assert.NotNull(license);
        Assert.NotNull(license.LicenseKey);

        // 2. Acquire a seat
        var checkoutRes = await _client.PostAsJsonAsync("/v1/leases", new CheckoutRequestDto
        {
            LicenseKey = license.LicenseKey!,
            FingerprintComponents = new Dictionary<string, string> { ["host"] = "cad-laptop-01" },
            Quantity = 1
        });
        Assert.Equal(HttpStatusCode.OK, checkoutRes.StatusCode);
        var checkoutDto = await checkoutRes.Content.ReadFromJsonAsync<CheckoutResponseDto>();
        Assert.NotNull(checkoutDto);

        // 3. Borrow the seat for 10 days
        var borrowRes = await _client.PostAsJsonAsync($"/v1/leases/{checkoutDto.LeaseId}/borrow", new BorrowRequestDto(10));
        Assert.Equal(HttpStatusCode.OK, borrowRes.StatusCode);
        var borrowDto = await borrowRes.Content.ReadFromJsonAsync<BorrowResponseDto>();
        Assert.NotNull(borrowDto);
        Assert.Equal(checkoutDto.LeaseId, borrowDto.LeaseId);

        // 4. Verify admin lists the borrowed seat
        var listRes = await _client.GetAsync("/admin/v1/leases/borrowed");
        Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);
        var borrowedList = await listRes.Content.ReadFromJsonAsync<List<BorrowedSeatAdminDto>>();
        Assert.NotNull(borrowedList);
        Assert.Contains(borrowedList, s => s.LeaseId == checkoutDto.LeaseId);

        // 5. Admin forcefully returns the seat
        var returnRes = await _client.PostAsync($"/admin/v1/leases/{checkoutDto.LeaseId}/return", null);
        Assert.Equal(HttpStatusCode.OK, returnRes.StatusCode);

        // 6. Verify it is no longer listed in borrowed seats
        var listRes2 = await _client.GetAsync("/admin/v1/leases/borrowed");
        var borrowedList2 = await listRes2.Content.ReadFromJsonAsync<List<BorrowedSeatAdminDto>>();
        Assert.NotNull(borrowedList2);
        Assert.DoesNotContain(borrowedList2, s => s.LeaseId == checkoutDto.LeaseId);
    }

    private async Task<(string LicenseKey, string LeaseId)> SetupAndAcquireSeatAsync(
        bool borrowEnabled = true,
        int borrowMaxDays = 14,
        int maxConcurrent = 5,
        int maxSeats = 5)
    {
        string slug = $"tenant-b-{Guid.NewGuid():N}";
        var tenantRes = await _client.PostAsJsonAsync("/admin/v1/tenants", new CreateTenantDto(slug, "Borrow Test Corp"));
        var tenant = await tenantRes.Content.ReadFromJsonAsync<TenantDto>();

        var prodReq = new HttpRequestMessage(HttpMethod.Post, "/admin/v1/products")
        {
            Content = JsonContent.Create(new CreateProductDto($"app-b-{Guid.NewGuid():N}"[..12], "Borrow Test App", ["windows"]))
        };
        prodReq.Headers.Add("X-Tenant-Id", tenant!.Id);
        var prodRes = await _client.SendAsync(prodReq);
        var product = await prodRes.Content.ReadFromJsonAsync<ProductDto>();

        var polRes = await _client.PostAsJsonAsync("/admin/v1/policies", new CreatePolicyDto
        {
            ProductId = product!.Id,
            Code = $"policy-b-{Guid.NewGuid():N}"[..16],
            Name = "Floating borrow policy",
            MaxSeats = maxSeats,
            LicenseModel = "floating",
            BorrowEnabled = borrowEnabled,
            BorrowMaxDurationDays = borrowMaxDays,
            BorrowMaxConcurrent = maxConcurrent
        });
        var policy = await polRes.Content.ReadFromJsonAsync<PolicyDto>();

        var licRes = await _client.PostAsJsonAsync("/admin/v1/licenses", new CreateLicenseDto
        {
            PolicyId = policy!.Id,
            CustomerRef = "CUST-BORROW-TEST",
            MaxSeats = maxSeats
        });
        var license = await licRes.Content.ReadFromJsonAsync<LicenseResponseDto>();

        var checkoutRes = await _client.PostAsJsonAsync("/v1/leases", new CheckoutRequestDto
        {
            LicenseKey = license!.LicenseKey!,
            FingerprintComponents = new Dictionary<string, string> { ["host"] = "test-machine" },
            Quantity = 1
        });
        var checkoutDto = await checkoutRes.Content.ReadFromJsonAsync<CheckoutResponseDto>();

        return (license.LicenseKey!, checkoutDto!.LeaseId);
    }

    [Fact]
    public async Task BorrowSeat_IssuesSymleaseAndPossessionKey_FLT19()
    {
        var (_, leaseId) = await SetupAndAcquireSeatAsync();

        var borrowRes = await _client.PostAsJsonAsync($"/v1/leases/{leaseId}/borrow", new BorrowRequestDto(7));
        Assert.Equal(HttpStatusCode.OK, borrowRes.StatusCode);

        var borrowDto = await borrowRes.Content.ReadFromJsonAsync<BorrowResponseDto>();
        Assert.NotNull(borrowDto);
        Assert.Equal(leaseId, borrowDto.LeaseId);
        Assert.True(borrowDto.BorrowedUntil > DateTimeOffset.UtcNow.AddDays(6));
        Assert.NotNull(borrowDto.Token);

        // FLT-19: Must return PEM-armored .symlease artifact and ephemeral possession key
        borrowDto.Symlease.Should().NotBeNull();
        borrowDto.Symlease.Should().StartWith("-----BEGIN SYMBOLON LEASE-----");
        borrowDto.Symlease.Trim().Should().EndWith("-----END SYMBOLON LEASE-----");

        borrowDto.PossessionKey.Should().NotBeNull();
        borrowDto.PossessionKey.Should().Contain("\"crv\":\"P-256\"");
        borrowDto.PossessionKey.Should().Contain("\"d\":"); // Private key returned to client
    }

    [Fact]
    public async Task BorrowSeat_PlainDelete_Returns403ProofOfPossessionRequired_FLT22()
    {
        var (_, leaseId) = await SetupAndAcquireSeatAsync();

        var borrowRes = await _client.PostAsJsonAsync($"/v1/leases/{leaseId}/borrow", new BorrowRequestDto(7));
        borrowRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // FLT-22: Standard DELETE on active borrowed seat must return 403 Forbidden with ProofOfPossessionRequired
        var deleteRes = await _client.DeleteAsync($"/v1/leases/{leaseId}");
        deleteRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        string body = await deleteRes.Content.ReadAsStringAsync();
        body.Should().Contain(ProblemTypes.ProofOfPossessionRequired);
    }

    [Fact]
    public async Task BorrowSeat_EarlyReturn_WithValidProofOfPossession_Succeeds_FLT21()
    {
        var (_, leaseId) = await SetupAndAcquireSeatAsync();

        var borrowRes = await _client.PostAsJsonAsync($"/v1/leases/{leaseId}/borrow", new BorrowRequestDto(7));
        borrowRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var borrowDto = await borrowRes.Content.ReadFromJsonAsync<BorrowResponseDto>();
        borrowDto.Should().NotBeNull();

        // 1. Request challenge nonce
        var challengeRes = await _client.PostAsync($"/v1/leases/{leaseId}/return-challenge", null);
        challengeRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var challengeDto = await challengeRes.Content.ReadFromJsonAsync<ReturnChallengeResponseDto>();
        challengeDto.Should().NotBeNull();
        challengeDto!.Nonce.Should().NotBeNullOrWhiteSpace();

        // 2. Sign challenge nonce with possession private key
        string signature = ProofOfPossessionEngine.SignChallenge(challengeDto.Nonce, borrowDto!.PossessionKey!);

        // 3. Submit early return with proof-of-possession
        var earlyReturnReq = new EarlyReturnRequestDto
        {
            Symlease = borrowDto.Symlease!,
            Nonce = challengeDto.Nonce,
            Signature = signature
        };

        var returnRes = await _client.PostAsJsonAsync($"/v1/leases/{leaseId}/return", earlyReturnReq);
        returnRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var returnDto = await returnRes.Content.ReadFromJsonAsync<EarlyReturnResponseDto>();
        returnDto.Should().NotBeNull();
        returnDto!.Success.Should().BeTrue();
        returnDto.LeaseId.Should().Be(leaseId);

        // 4. Verify lease is released (DELETE on released lease returns Success = false)
        var checkAgainRes = await _client.DeleteAsync($"/v1/leases/{leaseId}");
        var checkAgainDto = await checkAgainRes.Content.ReadFromJsonAsync<ReleaseResponseDto>();
        checkAgainDto!.Success.Should().BeFalse();
    }

    [Fact]
    public async Task BorrowSeat_EarlyReturn_WithInvalidSignature_Fails403()
    {
        var (_, leaseId) = await SetupAndAcquireSeatAsync();

        var borrowRes = await _client.PostAsJsonAsync($"/v1/leases/{leaseId}/borrow", new BorrowRequestDto(7));
        var borrowDto = await borrowRes.Content.ReadFromJsonAsync<BorrowResponseDto>();
        borrowDto.Should().NotBeNull();

        // Request challenge
        var challengeRes = await _client.PostAsync($"/v1/leases/{leaseId}/return-challenge", null);
        var challengeDto = await challengeRes.Content.ReadFromJsonAsync<ReturnChallengeResponseDto>();
        challengeDto.Should().NotBeNull();

        // Sign with a completely different key pair
        var foreignKeyPair = ProofOfPossessionEngine.GenerateKeyPair();
        string foreignSignature = ProofOfPossessionEngine.SignChallenge(challengeDto!.Nonce, foreignKeyPair.PrivateKeyJwk);

        var earlyReturnReq = new EarlyReturnRequestDto
        {
            Symlease = borrowDto!.Symlease!,
            Nonce = challengeDto.Nonce,
            Signature = foreignSignature
        };

        var returnRes = await _client.PostAsJsonAsync($"/v1/leases/{leaseId}/return", earlyReturnReq);
        returnRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        string body = await returnRes.Content.ReadAsStringAsync();
        body.Should().Contain(ProblemTypes.InvalidProofOfPossession);
    }

    [Fact]
    public async Task BorrowSeat_PolicyViolation_DurationExceeded_Fails400_FLT18()
    {
        // Policy limits borrow to max 5 days
        var (_, leaseId) = await SetupAndAcquireSeatAsync(borrowMaxDays: 5);

        // Request 10 days
        var borrowRes = await _client.PostAsJsonAsync($"/v1/leases/{leaseId}/borrow", new BorrowRequestDto(10));
        borrowRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        string body = await borrowRes.Content.ReadAsStringAsync();
        body.Should().Contain(ProblemTypes.BorrowDurationExceeded);
    }

    [Fact]
    public async Task BorrowSeat_PolicyViolation_Disabled_Fails403_FLT18()
    {
        // Policy with borrow disabled
        var (_, leaseId) = await SetupAndAcquireSeatAsync(borrowEnabled: false);

        var borrowRes = await _client.PostAsJsonAsync($"/v1/leases/{leaseId}/borrow", new BorrowRequestDto(3));
        borrowRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        string body = await borrowRes.Content.ReadAsStringAsync();
        body.Should().Contain(ProblemTypes.BorrowDisabled);
    }
}
