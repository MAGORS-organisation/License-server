using System.Net;
using FluentAssertions;
using Achilles.Crypto;
using Achilles.Format;
using Achilles.Protocol;
using Xunit;

namespace Achilles.Client.Tests;

public sealed class SeatLeaseDisposeTests
{
    private sealed class SyncMockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public SyncMockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
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
    public async Task Synchronous_Dispose_CompletesWithoutDeadlock()
    {
        bool deleteCalled = false;
        var handler = new SyncMockHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Delete)
            {
                deleteCalled = true;
            }
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://licenses.example.com") };

        // Test running synchronous Dispose inside a dedicated Task to verify completion within timeout
        var disposeTask = Task.Run(() =>
        {
            var lease = new SeatLease(
                acquired: true,
                reason: null,
                leaseId: "lse_dispose_test",
                token: "dummy.jwt.token",
                seatNo: 1,
                expiresAt: DateTimeOffset.UtcNow.AddMinutes(10),
                entitlements: ["core"],
                http: http,
                time: TimeProvider.System,
                heartbeatInterval: TimeSpan.FromSeconds(30),
                gracePeriod: TimeSpan.FromMinutes(1),
                fingerprint: new Dictionary<string, string>());

            // Act: synchronous Dispose must complete cleanly and call DELETE
            lease.Dispose();

            // Assert: idempotent second dispose does not throw
            lease.Dispose();

            return lease.State;
        });

        var completedTask = await Task.WhenAny(disposeTask, Task.Delay(5000));
        completedTask.Should().BeSameAs(disposeTask, "Dispose() should complete promptly without deadlocking");
        var finalState = await disposeTask;
        finalState.Should().Be(SeatState.Released);
        deleteCalled.Should().BeTrue();
    }
}
