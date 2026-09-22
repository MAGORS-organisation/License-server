using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Symbolon.ControlPlane.Models;
using Symbolon.Crypto;
using Symbolon.Data;
using Symbolon.Data.Entities;
using Symbolon.Domain;
using Symbolon.Domain.Security;
using Symbolon.Format;
using Symbolon.Protocol;

namespace Symbolon.ControlPlane.Endpoints;

public static class PublicEndpoints
{
    public static RouteGroupBuilder MapPublicEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1").WithTags("Public");

        group.MapPost("/leases", CheckoutAsync)
            .WithName("CheckoutSeat")
            .WithSummary("Vyžiada sedadlo z floating poolu licencie.");

        group.MapPost("/leases/{id}/renew", RenewAsync)
            .WithName("RenewLease")
            .WithSummary("Predĺži platnosť lease tokenu (heartbeat).");

        group.MapDelete("/leases/{id}", ReleaseAsync)
            .WithName("ReleaseLease")
            .WithSummary("Uvoľní pridelené sedadlo.");

        group.MapPost("/leases/{id}/borrow", BorrowAsync)
            .WithName("BorrowLease")
            .WithSummary("Vypožičia sedadlo pre offline roaming.");

        group.MapPost("/activations", ActivateAsync)
            .WithName("ActivateMachine")
            .WithSummary("Aktivuje uzol/stroj pre node-locked licenciu.");

        group.MapDelete("/activations/{id}", DeactivateAsync)
            .WithName("DeactivateMachine")
            .WithSummary("Deaktivuje stroj.");

        group.MapGet("/licenses/{key}/file", GetLicenseFileAsync)
            .WithName("GetLicenseFile")
            .WithSummary("Vydá podpísaný .symlic licenčný súbor.");

        group.MapGet("/revocations/latest", GetRevocationsAsync)
            .WithName("GetRevocations")
            .WithSummary("Vráti zoznam aktívnych revokácií.");

        group.MapGet("/.well-known/symbolon-keys", GetJwks)
            .WithName("GetJwks")
            .WithSummary("Vráti JWKS verejných kľúčov.");

        group.MapPost("/offline/requests", ProcessOfflineRequest)
            .WithName("ProcessOfflineRequest")
            .WithSummary("Spracuje offline .symreq požiadavku.");

        group.MapGet("/queue/{ticket}", GetQueueStatusAsync)
            .WithName("GetQueueStatus")
            .WithSummary("Zistí aktuálny stav čakajúcej požiadavky vo fronte.");

        group.MapDelete("/queue/{ticket}", CancelQueueAsync)
            .WithName("CancelQueue")
            .WithSummary("Zruší požiadavku vo fronte na sedadlo.");

        group.MapGet("/system/telemetry", GetTelemetry)
            .WithName("GetSystemTelemetry")
            .WithSummary("Vráti živé systémové metriky servera (CPU, RAM, DISK, NET, IP, User).");

        return group;
    }

    private static async Task<IResult> CheckoutAsync(
        CheckoutRequestDto dto,
        HttpContext context,
        SymbolonDbContext db,
        LeaseEngine engine,
        Queuing.IQueueManager queueManager,
        Webhooks.IWebhookDispatcher webhooks,
        TimeProvider time,
        Observability.SymbolonMetrics metrics,
        Alerting.IAlertService alertService,
        IFraudDetectionService fraudDetection,
        CancellationToken ct)
    {
        string? idempotencyKey = context.Request.Headers["Idempotency-Key"].FirstOrDefault();
        byte[] rawKeyBytes = Encoding.UTF8.GetBytes(dto.LicenseKey.Trim());
        byte[] lookup = SHA256.HashData(rawKeyBytes)[..4];
        string fullHashHex = Convert.ToHexString(SHA256.HashData(rawKeyBytes));

        var license = await db.Licenses
            .Include(l => l.Policy)
            .FirstOrDefaultAsync(l => l.KeyLookup == lookup, ct)
            .ConfigureAwait(false);

        if (license is null || license.State != "active" || !string.Equals(license.KeyHash, fullHashHex, StringComparison.OrdinalIgnoreCase))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "License Not Found",
                type: ProblemTypes.LicenseNotFound);
        }

        var now = time.GetUtcNow();
        if (license.ExpiresAt.HasValue && license.ExpiresAt.Value < now)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "License Expired",
                type: ProblemTypes.LicenseNotFound);
        }

        // Named User Enforcement (§4.3)
        if (string.Equals(license.Policy?.LicenseModel, "named-user", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(dto.UserId))
            {
                return TypedResults.Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "User Identification Required",
                    detail: "This policy enforces named-user licensing. Parameter 'userId' is required.",
                    type: ProblemTypes.UserNotAuthorized);
            }

            bool authorized = await db.LicenseUsers.AnyAsync(
                u => u.LicenseId == license.Id && (u.UserId == dto.UserId || u.GroupName == dto.UserId), ct).ConfigureAwait(false);

            if (!authorized)
            {
                return TypedResults.Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "User Not Authorized",
                    detail: $"User '{dto.UserId}' is not assigned to this named-user license.",
                    type: ProblemTypes.UserNotAuthorized);
            }
        }

        string fingerprint = dto.ToFingerprintHash();

        // Anti-Fraud, Impossible Travel & VM Cloning Evaluation
        string? clientIp = context.Request.Headers["X-Forwarded-For"].FirstOrDefault()
            ?? context.Connection.RemoteIpAddress?.ToString();

        GeoLocation? explicitLocation = null;
        if (double.TryParse(context.Request.Headers["X-Geo-Lat"].FirstOrDefault(), CultureInfo.InvariantCulture, out double lat) &&
            double.TryParse(context.Request.Headers["X-Geo-Lon"].FirstOrDefault(), CultureInfo.InvariantCulture, out double lon))
        {
            string city = context.Request.Headers["X-Geo-City"].FirstOrDefault() ?? "Custom";
            string country = context.Request.Headers["X-Geo-Country"].FirstOrDefault() ?? "Custom";
            explicitLocation = new GeoLocation(lat, lon, city, country);
        }

        var fraudEvent = new FraudAccessEvent(
            LicenseId: license.Id,
            FingerprintHash: fingerprint,
            FingerprintComponents: dto.FingerprintComponents,
            MachineId: dto.MachineId,
            UserId: dto.UserId,
            IpAddress: clientIp,
            Location: explicitLocation,
            Timestamp: now);

        var fraudAssessment = await fraudDetection.EvaluateAccessAsync(fraudEvent, ct).ConfigureAwait(false);
        if (fraudAssessment.IsSuspicious)
        {
            await alertService.TriggerSecurityAlertAsync(
                fraudAssessment.RiskType ?? "fraud_detected",
                license.TenantId,
                fraudAssessment.Description ?? "Security anomaly detected during seat checkout.",
                ct).ConfigureAwait(false);
        }

        int quantity = dto.Quantity ?? 1;
        var ttl = TimeSpan.FromSeconds(license.Policy?.LeaseTtlSeconds ?? 600);

        var cmd = new CheckoutCommand(
            license.Id,
            fingerprint,
            dto.MachineId,
            quantity,
            dto.Features,
            idempotencyKey,
            dto.AllowQueue ?? false,
            ttl);

        var result = await engine.CheckoutAsync(cmd, ct).ConfigureAwait(false);

        if (result.IsSuccess && result.Allocations is { Count: > 0 } && result.Tokens is { Count: > 0 })
        {
            metrics.RecordSeatAcquired(quantity);

            // Enterprise Alerting (Phase 2.5)
            int activeSeats = await db.Seats.CountAsync(s => s.LicenseId == license.Id && s.LeaseId != null && s.ExpiresAt > now, ct).ConfigureAwait(false);
            await alertService.CheckCapacityThresholdAsync(license.Id, activeSeats, license.MaxSeats, license.TenantId, ct).ConfigureAwait(false);

            var alloc = result.Allocations[0];
            var entitlements = dto.Features ?? ["core"];
            return TypedResults.Ok(new CheckoutResponseDto
            {
                LeaseId = alloc.LeaseId ?? string.Empty,
                Token = result.Tokens[0],
                ExpiresAt = alloc.ExpiresAt,
                Seat = alloc.SeatNo,
                Entitlements = entitlements
            });
        }

        if (result.QueueTicket is not null || (dto.AllowQueue == true && result.Reason == "seat-pool-exhausted"))
        {
            var ticket = await queueManager.EnqueueAsync(
                license.Id,
                fingerprint,
                dto.MachineId,
                dto.UserId,
                quantity,
                dto.Features,
                ttl: TimeSpan.FromMinutes(10),
                ct).ConfigureAwait(false);

            return TypedResults.Accepted($"/v1/queue/{ticket.Ticket}", new QueuedResponseDto
            {
                Ticket = ticket.Ticket,
                Position = 1,
                EstimatedWait = "PT2M"
            });
        }

        metrics.RecordCheckoutDenied(license.Id);
        await alertService.RecordDenialSpikeAsync(license.Id, license.TenantId, result.Reason ?? "seat-pool-exhausted", ct).ConfigureAwait(false);

        await webhooks.PublishEventAsync("seat.denied", new
        {
            licenseId = license.Id,
            tenantId = license.TenantId,
            customerRef = license.CustomerRef,
            fingerprint,
            reason = result.Reason ?? "seat-pool-exhausted",
            requestedQuantity = quantity,
            maxSeats = license.MaxSeats
        }, license.TenantId, ct).ConfigureAwait(false);

        if (result.Reason == "seat-pool-exhausted")
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Seat Pool Exhausted",
                type: ProblemTypes.PoolExhausted);
        }

        return TypedResults.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Checkout Denied",
            detail: result.Reason ?? "Unable to acquire seat.",
            type: ProblemTypes.InvalidRequest);
    }

    private static async Task<IResult> RenewAsync(
        string id,
        RenewRequestDto dto,
        LeaseEngine engine,
        Observability.SymbolonMetrics metrics,
        CancellationToken ct)
    {
        string? fp = dto.ResolveFingerprintHash();
        if (string.IsNullOrWhiteSpace(fp))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Missing Fingerprint",
                type: ProblemTypes.InvalidRequest);
        }

        var result = await engine.RenewAsync(
            id,
            fp,
            dto.ClientSeq,
            TimeSpan.FromMinutes(10),
            TimeSpan.FromMinutes(5),
            null,
            ct).ConfigureAwait(false);

        if (result.IsSuccess && result.Allocation is not null && result.Token is not null)
        {
            metrics.RecordLeaseRenewed();
            return TypedResults.Ok(new RenewResponseDto
            {
                Token = result.Token,
                ExpiresAt = result.Allocation.ExpiresAt,
                LeaseSeq = result.Allocation.LeaseSeq
            });
        }

        return result.Reason switch
        {
            "stale-sequence" => TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Stale Sequence Replay",
                type: ProblemTypes.StaleSequence),
            "seat-reassigned" => TypedResults.Problem(
                statusCode: StatusCodes.Status410Gone,
                title: "Seat Reassigned",
                type: ProblemTypes.SeatReassigned),
            "lease-unknown" => TypedResults.Problem(
                statusCode: StatusCodes.Status410Gone,
                title: "Lease Unknown",
                type: ProblemTypes.LeaseUnknown),
            "fingerprint-mismatch" => TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Fingerprint Mismatch",
                type: ProblemTypes.FingerprintMismatch),
            _ => TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid Request",
                type: ProblemTypes.InvalidRequest)
        };
    }

    private static async Task<IResult> ReleaseAsync(
        string id,
        SymbolonDbContext db,
        LeaseEngine engine,
        Queuing.IQueueManager queueManager,
        Observability.SymbolonMetrics metrics,
        CancellationToken ct)
    {
        var seat = await db.Seats.FirstOrDefaultAsync(s => s.LeaseId == id, ct).ConfigureAwait(false);
        string? licenseId = seat?.LicenseId;

        bool released = await engine.ReleaseAsync(id, ct).ConfigureAwait(false);
        if (released)
        {
            metrics.RecordSeatReleased(1);
            if (!string.IsNullOrWhiteSpace(licenseId))
            {
                await queueManager.TryPromoteNextAsync(licenseId, ct).ConfigureAwait(false);
            }
        }
        return TypedResults.Ok(new ReleaseResponseDto { Success = released });
    }

    private static async Task<IResult> BorrowAsync(
        string id,
        BorrowRequestDto dto,
        SymbolonDbContext db,
        ILeaseTokenIssuer tokenIssuer,
        TimeProvider time,
        CancellationToken ct)
    {
        var seat = await db.Seats
            .Include(s => s.License)
            .FirstOrDefaultAsync(s => s.LeaseId == id, ct)
            .ConfigureAwait(false);

        if (seat is null)
        {
            return TypedResults.Problem(statusCode: 404, title: "Lease Not Found", type: ProblemTypes.LeaseUnknown);
        }

        var now = time.GetUtcNow();
        var borrowedUntil = now.AddDays(dto.Days);

        seat.BorrowedUntil = borrowedUntil;
        seat.ExpiresAt = borrowedUntil;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var alloc = new SeatAllocation
        {
            SeatId = seat.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            SeatNo = seat.SeatNo,
            LicenseId = seat.LicenseId,
            LeaseId = seat.LeaseId,
            HolderFingerprint = seat.HolderFp is not null ? Encoding.UTF8.GetString(seat.HolderFp) : string.Empty,
            ExpiresAt = borrowedUntil,
            IsOverage = seat.IsOverage,
            LeaseSeq = seat.LeaseSeq
        };

        string token = tokenIssuer.Issue(alloc);
        return TypedResults.Ok(new BorrowResponseDto(seat.LeaseId!, borrowedUntil, token));
    }

    private static async Task<IResult> ActivateAsync(
        ActivationRequestDto dto,
        SymbolonDbContext db,
        IAuditLedger audit,
        Webhooks.IWebhookDispatcher webhooks,
        TimeProvider time,
        CancellationToken ct)
    {
        byte[] rawKeyBytes = Encoding.UTF8.GetBytes(dto.LicenseKey.Trim());
        byte[] lookup = SHA256.HashData(rawKeyBytes)[..4];
        string fullHashHex = Convert.ToHexString(SHA256.HashData(rawKeyBytes));

        var license = await db.Licenses
            .Include(l => l.Policy)
            .Include(l => l.Machines)
            .FirstOrDefaultAsync(l => l.KeyLookup == lookup, ct)
            .ConfigureAwait(false);

        if (license is null || license.State != "active" || !string.Equals(license.KeyHash, fullHashHex, StringComparison.OrdinalIgnoreCase))
        {
            return TypedResults.Problem(statusCode: 404, title: "License Not Found", type: ProblemTypes.LicenseNotFound);
        }

        string fp = FingerprintHelper.ComputeHash(dto.FingerprintComponents);
        var existing = license.Machines.FirstOrDefault(m => m.Fingerprint == fp);
        var now = time.GetUtcNow();

        if (existing is not null)
        {
            existing.LastHeartbeat = now;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return TypedResults.Ok(new ActivationResponseDto(existing.Id, license.Id, fp, existing.State, existing.FirstSeen));
        }

        int activeMachines = license.Machines.Count(m => m.State == "active");
        if (activeMachines >= license.MaxSeats)
        {
            return TypedResults.Problem(statusCode: 409, title: "Max Machine Activations Reached", type: ProblemTypes.PoolExhausted);
        }

        string machineId = $"mch_{Guid.NewGuid():N}";
        var machine = new MachineEntity
        {
            Id = machineId,
            LicenseId = license.Id,
            Fingerprint = fp,
            ComponentsJson = JsonSerializer.Serialize(dto.FingerprintComponents),
            FirstSeen = now,
            LastHeartbeat = now,
            State = "active"
        };

        db.Machines.Add(machine);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await audit.AppendAsync(new AuditEvent("activate", license.Id, null, fp, now, $"Node-lock machine activated: {dto.MachineId}"), ct).ConfigureAwait(false);

        await webhooks.PublishEventAsync("machine.activated", new
        {
            activationId = machine.Id,
            licenseId = license.Id,
            tenantId = license.TenantId,
            fingerprint = fp,
            machineId = dto.MachineId
        }, license.TenantId, ct).ConfigureAwait(false);

        return TypedResults.Ok(new ActivationResponseDto(machine.Id, license.Id, fp, "active", now));
    }

    private static async Task<IResult> DeactivateAsync(
        string id,
        SymbolonDbContext db,
        IAuditLedger audit,
        Webhooks.IWebhookDispatcher webhooks,
        TimeProvider time,
        CancellationToken ct)
    {
        var machine = await db.Machines.FirstOrDefaultAsync(m => m.Id == id, ct).ConfigureAwait(false);
        if (machine is null)
        {
            return TypedResults.NotFound();
        }

        machine.State = "deactivated";
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var now = time.GetUtcNow();
        await audit.AppendAsync(new AuditEvent("deactivate", machine.LicenseId, null, machine.Fingerprint, now, "Machine deactivated"), ct).ConfigureAwait(false);

        await webhooks.PublishEventAsync("machine.deactivated", new
        {
            activationId = machine.Id,
            licenseId = machine.LicenseId,
            fingerprint = machine.Fingerprint
        }, null, ct).ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> GetLicenseFileAsync(
        string key,
        SymbolonDbContext db,
        ISignatureProvider signingKey,
        TimeProvider time,
        CancellationToken ct)
    {
        byte[] rawKeyBytes = Encoding.UTF8.GetBytes(key.Trim());
        byte[] lookup = SHA256.HashData(rawKeyBytes)[..4];
        string fullHashHex = Convert.ToHexString(SHA256.HashData(rawKeyBytes));

        var license = await db.Licenses
            .Include(l => l.Policy)
            .FirstOrDefaultAsync(l => l.KeyLookup == lookup, ct)
            .ConfigureAwait(false);

        if (license is null || license.State != "active" || !string.Equals(license.KeyHash, fullHashHex, StringComparison.OrdinalIgnoreCase))
        {
            return TypedResults.Problem(statusCode: 404, title: "License Not Found", type: ProblemTypes.LicenseNotFound);
        }

        var now = time.GetUtcNow();
        var claims = new LicenseClaims
        {
            Iss = "symbolon:control-plane",
            Sub = license.CustomerRef ?? "customer",
            Aud = "symbolon:client",
            Jti = license.Id,
            Iat = license.IssuedAt.ToUnixTimeSeconds(),
            Exp = license.ExpiresAt?.ToUnixTimeSeconds() ?? now.AddDays(365).ToUnixTimeSeconds(),
            Symlic = new SymlicClaims
            {
                V = 1,
                Profile = "classical-v1",
                RequiredAlgs = ["ES256"],
                License = new LicenseMetadata
                {
                    Model = license.Policy?.LicenseModel ?? "floating",
                    State = license.State,
                    Customer = new CustomerMetadata { Ref = license.CustomerRef }
                },
                Limits = new LicenseLimits
                {
                    MaxSeats = license.MaxSeats,
                    SeatUnit = "machine"
                },
                Entitlements = [new EntitlementClaim { Code = "core" }]
            }
        };

        var signer = new LicenseDocumentSigner([signingKey]);
        string pem = signer.Sign(claims);

        return TypedResults.Content(pem, "application/x-pem-file");
    }

    private static async Task<IResult> GetRevocationsAsync(
        SymbolonDbContext db,
        CancellationToken ct)
    {
        var revs = await db.Revocations
            .OrderByDescending(r => r.RevokedAt)
            .Take(100)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return TypedResults.Ok(revs);
    }

    private static async Task<IResult> GetJwks(
        Security.KeyManager keyManager,
        CancellationToken ct)
    {
        var jwks = await keyManager.GetPublicJwksAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(jwks);
    }

    private static IResult ProcessOfflineRequest()
    {
        return TypedResults.Ok(new { status = "received", message = "Offline request processed" });
    }

    private static async Task<IResult> GetQueueStatusAsync(
        string ticket,
        Queuing.IQueueManager queueManager,
        CancellationToken ct)
    {
        var status = await queueManager.GetStatusAsync(ticket, ct).ConfigureAwait(false);
        if (status is null)
        {
            return TypedResults.NotFound();
        }

        if (status.Status == "expired" || status.Status == "cancelled")
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status410Gone,
                title: "Queue Ticket No Longer Valid",
                detail: $"Queue ticket '{ticket}' is {status.Status}.",
                type: ProblemTypes.InvalidRequest);
        }

        return TypedResults.Ok(status);
    }

    private static async Task<IResult> CancelQueueAsync(
        string ticket,
        Queuing.IQueueManager queueManager,
        CancellationToken ct)
    {
        bool cancelled = await queueManager.CancelAsync(ticket, ct).ConfigureAwait(false);
        return cancelled
            ? TypedResults.Ok(new { message = $"Queue ticket {ticket} cancelled.", ticket })
            : TypedResults.NotFound();
    }

    private static IResult GetTelemetry(
        HttpContext context,
        Observability.SymbolonMetrics metrics)
    {
        var telemetry = Observability.TelemetryCollector.Collect(context, metrics.ActiveSeats);
        return TypedResults.Ok(telemetry);
    }
}
