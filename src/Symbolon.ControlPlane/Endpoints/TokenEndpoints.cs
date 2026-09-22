using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Symbolon.Domain.Tokens;

namespace Symbolon.ControlPlane.Endpoints;

public static class TokenEndpoints
{
    public static void MapTokenEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // 1. Client Metered Endpoints (/v1/tokens)
        var clientGroup = app.MapGroup("/v1/tokens")
            .WithTags("Token & Metered Licensing");

        clientGroup.MapPost("/reserve", async (ReserveTokensRequest request, ITokenEngine engine, CancellationToken ct) =>
        {
            var result = await engine.ReserveTokensAsync(request, ct).ConfigureAwait(false);
            return result.Success
                ? Results.Ok(result)
                : Results.BadRequest(result);
        });

        clientGroup.MapPost("/heartbeat", async (HeartbeatTokensRequest request, ITokenEngine engine, CancellationToken ct) =>
        {
            var result = await engine.HeartbeatTokensAsync(request, ct).ConfigureAwait(false);
            return result.Success
                ? Results.Ok(result)
                : Results.BadRequest(result);
        });

        clientGroup.MapPost("/commit", async (CommitTokensRequest request, ITokenEngine engine, CancellationToken ct) =>
        {
            var result = await engine.CommitTokensAsync(request, ct).ConfigureAwait(false);
            return result.Success
                ? Results.Ok(result)
                : Results.BadRequest(result);
        });

        clientGroup.MapPost("/rollback", async (RollbackTokensRequest request, ITokenEngine engine, CancellationToken ct) =>
        {
            var result = await engine.RollbackTokensAsync(request, ct).ConfigureAwait(false);
            return result.Success
                ? Results.Ok(result)
                : Results.BadRequest(result);
        });

        clientGroup.MapGet("/wallets/{id}/balance", async (string id, ITokenEngine engine, CancellationToken ct) =>
        {
            var balance = await engine.GetBalanceAsync(id, ct).ConfigureAwait(false);
            return balance is not null
                ? Results.Ok(balance)
                : Results.NotFound(new { error = $"Wallet '{id}' not found." });
        });

        // 2. Admin Management Endpoints (/admin/v1/tokens)
        var adminGroup = app.MapGroup("/admin/v1/tokens")
            .WithTags("Admin Tokens");

        adminGroup.MapGet("/wallets", async (string? tenantId, ITokenEngine engine, CancellationToken ct) =>
        {
            var wallets = await engine.ListWalletsAsync(tenantId, ct).ConfigureAwait(false);
            return Results.Ok(wallets);
        });

        adminGroup.MapPost("/wallets", async (CreateWalletRequest request, ITokenEngine engine, CancellationToken ct) =>
        {
            var wallet = await engine.CreateWalletAsync(request, ct).ConfigureAwait(false);
            return Results.Created($"/admin/v1/tokens/wallets/{wallet.Id}", wallet);
        });

        adminGroup.MapPost("/wallets/{id}/credit", async (string id, CreditWalletBody body, ITokenEngine engine, CancellationToken ct) =>
        {
            var req = new CreditWalletRequest(id, body.Amount, body.Reason ?? "Manual administrator credit", body.IdempotencyKey);
            var updated = await engine.CreditWalletAsync(req, ct).ConfigureAwait(false);
            return Results.Ok(updated);
        });

        adminGroup.MapGet("/rates", async (string? tenantId, string? productId, ITokenEngine engine, CancellationToken ct) =>
        {
            var rates = await engine.ListRatesAsync(tenantId, productId, ct).ConfigureAwait(false);
            return Results.Ok(rates);
        });

        adminGroup.MapPost("/rates", async (SetRateRequest request, ITokenEngine engine, CancellationToken ct) =>
        {
            var rate = await engine.SetRateAsync(request, ct).ConfigureAwait(false);
            return Results.Ok(rate);
        });

        adminGroup.MapGet("/ledger", async (string? walletId, int? limit, ITokenEngine engine, CancellationToken ct) =>
        {
            var entries = await engine.ListLedgerAsync(walletId, limit ?? 50, ct).ConfigureAwait(false);
            return Results.Ok(entries);
        });
    }
}

public sealed record CreditWalletBody(decimal Amount, string? Reason = null, string? IdempotencyKey = null);
