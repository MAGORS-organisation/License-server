using System.Net;
using System.Text.Json;
using FluentAssertions;
using Achilles.Crypto;
using Achilles.Format;
using Achilles.Protocol;
using Xunit;

namespace Achilles.Client.Tests;

public sealed class TokenClientTests
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

    private static AchillesClient CreateClient(MockHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://licenses.example.com") };
        var validKey = LicenseKey.Generate();
        return new AchillesClient(new AchillesClientOptions
        {
            ServerUri = new Uri("https://licenses.example.com"),
            LicenseKey = validKey.Canonical,
            ProductCode = "test-prod",
            HttpClient = httpClient,
            MachineId = "mach_test_1"
        });
    }

    [Fact]
    public async Task ReserveTokensAsync_WhenValidRequest_ReturnsSuccessResponse()
    {
        var expectedResp = new ReserveTokensResponseDto
        {
            Success = true,
            ReservationId = "res_abc_1",
            ReservedAmount = 100m,
            AvailableBalance = 900m,
            OverdraftRemaining = 200m
        };

        using var handler = new MockHttpMessageHandler(req =>
        {
            req.Method.Should().Be(HttpMethod.Post);
            req.RequestUri!.AbsolutePath.Should().EndWith("/v1/tokens/reserve");

            var json = JsonSerializer.Serialize(expectedResp, AchillesProtocolJsonContext.Default.ReserveTokensResponseDto);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };
        });

        using var client = CreateClient(handler);
        var result = await client.ReserveTokensAsync(new ReserveTokensRequestDto
        {
            WalletId = "wlt_1",
            FeatureCode = "ai_inference",
            EstimatedUnits = 100m
        });

        result.Success.Should().BeTrue();
        result.ReservationId.Should().Be("res_abc_1");
        result.ReservedAmount.Should().Be(100m);
        result.AvailableBalance.Should().Be(900m);
    }

    [Fact]
    public async Task HeartbeatTokensAsync_WhenValidRequest_ReturnsUpdatedBalance()
    {
        var expectedResp = new HeartbeatTokensResponseDto
        {
            Success = true,
            TotalConsumed = 50m,
            RemainingReserved = 50m,
            AvailableBalance = 850m
        };

        using var handler = new MockHttpMessageHandler(req =>
        {
            req.Method.Should().Be(HttpMethod.Post);
            req.RequestUri!.AbsolutePath.Should().EndWith("/v1/tokens/heartbeat");

            var json = JsonSerializer.Serialize(expectedResp, AchillesProtocolJsonContext.Default.HeartbeatTokensResponseDto);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };
        });

        using var client = CreateClient(handler);
        var result = await client.HeartbeatTokensAsync(new HeartbeatTokensRequestDto
        {
            ReservationId = "res_abc_1",
            DeltaUnits = 50m
        });

        result.Success.Should().BeTrue();
        result.TotalConsumed.Should().Be(50m);
        result.RemainingReserved.Should().Be(50m);
        result.AvailableBalance.Should().Be(850m);
    }

    [Fact]
    public async Task CommitTokensAsync_WhenValidRequest_ReturnsCommitSettlement()
    {
        var expectedResp = new CommitTokensResponseDto
        {
            Success = true,
            ConsumedCredits = 75m,
            RefundedCredits = 25m,
            NewBalance = 925m
        };

        using var handler = new MockHttpMessageHandler(req =>
        {
            req.Method.Should().Be(HttpMethod.Post);
            req.RequestUri!.AbsolutePath.Should().EndWith("/v1/tokens/commit");

            var json = JsonSerializer.Serialize(expectedResp, AchillesProtocolJsonContext.Default.CommitTokensResponseDto);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };
        });

        using var client = CreateClient(handler);
        var result = await client.CommitTokensAsync(new CommitTokensRequestDto
        {
            ReservationId = "res_abc_1",
            ActualUnits = 75m
        });

        result.Success.Should().BeTrue();
        result.ConsumedCredits.Should().Be(75m);
        result.RefundedCredits.Should().Be(25m);
        result.NewBalance.Should().Be(925m);
    }

    [Fact]
    public async Task RollbackTokensAsync_WhenValidRequest_ReturnsRollbackResponse()
    {
        var expectedResp = new RollbackTokensResponseDto
        {
            Success = true,
            RestoredCredits = 100m,
            NewBalance = 1000m
        };

        using var handler = new MockHttpMessageHandler(req =>
        {
            req.Method.Should().Be(HttpMethod.Post);
            req.RequestUri!.AbsolutePath.Should().EndWith("/v1/tokens/rollback");

            var json = JsonSerializer.Serialize(expectedResp, AchillesProtocolJsonContext.Default.RollbackTokensResponseDto);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };
        });

        using var client = CreateClient(handler);
        var result = await client.RollbackTokensAsync(new RollbackTokensRequestDto
        {
            ReservationId = "res_abc_1",
            Reason = "Operation failed"
        });

        result.Success.Should().BeTrue();
        result.RestoredCredits.Should().Be(100m);
        result.NewBalance.Should().Be(1000m);
    }

    [Fact]
    public async Task GetTokenWalletBalanceAsync_WhenWalletExists_ReturnsBalance()
    {
        var expectedResp = new TokenWalletBalanceResponseDto
        {
            WalletId = "wlt_enterprise",
            WalletCode = "ENT_WALLET",
            WalletName = "Enterprise Primary Wallet",
            TotalCredits = 6000m,
            Balance = 5000m,
            ReservedCredits = 500m,
            AvailableBalance = 4500m,
            OverdraftLimit = 500m,
            State = "Active",
            IsLowBalance = false
        };

        using var handler = new MockHttpMessageHandler(req =>
        {
            req.Method.Should().Be(HttpMethod.Get);
            req.RequestUri!.AbsolutePath.Should().Contain("/v1/tokens/wallets/wlt_enterprise/balance");

            var json = JsonSerializer.Serialize(expectedResp, AchillesProtocolJsonContext.Default.TokenWalletBalanceResponseDto);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };
        });

        using var client = CreateClient(handler);
        var result = await client.GetTokenWalletBalanceAsync("wlt_enterprise");

        result.Should().NotBeNull();
        result!.WalletId.Should().Be("wlt_enterprise");
        result.Balance.Should().Be(5000m);
        result.AvailableBalance.Should().Be(4500m);
        result.OverdraftLimit.Should().Be(500m);
        result.State.Should().Be("Active");
    }

    [Fact]
    public async Task BeginMeteredScopeAsync_WhenCommittedExplicitly_DoesNotRollbackOnDispose()
    {
        int rollbackCalls = 0;
        int commitCalls = 0;

        using var handler = new MockHttpMessageHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path.EndsWith("/v1/tokens/reserve", StringComparison.Ordinal))
            {
                var resp = new ReserveTokensResponseDto
                {
                    Success = true,
                    ReservationId = "res_scope_1",
                    ReservedAmount = 100m,
                    AvailableBalance = 900m
                };
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(resp, AchillesProtocolJsonContext.Default.ReserveTokensResponseDto), System.Text.Encoding.UTF8, "application/json")
                };
            }

            if (path.EndsWith("/v1/tokens/commit", StringComparison.Ordinal))
            {
                commitCalls++;
                var resp = new CommitTokensResponseDto
                {
                    Success = true,
                    ConsumedCredits = 80m,
                    RefundedCredits = 20m,
                    NewBalance = 920m
                };
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(resp, AchillesProtocolJsonContext.Default.CommitTokensResponseDto), System.Text.Encoding.UTF8, "application/json")
                };
            }

            if (path.EndsWith("/v1/tokens/rollback", StringComparison.Ordinal))
            {
                rollbackCalls++;
                var resp = new RollbackTokensResponseDto { Success = true, RestoredCredits = 100m, NewBalance = 1000m };
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(resp, AchillesProtocolJsonContext.Default.RollbackTokensResponseDto), System.Text.Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var client = CreateClient(handler);

        await using (var scope = await client.BeginMeteredScopeAsync("wlt_1", "render_3d", 100m))
        {
            scope.ReservationId.Should().Be("res_scope_1");
            scope.ReservedAmount.Should().Be(100m);
            scope.AvailableBalance.Should().Be(900m);

            var commitResult = await scope.CommitAsync(actualUnits: 80m);
            commitResult.Success.Should().BeTrue();
            scope.IsCompleted.Should().BeTrue();
            scope.AvailableBalance.Should().Be(920m);
        }

        commitCalls.Should().Be(1);
        rollbackCalls.Should().Be(0, "explicit commit should prevent automatic rollback on dispose");
    }

    [Fact]
    public async Task BeginMeteredScopeAsync_WhenDisposedWithoutCommit_AutomaticallyRollsBack()
    {
        int rollbackCalls = 0;

        using var handler = new MockHttpMessageHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path.EndsWith("/v1/tokens/reserve", StringComparison.Ordinal))
            {
                var resp = new ReserveTokensResponseDto
                {
                    Success = true,
                    ReservationId = "res_auto_rollback",
                    ReservedAmount = 50m,
                    AvailableBalance = 950m
                };
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(resp, AchillesProtocolJsonContext.Default.ReserveTokensResponseDto), System.Text.Encoding.UTF8, "application/json")
                };
            }

            if (path.EndsWith("/v1/tokens/rollback", StringComparison.Ordinal))
            {
                rollbackCalls++;
                var resp = new RollbackTokensResponseDto { Success = true, RestoredCredits = 50m, NewBalance = 1000m };
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(resp, AchillesProtocolJsonContext.Default.RollbackTokensResponseDto), System.Text.Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var client = CreateClient(handler);

        await using (var scope = await client.BeginMeteredScopeAsync("wlt_1", "batch_proc", 50m))
        {
            scope.ReservationId.Should().Be("res_auto_rollback");
            // Simulate unhandled exception or early exit without calling scope.CommitAsync
        }

        rollbackCalls.Should().Be(1, "disposing uncommitted scope must automatically roll back reserved tokens");
    }

    [Fact]
    public async Task BeginMeteredScopeAsync_SynchronousDisposeWithoutCommit_AutomaticallyRollsBack()
    {
        int rollbackCalls = 0;

        using var handler = new MockHttpMessageHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path.EndsWith("/v1/tokens/reserve", StringComparison.Ordinal))
            {
                var resp = new ReserveTokensResponseDto
                {
                    Success = true,
                    ReservationId = "res_sync_rollback",
                    ReservedAmount = 30m,
                    AvailableBalance = 970m
                };
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(resp, AchillesProtocolJsonContext.Default.ReserveTokensResponseDto), System.Text.Encoding.UTF8, "application/json")
                };
            }

            if (path.EndsWith("/v1/tokens/rollback", StringComparison.Ordinal))
            {
                rollbackCalls++;
                var resp = new RollbackTokensResponseDto { Success = true, RestoredCredits = 30m, NewBalance = 1000m };
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(resp, AchillesProtocolJsonContext.Default.RollbackTokensResponseDto), System.Text.Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var client = CreateClient(handler);

        using (var scope = await client.BeginMeteredScopeAsync("wlt_1", "sync_task", 30m))
        {
            scope.ReservationId.Should().Be("res_sync_rollback");
            // Normal exit without commit
        }

        rollbackCalls.Should().Be(1, "synchronous dispose of uncommitted scope must roll back tokens");
    }
}
