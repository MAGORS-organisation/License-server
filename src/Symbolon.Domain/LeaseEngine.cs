using Microsoft.Extensions.Logging;

namespace Symbolon.Domain;

public sealed record CheckoutCommand(
    string LicenseId,
    string Fingerprint,
    string? MachineId,
    int Quantity,
    IReadOnlyList<string>? Features,
    string? IdempotencyKey,
    bool AllowQueue,
    TimeSpan Ttl);

public sealed record CheckoutResult(
    bool IsSuccess,
    IReadOnlyList<SeatAllocation>? Allocations,
    IReadOnlyList<string>? Tokens,
    string? Reason,
    TimeSpan? EstimatedWait = null,
    string? QueueTicket = null)
{
    public static CheckoutResult Ok(SeatAllocation[] allocations, IReadOnlyList<string> tokens) =>
        new(true, allocations, tokens, null);

    public static CheckoutResult Invalid(string reason) =>
        new(false, null, null, reason);

    public static CheckoutResult PoolExhausted(TimeSpan? estimatedWait) =>
        new(false, null, null, "seat-pool-exhausted", estimatedWait);

    public static CheckoutResult Queued(string ticket) =>
        new(false, null, null, "queued", null, ticket);
}

public sealed record RenewResult(
    bool IsSuccess,
    SeatAllocation? Allocation,
    string? Token,
    string? Reason)
{
    public static RenewResult Ok(SeatAllocation allocation, string token) =>
        new(true, allocation, token, null);

    public static RenewResult Conflict(string reason) =>
        new(false, null, null, reason);

    public static RenewResult Gone(string reason) =>
        new(false, null, null, reason);
}

/// <summary>
/// Domain engine controlling floating seat lifecycle: checkout, atomic acquisition, renewal, and release.
/// Conforms to docs/08-referencna-implementacia.md §8.4 and spec/07-floating-protokol.md.
/// </summary>
public sealed partial class LeaseEngine
{
    private readonly ISeatStore _seats;
    private readonly ILeaseTokenIssuer _tokens;
    private readonly IAuditLedger _audit;
    private readonly TimeProvider _time;
    private readonly ILogger<LeaseEngine> _log;

    public LeaseEngine(
        ISeatStore seats,
        ILeaseTokenIssuer tokens,
        IAuditLedger audit,
        TimeProvider? time = null,
        ILogger<LeaseEngine>? log = null)
    {
        _seats = seats ?? throw new ArgumentNullException(nameof(seats));
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _time = time ?? TimeProvider.System;
        _log = log ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<LeaseEngine>.Instance;
    }

    public async Task<CheckoutResult> CheckoutAsync(CheckoutCommand req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);

        var now = _time.GetUtcNow();

        if (req.Quantity is < 1 or > 64)
        {
            return CheckoutResult.Invalid("quantity-out-of-range");
        }

        // 1. Idempotency: same key in window => return already issued lease
        if (req.IdempotencyKey is { } ik &&
            await _seats.TryGetIdempotentAsync(req.LicenseId, ik, now, ct).ConfigureAwait(false) is { } existing)
        {
            LogIdempotentCheckout(_log, ik);
            var existingTokens = _tokens.Issue(existing, req.Features);
            return CheckoutResult.Ok(existing, existingTokens);
        }

        // 2. Atomic seat acquisition
        SeatAllocation[]? allocated = req.Quantity == 1
            ? await _seats.TryAcquireOneAsync(req.LicenseId, req.Fingerprint, req.MachineId, now, req.Ttl, ct).ConfigureAwait(false)
                is { } one ? [one] : null
            : await _seats.TryAcquireManyAsync(req.LicenseId, req.Fingerprint, req.MachineId, req.Quantity, now, req.Ttl, ct).ConfigureAwait(false);

        // 3. Pool exhausted
        if (allocated is null)
        {
            LogPoolExhausted(_log, req.LicenseId);
            await _audit.AppendAsync(new AuditEvent("deny", req.LicenseId, null, req.Fingerprint, now, "seat-pool-exhausted"), ct).ConfigureAwait(false);

            if (req.AllowQueue)
            {
                string ticket = $"q_{Guid.NewGuid():N}";
                return CheckoutResult.Queued(ticket);
            }

            var wait = await _seats.EstimateWaitAsync(req.LicenseId, now, ct).ConfigureAwait(false);
            return CheckoutResult.PoolExhausted(wait);
        }

        // 4. Save idempotency record if key was provided
        if (req.IdempotencyKey is { } key)
        {
            await _seats.SaveIdempotentAsync(req.LicenseId, key, allocated, now, req.Ttl, ct).ConfigureAwait(false);
        }

        // 5. Issue signed lease tokens and append audit
        var tokens = _tokens.Issue(allocated, req.Features);
        await _audit.AppendAsync(new AuditEvent("checkout", req.LicenseId, allocated[0].LeaseId, req.Fingerprint, now, $"seats={allocated.Length}"), ct).ConfigureAwait(false);

        return CheckoutResult.Ok(allocated, tokens);
    }

    public async Task<RenewResult> RenewAsync(
        string leaseId,
        string fingerprint,
        long clientSeq,
        TimeSpan ttl,
        TimeSpan resurrectionWindow,
        IReadOnlyList<string>? features = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);

        var now = _time.GetUtcNow();
        var outcome = await _seats.TryRenewAsync(leaseId, fingerprint, clientSeq, now, ttl, resurrectionWindow, ct).ConfigureAwait(false);

        switch (outcome.Type)
        {
            case RenewOutcomeType.Renewed:
            case RenewOutcomeType.Resurrected:
                var token = _tokens.Issue(outcome.Allocation!, features);
                await _audit.AppendAsync(new AuditEvent("renew", outcome.Allocation!.LicenseId, leaseId, fingerprint, now, $"seq={outcome.Allocation.LeaseSeq}"), ct).ConfigureAwait(false);
                return RenewResult.Ok(outcome.Allocation!, token);

            case RenewOutcomeType.Taken:
                LogSeatReassigned(_log, leaseId);
                return RenewResult.Gone("seat-reassigned");

            case RenewOutcomeType.SeqReplay:
                LogStaleSequence(_log, leaseId);
                return RenewResult.Conflict("stale-sequence");

            case RenewOutcomeType.Conflict:
                LogHolderMismatch(_log, leaseId);
                return RenewResult.Conflict("fingerprint-mismatch");

            case RenewOutcomeType.Unknown:
            default:
                LogLeaseUnknown(_log, leaseId);
                return RenewResult.Gone("lease-unknown");
        }
    }

    public async Task<bool> ReleaseAsync(string leaseId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseId);

        var now = _time.GetUtcNow();
        bool released = await _seats.TryReleaseAsync(leaseId, now, ct).ConfigureAwait(false);
        if (released)
        {
            await _audit.AppendAsync(new AuditEvent("release", "unknown", leaseId, null, now, null), ct).ConfigureAwait(false);
        }

        return released;
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Idempotent checkout hit for key {IdempotencyKey}")]
    private static partial void LogIdempotentCheckout(ILogger logger, string idempotencyKey);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Checkout denied: seat pool exhausted for license {LicenseId}")]
    private static partial void LogPoolExhausted(ILogger logger, string licenseId);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Renew failed: seat reassigned for lease {LeaseId}")]
    private static partial void LogSeatReassigned(ILogger logger, string leaseId);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "Renew conflict: stale sequence for lease {LeaseId}")]
    private static partial void LogStaleSequence(ILogger logger, string leaseId);

    [LoggerMessage(EventId = 5, Level = LogLevel.Warning, Message = "Renew conflict: holder mismatch for lease {LeaseId}")]
    private static partial void LogHolderMismatch(ILogger logger, string leaseId);

    [LoggerMessage(EventId = 6, Level = LogLevel.Warning, Message = "Renew failed: lease unknown {LeaseId}")]
    private static partial void LogLeaseUnknown(ILogger logger, string leaseId);
}
