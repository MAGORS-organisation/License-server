using System.Text;
using Microsoft.AspNetCore.Mvc;
using Achilles.ControlPlane.Billing;
using Achilles.Domain.Billing;
using Achilles.Protocol;

namespace Achilles.ControlPlane.Endpoints;

public static class BillingEndpoints
{
    public const string DefaultStripeSecret = "whsec_stripe_test_secret_1234567890";
    public const string DefaultLemonSqueezySecret = "whsec_lemonsqueezy_test_secret_1234567890";
    public const string DefaultPaddleSecret = "whsec_paddle_test_secret_1234567890";

    public static RouteGroupBuilder MapBillingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/billing").WithTags("Billing Webhooks");

        group.MapPost("/stripe/webhook", HandleStripeWebhookAsync)
            .WithName("StripeBillingWebhook")
            .WithSummary("Spracuje prichádzajúci Stripe webhook (checkout.session.completed, subscription lifecycle).");

        group.MapPost("/lemonsqueezy/webhook", HandleLemonSqueezyWebhookAsync)
            .WithName("LemonSqueezyBillingWebhook")
            .WithSummary("Spracuje prichádzajúci LemonSqueezy webhook (order_created, subscription events).");

        group.MapPost("/paddle/webhook", HandlePaddleWebhookAsync)
            .WithName("PaddleBillingWebhook")
            .WithSummary("Spracuje prichádzajúci Paddle v2 webhook (transaction.completed, subscription events).");

        return group;
    }

    private static async Task<IResult> HandleStripeWebhookAsync(
        HttpContext context,
        BillingLicenseProvisioner provisioner,
        CancellationToken ct)
    {
        string? signatureHeader = context.Request.Headers["Stripe-Signature"].FirstOrDefault();
        string secret = Environment.GetEnvironmentVariable("STRIPE_WEBHOOK_SECRET") ?? DefaultStripeSecret;

        using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
        string rawPayload = await reader.ReadToEndAsync(ct).ConfigureAwait(false);

        var processor = new StripeBillingProcessor();
        if (!processor.VerifySignature(secret, signatureHeader, rawPayload))
        {
            return TypedResults.Unauthorized();
        }

#pragma warning disable CA1031 // Do not catch general exception types (webhook payload processing should fail gracefully to HTTP 400)
        try
        {
            var evt = processor.ParseEvent(rawPayload);
            var result = await provisioner.ProcessAsync(evt, ct).ConfigureAwait(false);

            return TypedResults.Ok(new BillingWebhookResponseDto(
                result.Success,
                result.Action,
                result.LicenseId,
                result.LicenseKey,
                result.Message,
                result.CustomerId,
                result.SubscriptionId));
        }
        catch (Exception ex)
        {
            return TypedResults.BadRequest(new BillingWebhookResponseDto(
                false,
                "error",
                null,
                null,
                $"Failed to process Stripe webhook: {ex.Message}"));
        }
#pragma warning restore CA1031
    }

    private static async Task<IResult> HandleLemonSqueezyWebhookAsync(
        HttpContext context,
        BillingLicenseProvisioner provisioner,
        CancellationToken ct)
    {
        string? signatureHeader = context.Request.Headers["X-Signature"].FirstOrDefault();
        string secret = Environment.GetEnvironmentVariable("LEMONSQUEEZY_WEBHOOK_SECRET") ?? DefaultLemonSqueezySecret;

        using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
        string rawPayload = await reader.ReadToEndAsync(ct).ConfigureAwait(false);

        var processor = new LemonSqueezyBillingProcessor();
        if (!processor.VerifySignature(secret, signatureHeader, rawPayload))
        {
            return TypedResults.Unauthorized();
        }

#pragma warning disable CA1031
        try
        {
            var evt = processor.ParseEvent(rawPayload);
            var result = await provisioner.ProcessAsync(evt, ct).ConfigureAwait(false);

            return TypedResults.Ok(new BillingWebhookResponseDto(
                result.Success,
                result.Action,
                result.LicenseId,
                result.LicenseKey,
                result.Message,
                result.CustomerId,
                result.SubscriptionId));
        }
        catch (Exception ex)
        {
            return TypedResults.BadRequest(new BillingWebhookResponseDto(
                false,
                "error",
                null,
                null,
                $"Failed to process LemonSqueezy webhook: {ex.Message}"));
        }
#pragma warning restore CA1031
    }

    private static async Task<IResult> HandlePaddleWebhookAsync(
        HttpContext context,
        BillingLicenseProvisioner provisioner,
        CancellationToken ct)
    {
        string? signatureHeader = context.Request.Headers["Paddle-Signature"].FirstOrDefault();
        string secret = Environment.GetEnvironmentVariable("PADDLE_WEBHOOK_SECRET") ?? DefaultPaddleSecret;

        using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
        string rawPayload = await reader.ReadToEndAsync(ct).ConfigureAwait(false);

        var processor = new PaddleBillingProcessor();
        if (!processor.VerifySignature(secret, signatureHeader, rawPayload))
        {
            return TypedResults.Unauthorized();
        }

#pragma warning disable CA1031
        try
        {
            var evt = processor.ParseEvent(rawPayload);
            var result = await provisioner.ProcessAsync(evt, ct).ConfigureAwait(false);

            return TypedResults.Ok(new BillingWebhookResponseDto(
                result.Success,
                result.Action,
                result.LicenseId,
                result.LicenseKey,
                result.Message,
                result.CustomerId,
                result.SubscriptionId));
        }
        catch (Exception ex)
        {
            return TypedResults.BadRequest(new BillingWebhookResponseDto(
                false,
                "error",
                null,
                null,
                $"Failed to process Paddle webhook: {ex.Message}"));
        }
#pragma warning restore CA1031
    }
}
