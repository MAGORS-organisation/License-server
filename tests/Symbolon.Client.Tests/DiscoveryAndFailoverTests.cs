using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using FluentAssertions;
using Symbolon.Client.Discovery;
using Symbolon.Crypto;
using Symbolon.Format;
using Symbolon.Protocol;
using Xunit;

namespace Symbolon.Client.Tests;

public sealed class DiscoveryAndFailoverTests
{
    [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated in tests")]
    private sealed class MockFailoverHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public MockFailoverHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }

    [Fact]
    public void SymbolonServerResolver_ParsesFlexNetNotation_Correctly()
    {
        var uris = SymbolonServerResolver.Resolve("27000@lic1.corp.com", fallbackToEnvironment: false);

        uris.Should().HaveCount(1);
        uris[0].Scheme.Should().Be("http");
        uris[0].Host.Should().Be("lic1.corp.com");
        uris[0].Port.Should().Be(27000);
    }

    [Fact]
    public void SymbolonServerResolver_ParsesMultipleDelimitedServers_Correctly()
    {
        string input = "27000@lic1.corp.com;https://backup-lic.corp.com:8443,@lic3.local;corp-srv:9000";
        var uris = SymbolonServerResolver.Resolve(input, fallbackToEnvironment: false);

        uris.Should().HaveCount(4);
        uris[0].Should().Be(new Uri("http://lic1.corp.com:27000/"));
        uris[1].Should().Be(new Uri("https://backup-lic.corp.com:8443/"));
        uris[2].Should().Be(new Uri("http://lic3.local:8080/"));
        uris[3].Should().Be(new Uri("http://corp-srv:9000/"));
    }

    [Fact]
    public void SymbolonServerResolver_FallsBackToEnvironmentVariable()
    {
        string testVar = "SYMBOLON_LICENSE_SERVER";
        string original = Environment.GetEnvironmentVariable(testVar) ?? string.Empty;

        try
        {
            Environment.SetEnvironmentVariable(testVar, "28500@env-lic.internal");
            var uris = SymbolonServerResolver.Resolve(fallbackToEnvironment: true);

            uris.Should().NotBeEmpty();
            uris[0].Host.Should().Be("env-lic.internal");
            uris[0].Port.Should().Be(28500);
        }
        finally
        {
            Environment.SetEnvironmentVariable(testVar, string.IsNullOrEmpty(original) ? null : original);
        }
    }

    [Fact]
    public async Task ServerFailoverPool_FailsOverTransparently_WhenPrimaryFails()
    {
        var primaryUri = new Uri("http://primary.invalid:5000");
        var secondaryUri = new Uri("http://secondary.invalid:5000");

        var pool = new ServerFailoverPool(
            [primaryUri, secondaryUri],
            cooldownDuration: TimeSpan.FromMinutes(5));

        int attempts = 0;
        var calledUris = new List<Uri>();

        var result = await pool.ExecuteWithFailoverAsync<string>(async (uri, client, ct) =>
        {
            attempts++;
            calledUris.Add(uri);

            if (uri == primaryUri)
            {
                throw new HttpRequestException("Primary node connection refused");
            }

            return await Task.FromResult("secondary-success");
        });

        result.Should().Be("secondary-success");
        attempts.Should().Be(2);
        calledUris.Should().ContainInOrder([primaryUri, secondaryUri]);

        var nodes = pool.GetNodes();
        var primaryStatus = nodes.First(n => n.ServerUri == primaryUri);
        primaryStatus.ConsecutiveFailures.Should().Be(1);
        primaryStatus.IsHealthy.Should().BeFalse();

        var secondaryStatus = nodes.First(n => n.ServerUri == secondaryUri);
        secondaryStatus.ConsecutiveFailures.Should().Be(0);
        secondaryStatus.IsHealthy.Should().BeTrue();
    }

    [Fact]
    public async Task ServerFailoverPool_ThrowsExhaustedException_WhenAllNodesFail()
    {
        var pool = new ServerFailoverPool([
            new Uri("http://node1.invalid:5000"),
            new Uri("http://node2.invalid:5000")
        ]);

        var act = async () => await pool.ExecuteWithFailoverAsync<string>((uri, client, ct) =>
        {
            throw new HttpRequestException($"Failed to reach {uri}");
        });

        await act.Should().ThrowAsync<SymbolonFailoverExhaustedException>()
            .WithMessage("*All 2 server(s)*");
    }

    [Fact]
    public async Task SymbolonDiscoveryClient_ReceivesAnnouncement_FromLocalResponder()
    {
        int testPort = 17584; // use isolated port for test
        using var udpResponder = new UdpClient();
        udpResponder.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udpResponder.Client.Bind(new IPEndPoint(IPAddress.Loopback, testPort));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Start background responder task
        var responderTask = Task.Run(async () =>
        {
            var res = await udpResponder.ReceiveAsync(cts.Token);
            var announcement = new DiscoveryAnnouncementPacket
            {
                Magic = SymbolonDiscoveryConstants.MagicServer,
                NodeId = "test-node-1",
                ServerUrl = "http://127.0.0.1:5055",
                ServerType = "controlplane",
                ClusterId = "test-cluster",
                Version = "1.0.0"
            };

            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(announcement, SymbolonProtocolJsonContext.Default.DiscoveryAnnouncementPacket);
            await udpResponder.SendAsync(bytes, bytes.Length, res.RemoteEndPoint);
        }, cts.Token);

        // Discovery client probe
        var discovered = await SymbolonDiscoveryClient.DiscoverServersAsync(
            productCode: null,
            timeout: TimeSpan.FromMilliseconds(1000),
            port: testPort,
            broadcastAddress: IPAddress.Loopback,
            ct: cts.Token);

        await responderTask;

        discovered.Should().NotBeEmpty();
        var server = discovered[0];
        server.Announcement.NodeId.Should().Be("test-node-1");
        server.Announcement.ServerUrl.Should().Be("http://127.0.0.1:5055");
        server.RoundTripMs.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task SymbolonClient_WithMultipleServerUris_ExecutesFailoverDuringAcquisition()
    {
        var validKey = LicenseKey.Generate();
        using var signingKey = Es256SignatureProvider.GenerateKey("test-key");
        var tokenSigner = new LeaseTokenSigner(signingKey);
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string dummyJwt = tokenSigner.IssueToken(new LeaseClaims
        {
            Iss = "relay:rly_secondary",
            Sub = "lic_1",
            Jti = "lse_999",
            Iat = now,
            Exp = now + 600,
            Seat = 1,
            Fp = "sha256:0000000000000000000000000000000000000000000000000000000000000000",
            Ent = ["core"],
            Seq = 0
        });

        int callCount = 0;
        var handler = new MockFailoverHttpMessageHandler(req =>
        {
            callCount++;
            if (req.RequestUri!.Host == "primary-dead.local")
            {
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }

            // Secondary succeeds
            var responseDto = new CheckoutResponseDto
            {
                LeaseId = "lse_999",
                Token = dummyJwt,
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
                Seat = 1,
                Entitlements = ["core"]
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(responseDto, SymbolonProtocolJsonContext.Default.CheckoutResponseDto),
                    System.Text.Encoding.UTF8,
                    "application/json")
            };
        });

        using var httpClient = new HttpClient(handler);

        var options = new SymbolonClientOptions
        {
            ServerUris = [
                new Uri("http://primary-dead.local:5000"),
                new Uri("http://secondary-alive.local:5000")
            ],
            LicenseKey = validKey.ToString(),
            ProductCode = "cad-test",
            HttpClient = httpClient
        };

        var client = new SymbolonClient(options);
        var lease = await client.AcquireSeatAsync();

        lease.Should().NotBeNull();
        lease.SeatNo.Should().Be(1);
        callCount.Should().Be(2); // First failed, second succeeded
    }
}
