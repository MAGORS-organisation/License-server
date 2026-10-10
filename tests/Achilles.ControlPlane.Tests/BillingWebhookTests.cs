using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Achilles.ControlPlane.Endpoints;
using Achilles.Data;
using Achilles.Domain.Webhooks;
using Achilles.Protocol;
using Xunit;

namespace Achilles.ControlPlane.Tests;

public sealed class BillingWebhookTests : IClassFixture<ControlPlaneFactory>
{
    private readonly HttpClient _client;
    private readonly ControlPlaneFactory _factory;

    public BillingWebhookTests(ControlPlaneFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Stripe_CheckoutCompleted_ProvisionsLicense_And_CancellationRevokes()
    {
        string secret = BillingEndpoints.DefaultStripeSecret;
        string customerId = $"cus_stripe_{Guid.NewGuid():N}"[..18];
        string customerEmail = "stripe_user@example.com";
        string subId = $"sub_stripe_{Guid.NewGuid():N}"[..18];
        long nowSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // 1. Provision via checkout.session.completed
        var payloadObj = new
        {
            id = $"evt_{Guid.NewGuid():N}",
            type = "checkout.session.completed",
            data = new
            {
                @object = new
                {
                    id = $"cs_{Guid.NewGuid():N}",
                    customer = customerId,
                    customer_details = new
                    {
                        email = customerEmail,
                        name = "Stripe Enterprise Corp"
                    },
                    subscription = subId,
                    metadata = new
                    {
                        seats = "4",
                        productCode = "cad-pro"
                    }
                }
            }
        };

        string rawPayload = JsonSerializer.Serialize(payloadObj);
        string sigHeader = WebhookSecurity.BuildSignatureHeader(secret, nowSec, rawPayload);

        using var req = new HttpRequestMessage(HttpMethod.Post, "/v1/billing/stripe/webhook")
        {
            Content = new StringContent(rawPayload, Encoding.UTF8, "application/json")
        };
        req.Headers.Add("Stripe-Signature", sigHeader);

        var res = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var resultDto = await res.Content.ReadFromJsonAsync<BillingWebhookResponseDto>();
        Assert.NotNull(resultDto);
        Assert.True(resultDto.Success);
        Assert.Equal("provisioned", resultDto.Action);
        Assert.NotNull(resultDto.LicenseId);
        Assert.NotNull(resultDto.LicenseKey);
        Assert.StartsWith("SYM-", resultDto.LicenseKey, StringComparison.Ordinal);

        // Verify database state: license and exactly 4 seats
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AchillesDbContext>();
            var lic = await db.Licenses.Include(l => l.Seats).FirstOrDefaultAsync(l => l.Id == resultDto.LicenseId);
            Assert.NotNull(lic);
            Assert.Equal("active", lic.State);
            Assert.Equal(4, lic.MaxSeats);
            Assert.Equal(4, lic.Seats.Count);
            Assert.Equal(subId, lic.CustomerRef);
        }

        // 2. Renewal via customer.subscription.updated
        long renewPeriodEnd = nowSec + 86400 * 365 * 2; // 2 years ahead
        var renewObj = new
        {
            id = $"evt_{Guid.NewGuid():N}",
            type = "customer.subscription.updated",
            data = new
            {
                @object = new
                {
                    id = subId,
                    customer = customerId,
                    status = "active",
                    current_period_end = renewPeriodEnd
                }
            }
        };

        string renewPayload = JsonSerializer.Serialize(renewObj);
        string renewSigHeader = WebhookSecurity.BuildSignatureHeader(secret, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), renewPayload);

        using var renewReq = new HttpRequestMessage(HttpMethod.Post, "/v1/billing/stripe/webhook")
        {
            Content = new StringContent(renewPayload, Encoding.UTF8, "application/json")
        };
        renewReq.Headers.Add("Stripe-Signature", renewSigHeader);

        var renewRes = await _client.SendAsync(renewReq);
        Assert.Equal(HttpStatusCode.OK, renewRes.StatusCode);
        var renewResult = await renewRes.Content.ReadFromJsonAsync<BillingWebhookResponseDto>();
        Assert.NotNull(renewResult);
        Assert.True(renewResult.Success);
        Assert.Equal("renewed", renewResult.Action);

        // 3. Revocation via customer.subscription.deleted
        var deleteObj = new
        {
            id = $"evt_{Guid.NewGuid():N}",
            type = "customer.subscription.deleted",
            data = new
            {
                @object = new
                {
                    id = subId,
                    customer = customerId,
                    status = "canceled"
                }
            }
        };

        string delPayload = JsonSerializer.Serialize(deleteObj);
        string delSigHeader = WebhookSecurity.BuildSignatureHeader(secret, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), delPayload);

        using var delReq = new HttpRequestMessage(HttpMethod.Post, "/v1/billing/stripe/webhook")
        {
            Content = new StringContent(delPayload, Encoding.UTF8, "application/json")
        };
        delReq.Headers.Add("Stripe-Signature", delSigHeader);

        var delRes = await _client.SendAsync(delReq);
        Assert.Equal(HttpStatusCode.OK, delRes.StatusCode);
        var delResult = await delRes.Content.ReadFromJsonAsync<BillingWebhookResponseDto>();
        Assert.NotNull(delResult);
        Assert.True(delResult.Success);
        Assert.Equal("revoked", delResult.Action);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AchillesDbContext>();
            var lic = await db.Licenses.FirstOrDefaultAsync(l => l.Id == resultDto.LicenseId);
            Assert.NotNull(lic);
            Assert.Equal("revoked", lic.State);
        }
    }

    [Fact]
    public async Task Stripe_TamperedSignature_And_Replay_Rejected()
    {
        string secret = BillingEndpoints.DefaultStripeSecret;
        string rawPayload = JsonSerializer.Serialize(new { type = "checkout.session.completed" });

        // 1. Invalid signature
        using var badSigReq = new HttpRequestMessage(HttpMethod.Post, "/v1/billing/stripe/webhook")
        {
            Content = new StringContent(rawPayload, Encoding.UTF8, "application/json")
        };
        badSigReq.Headers.Add("Stripe-Signature", "t=12345678,v1=deadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeef");
        var badSigRes = await _client.SendAsync(badSigReq);
        Assert.Equal(HttpStatusCode.Unauthorized, badSigRes.StatusCode);

        // 2. Replay attack (> 5 minutes in the past)
        long oldTs = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 600;
        string replaySig = WebhookSecurity.BuildSignatureHeader(secret, oldTs, rawPayload);
        using var replayReq = new HttpRequestMessage(HttpMethod.Post, "/v1/billing/stripe/webhook")
        {
            Content = new StringContent(rawPayload, Encoding.UTF8, "application/json")
        };
        replayReq.Headers.Add("Stripe-Signature", replaySig);
        var replayRes = await _client.SendAsync(replayReq);
        Assert.Equal(HttpStatusCode.Unauthorized, replayRes.StatusCode);
    }

    [Fact]
    public async Task LemonSqueezy_OrderCreated_ProvisionsLicense_And_TamperedRejected()
    {
        string secret = BillingEndpoints.DefaultLemonSqueezySecret;
        string userEmail = "lemonsqueezy_user@example.com";
        string subId = $"ls_sub_{Guid.NewGuid():N}"[..16];

        var payloadObj = new
        {
            meta = new
            {
                event_name = "order_created",
                custom_data = new
                {
                    seats = "5",
                    productCode = "cad-lemon"
                }
            },
            data = new
            {
                id = subId,
                attributes = new
                {
                    customer_id = 123456,
                    user_email = userEmail,
                    user_name = "Lemon User",
                    status = "active",
                    renews_at = DateTimeOffset.UtcNow.AddYears(1)
                }
            }
        };

        string rawPayload = JsonSerializer.Serialize(payloadObj);
        byte[] keyBytes = Encoding.UTF8.GetBytes(secret);
        byte[] hash = HMACSHA256.HashData(keyBytes, Encoding.UTF8.GetBytes(rawPayload));
        string validSignature = Convert.ToHexString(hash);

        // 1. Valid request
        using var req = new HttpRequestMessage(HttpMethod.Post, "/v1/billing/lemonsqueezy/webhook")
        {
            Content = new StringContent(rawPayload, Encoding.UTF8, "application/json")
        };
        req.Headers.Add("X-Signature", validSignature);

        var res = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var result = await res.Content.ReadFromJsonAsync<BillingWebhookResponseDto>();
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal("provisioned", result.Action);
        Assert.NotNull(result.LicenseId);
        Assert.NotNull(result.LicenseKey);

        // 2. Tampered signature -> 401 Unauthorized
        using var tamperedReq = new HttpRequestMessage(HttpMethod.Post, "/v1/billing/lemonsqueezy/webhook")
        {
            Content = new StringContent(rawPayload, Encoding.UTF8, "application/json")
        };
        tamperedReq.Headers.Add("X-Signature", "00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff");

        var tamperedRes = await _client.SendAsync(tamperedReq);
        Assert.Equal(HttpStatusCode.Unauthorized, tamperedRes.StatusCode);
    }

    [Fact]
    public async Task Paddle_TransactionCompleted_And_PastDueSuspension()
    {
        string secret = BillingEndpoints.DefaultPaddleSecret;
        string customerId = $"ctm_{Guid.NewGuid():N}"[..16];
        string subId = $"sub_pad_{Guid.NewGuid():N}"[..16];
        long nowSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var payloadObj = new
        {
            event_id = $"evt_{Guid.NewGuid():N}",
            event_type = "transaction.completed",
            data = new
            {
                id = subId,
                customer_id = customerId,
                status = "active",
                items = new[]
                {
                    new { quantity = 2, price = new { id = "pri_annual" } }
                },
                custom_data = new
                {
                    email = "paddle_client@example.com",
                    seats = "2"
                }
            }
        };

        string rawPayload = JsonSerializer.Serialize(payloadObj);
        string stringToSign = $"{nowSec}:{rawPayload}";
        byte[] paddleKey = Encoding.UTF8.GetBytes(secret);
        byte[] paddleHash = HMACSHA256.HashData(paddleKey, Encoding.UTF8.GetBytes(stringToSign));
        string signatureHeader = $"ts={nowSec};h1={Convert.ToHexString(paddleHash)}";

        // 1. Transaction completed -> provision
        using var req = new HttpRequestMessage(HttpMethod.Post, "/v1/billing/paddle/webhook")
        {
            Content = new StringContent(rawPayload, Encoding.UTF8, "application/json")
        };
        req.Headers.Add("Paddle-Signature", signatureHeader);

        var res = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var result = await res.Content.ReadFromJsonAsync<BillingWebhookResponseDto>();
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal("provisioned", result.Action);
        Assert.NotNull(result.LicenseId);

        // 2. Subscription past_due -> suspension
        var pastDueObj = new
        {
            event_id = $"evt_{Guid.NewGuid():N}",
            event_type = "subscription.past_due",
            data = new
            {
                id = subId,
                customer_id = customerId,
                status = "past_due"
            }
        };

        string pastDuePayload = JsonSerializer.Serialize(pastDueObj);
        long pastDueNowSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string pastDueStringToSign = $"{pastDueNowSec}:{pastDuePayload}";
        byte[] pastDueHash = HMACSHA256.HashData(paddleKey, Encoding.UTF8.GetBytes(pastDueStringToSign));
        string pastDueSigHeader = $"ts={pastDueNowSec};h1={Convert.ToHexString(pastDueHash)}";

        using var pastDueReq = new HttpRequestMessage(HttpMethod.Post, "/v1/billing/paddle/webhook")
        {
            Content = new StringContent(pastDuePayload, Encoding.UTF8, "application/json")
        };
        pastDueReq.Headers.Add("Paddle-Signature", pastDueSigHeader);

        var pastDueRes = await _client.SendAsync(pastDueReq);
        Assert.Equal(HttpStatusCode.OK, pastDueRes.StatusCode);
        var pastDueResult = await pastDueRes.Content.ReadFromJsonAsync<BillingWebhookResponseDto>();
        Assert.NotNull(pastDueResult);
        Assert.True(pastDueResult.Success);
        Assert.Equal("suspended", pastDueResult.Action);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AchillesDbContext>();
            var lic = await db.Licenses.FirstOrDefaultAsync(l => l.Id == result.LicenseId);
            Assert.NotNull(lic);
            Assert.Equal("suspended", lic.State);
        }
    }
}
