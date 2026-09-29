using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Symbolon.Domain;
using Symbolon.Domain.PolicyRules;
using Symbolon.Format;
using Symbolon.Protocol;

namespace Symbolon.Relay;

internal static class LeaseEndpoints
{
    public static RouteGroupBuilder MapLeases(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1")
                       .WithTags("Leases");

        group.MapPost("/leases", CheckoutAsync)
             .WithName("CheckoutSeat")
             .WithSummary("Vyžiada sedadlo z floating poolu licencie.")
             .Produces<CheckoutResponseDto>(StatusCodes.Status200OK)
             .Produces<QueuedResponseDto>(StatusCodes.Status202Accepted)
             .ProducesProblem(StatusCodes.Status400BadRequest)
             .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/leases/{id}/renew", RenewAsync)
             .WithName("RenewLease")
             .WithSummary("Obnoví existujúci lease.")
             .Produces<RenewResponseDto>(StatusCodes.Status200OK)
             .ProducesProblem(StatusCodes.Status409Conflict)
             .ProducesProblem(StatusCodes.Status410Gone);

        group.MapDelete("/leases/{id}", ReleaseAsync)
             .WithName("ReleaseLease")
             .WithSummary("Explicitne uvoľní sedadlo späť do poolu.")
             .Produces<ReleaseResponseDto>(StatusCodes.Status200OK)
             .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/queue/{ticket}", GetQueueStatusAsync)
             .WithName("GetQueueStatus")
             .WithSummary("Vráti stav čakania v rade.")
             .Produces<QueueStatusResponseDto>(StatusCodes.Status200OK)
             .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/queue/{ticket}", CancelQueueTicketAsync)
             .WithName("CancelQueueTicket")
             .WithSummary("Zruší čakanie v rade.")
             .Produces(StatusCodes.Status204NoContent)
             .ProducesProblem(StatusCodes.Status404NotFound);

        return group;
    }

    private static async Task<IResult> CheckoutAsync(
        [FromBody] CheckoutRequestDto dto,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        HttpContext httpContext,
        LeaseEngine engine,
        RelayQueueManager queueManager,
        RelayOptionsManager optionsManager,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(dto);

        // Validate license key format & checksum before processing (KEY-8)
        if (!LicenseKey.TryParse(dto.LicenseKey, out var key, out string? keyError))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                type: ProblemTypes.InvalidRequest,
                title: "Invalid License Key",
                detail: $"Preklep v kľúči: {keyError}");
        }

        string fingerprintHash = dto.ToFingerprintHash();
        string licenseId = key.Canonical;

        // Policy rules check (FLT-24)
        var rules = optionsManager.GetRules(licenseId);
        string? reservationTarget = null;
        int? ruleDerivedPriority = null;

        if (rules is not null)
        {
            string? clientIp = httpContext.Connection.RemoteIpAddress?.ToString();
            var evalContext = new RuleEvaluationContext(
                LicenseId: licenseId,
                UserId: dto.UserId,
                MachineId: dto.MachineId,
                HostName: dto.MachineId,
                ClientIp: clientIp,
                Features: dto.Features,
                CurrentlyHeldByClient: 0,
                GetActiveCountForTarget: _ => 0);

            var evalResult = PolicyRuleEngine.Evaluate(rules, evalContext);
            if (!evalResult.Allowed)
            {
                if (evalResult.DenyType == "group-quota-exceeded")
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status409Conflict,
                        type: ProblemTypes.GroupQuotaExceeded,
                        title: "Group Quota Exceeded",
                        detail: evalResult.DenyReason);
                }

                return Results.Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    type: ProblemTypes.RuleDenied,
                    title: "Access Denied by Policy Rule",
                    detail: evalResult.DenyReason);
            }

            reservationTarget = evalResult.MatchedReservationTarget;
            ruleDerivedPriority = evalResult.ResolvedPriority;
        }

        var command = new CheckoutCommand(
            LicenseId: licenseId,
            Fingerprint: fingerprintHash,
            MachineId: dto.MachineId,
            Quantity: dto.Quantity ?? 1,
            Features: dto.Features,
            IdempotencyKey: idempotencyKey,
            AllowQueue: dto.AllowQueue ?? false,
            Ttl: TimeSpan.FromMinutes(10),
            ReservationTarget: reservationTarget);

        var result = await engine.CheckoutAsync(command, ct).ConfigureAwait(false);

        if (result.IsSuccess && result.Allocations is { Count: > 0 } && result.Tokens is { Count: > 0 })
        {
            var first = result.Allocations[0];
            var response = new CheckoutResponseDto
            {
                LeaseId = first.LeaseId ?? string.Empty,
                Token = result.Tokens[0],
                ExpiresAt = first.ExpiresAt,
                Seat = first.SeatNo,
                Entitlements = dto.Features ?? ["core"]
            };
            return Results.Ok(response);
        }

        if (result.QueueTicket is not null)
        {
            var ticket = queueManager.Enqueue(
                licenseId,
                fingerprintHash,
                dto.MachineId,
                dto.Quantity ?? 1,
                dto.Features,
                dto.Priority ?? ruleDerivedPriority ?? 0,
                TimeSpan.FromMinutes(10));

            int position = queueManager.GetPosition(ticket);
            httpContext.Response.Headers.RetryAfter = "3";

            return Results.Accepted(
                $"/v1/queue/{ticket.Ticket}",
                new QueuedResponseDto
                {
                    Ticket = ticket.Ticket,
                    Position = position,
                    Priority = ticket.Priority,
                    EstimatedWait = "PT2M",
                    RetryAfterSeconds = 3
                });
        }

        if (string.Equals(result.Reason, "seat-pool-exhausted", StringComparison.Ordinal))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                type: ProblemTypes.PoolExhausted,
                title: "Seat Pool Exhausted",
                detail: "Všetky sedadlá sú momentálne obsadené.");
        }

        return Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            type: ProblemTypes.InvalidRequest,
            title: "Invalid Request",
            detail: result.Reason);
    }

    private static async Task<IResult> RenewAsync(
        string id,
        [FromBody] RenewRequestDto dto,
        LeaseEngine engine,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(dto);

        string? fingerprintHash = dto.ResolveFingerprintHash();
        if (string.IsNullOrWhiteSpace(fingerprintHash))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                type: ProblemTypes.InvalidRequest,
                title: "Missing Fingerprint",
                detail: "Obnova vyžaduje fingerprint alebo fingerprintComponents.");
        }

        var result = await engine.RenewAsync(
            leaseId: id,
            fingerprint: fingerprintHash,
            clientSeq: dto.ClientSeq,
            ttl: TimeSpan.FromMinutes(10),
            resurrectionWindow: TimeSpan.FromMinutes(5),
            ct: ct).ConfigureAwait(false);

        if (result.IsSuccess && result.Allocation is not null && result.Token is not null)
        {
            return Results.Ok(new RenewResponseDto
            {
                Token = result.Token,
                ExpiresAt = result.Allocation.ExpiresAt,
                LeaseSeq = result.Allocation.LeaseSeq
            });
        }

        return result.Reason switch
        {
            "stale-sequence" => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                type: ProblemTypes.StaleSequence,
                title: "Stale Sequence",
                detail: "Poradie požiadavky nezodpovedá aktuálnej sekvencii lease."),

            "fingerprint-mismatch" => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                type: ProblemTypes.FingerprintMismatch,
                title: "Holder Mismatch",
                detail: "Sedadlo patrí inému zariadeniu."),

            "seat-reassigned" => Results.Problem(
                statusCode: StatusCodes.Status410Gone,
                type: ProblemTypes.SeatReassigned,
                title: "Seat Reassigned",
                detail: "Sedadlo vypršalo a bolo pridelené inému klientovi."),

            _ => Results.Problem(
                statusCode: StatusCodes.Status410Gone,
                type: ProblemTypes.LeaseUnknown,
                title: "Lease Unknown",
                detail: "Zadaný lease neexistuje.")
        };
    }

    private static async Task<IResult> ReleaseAsync(
        string id,
        LeaseEngine engine,
        CancellationToken ct)
    {
        bool released = await engine.ReleaseAsync(id, ct).ConfigureAwait(false);
        return released
            ? Results.Ok(new ReleaseResponseDto { Success = true })
            : Results.NotFound(new ReleaseResponseDto { Success = false });
    }

    private static async Task<IResult> GetQueueStatusAsync(
        string ticket,
        HttpContext httpContext,
        RelayQueueManager queueManager,
        CancellationToken ct)
    {
        var status = await queueManager.GetStatusAsync(ticket, ct).ConfigureAwait(false);
        if (status is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                type: ProblemTypes.QueueNotFound,
                title: "Queue Ticket Not Found",
                detail: $"Queue ticket '{ticket}' was not found.");
        }

        if (status.Status == "waiting" && status.RetryAfterSeconds.HasValue)
        {
            httpContext.Response.Headers.RetryAfter = status.RetryAfterSeconds.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return Results.Ok(status);
    }

    private static IResult CancelQueueTicketAsync(
        string ticket,
        RelayQueueManager queueManager)
    {
        bool cancelled = queueManager.Cancel(ticket);
        if (!cancelled)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                type: ProblemTypes.QueueNotFound,
                title: "Queue Ticket Not Found",
                detail: $"Waiting queue ticket '{ticket}' was not found or is already completed.");
        }

        return Results.NoContent();
    }
}
