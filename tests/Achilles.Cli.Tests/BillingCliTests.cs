using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Achilles.Domain.Webhooks;
using Xunit;

namespace Achilles.Cli.Tests;

public sealed class BillingCliTests
{
    [Fact]
    public async Task BillingHelp_ReturnsZero()
    {
        int exitCode = await Program.Main(["billing", "--help"]);
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task Billing_WithoutArgs_ReturnsZero()
    {
        int exitCode = await Program.Main(["billing"]);
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task Billing_UnknownCommand_ReturnsNonZero()
    {
        int exitCode = await Program.Main(["billing", "nonexistent-command"]);
        exitCode.Should().Be(1);
    }

    [Fact]
    public async Task VerifySig_Stripe_ValidSignature_ReturnsZero()
    {
        string secret = "whsec_test_secret_1234567890";
        string payload = JsonSerializer.Serialize(new { type = "checkout.session.completed" });
        long nowSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string header = WebhookSecurity.BuildSignatureHeader(secret, nowSec, payload);

        int exitCode = await Program.Main([
            "billing", "verify-sig",
            "--provider", "stripe",
            "--secret", secret,
            "--header", header,
            "--payload", payload
        ]);

        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task VerifySig_LemonSqueezy_ValidSignature_ReturnsZero()
    {
        string secret = "whsec_lemon_secret_1234567890";
        string payload = JsonSerializer.Serialize(new { event_name = "order_created" });
        byte[] keyBytes = Encoding.UTF8.GetBytes(secret);
        byte[] hash = HMACSHA256.HashData(keyBytes, Encoding.UTF8.GetBytes(payload));
        string header = Convert.ToHexString(hash);

        int exitCode = await Program.Main([
            "billing", "verify-sig",
            "--provider", "lemonsqueezy",
            "--secret", secret,
            "--header", header,
            "--payload", payload
        ]);

        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task VerifySig_Paddle_ValidSignature_ReturnsZero()
    {
        string secret = "whsec_paddle_secret_1234567890";
        string payload = JsonSerializer.Serialize(new { event_type = "transaction.completed" });
        long nowSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string stringToSign = $"{nowSec}:{payload}";
        byte[] keyBytes = Encoding.UTF8.GetBytes(secret);
        byte[] hash = HMACSHA256.HashData(keyBytes, Encoding.UTF8.GetBytes(stringToSign));
        string header = $"ts={nowSec};h1={Convert.ToHexString(hash)}";

        int exitCode = await Program.Main([
            "billing", "verify-sig",
            "--provider", "paddle",
            "--secret", secret,
            "--header", header,
            "--payload", payload
        ]);

        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task VerifySig_InvalidSignature_ReturnsNonZero()
    {
        string secret = "whsec_test_secret_1234567890";
        string payload = "{}";
        string badHeader = "t=12345678,v1=badbadbadbadbadbadbadbadbadbadbadbadbadbadbadbadbadbadbadbadbadbad";

        int exitCode = await Program.Main([
            "billing", "verify-sig",
            "--provider", "stripe",
            "--secret", secret,
            "--header", badHeader,
            "--payload", payload
        ]);

        exitCode.Should().Be(1);
    }
}
