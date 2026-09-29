using System.Buffers.Text;
using System.Diagnostics;
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
using Symbolon.Domain.Entitlements;
using Symbolon.Domain.Security;
using Symbolon.Domain.Webhooks;
using Symbolon.Format;
using Symbolon.Protocol;
using Symbolon.Protocol.Tracing;

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

        group.MapPost("/leases/{id}/features/acquire", AcquireFeatureAsync)
            .WithName("AcquireLeaseFeature")
            .WithSummary("Vyžiada dynamickú licenciu/modul pre aktívny lease.");

        group.MapPost("/leases/{id}/features/release", ReleaseFeatureAsync)
            .WithName("ReleaseLeaseFeature")
            .WithSummary("Uvoľní dynamickú licenciu/modul priradenú k danému lease.");

        group.MapGet("/leases/{id}/features", GetLeaseFeaturesAsync)
            .WithName("GetLeaseFeatures")
            .WithSummary("Vráti zoznam aktívnych modulov/funkcií držaných daným lease.");

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
            .WithSummary("Vráti zoznam aktívnych revokácií (.symrl a JSON metadata).");

        group.MapGet("/revocations/latest.symrl", GetRevocationsSymrlAsync)
            .WithName("GetRevocationsSymrl")
            .WithSummary("Stiahne kryptograficky podpísaný .symrl revokačný zoznam.");

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
        FeatureEntitlementEngine featureEngine,
        Queuing.IQueueManager queueManager,
        Webhooks.IWebhookDispatcher webhooks,
        TimeProvider time,
        Observability.SymbolonMetrics metrics,
        Alerting.IAlertService alertService,
        IFraudDetectionService fraudDetection,
        CancellationToken ct)
    {
        using var activity = SymbolonTracing.ActivitySource.StartActivity(SymbolonTracing.OpCheckout);
        activity?.SetTag(SymbolonTracing.TagLicenseId, dto.LicenseKey);
        activity?.SetTag(SymbolonTracing.TagSeatCount, dto.Quantity);

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

            await webhooks.PublishEventAsync(WebhookEventTypes.FraudDetected, new
            {
                licenseId = license.Id,
                tenantId = license.TenantId,
                riskType = fraudAssessment.RiskType ?? "fraud_detected",
                description = fraudAssessment.Description,
                clientIp,
                fingerprint
            }, license.TenantId, ct).ConfigureAwait(false);
        }

        int quantity = dto.Quantity ?? 1;
        var ttl = TimeSpan.FromSeconds(license.Policy?.LeaseTtlSeconds ?? 600);

        var expandedFeatures = dto.Features is { Count: > 0 }
            ? await featureEngine.ExpandFeaturesAsync(license.TenantId, dto.Features, ct).ConfigureAwait(false)
            : (dto.Features ?? ["core"]);

        var cmd = new CheckoutCommand(
            license.Id,
            fingerprint,
            dto.MachineId,
            quantity,
            expandedFeatures,
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
            if (!string.IsNullOrWhiteSpace(dto.UserId) && !string.IsNullOrWhiteSpace(alloc.LeaseId))
            {
                var allocatedSeats = await db.Seats
                    .Where(s => s.LeaseId == alloc.LeaseId)
                    .ToListAsync(ct)
                    .ConfigureAwait(false);
                foreach (var s in allocatedSeats)
                {
                    s.ReservedFor = dto.UserId;
                }
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }

            if (dto.Features is { Count: > 0 } && !string.IsNullOrWhiteSpace(alloc.LeaseId))
            {
                foreach (var feat in expandedFeatures)
                {
                    var req = new FeatureAcquisitionRequest(
                        LeaseId: alloc.LeaseId,
                        LicenseId: license.Id,
                        TenantId: license.TenantId,
                        FeatureCode: feat,
                        Version: null,
                        Ttl: ttl);
                    await featureEngine.AcquireFeatureAsync(req, ct).ConfigureAwait(false);
                }
            }

            activity?.SetTag(SymbolonTracing.TagLeaseId, alloc.LeaseId ?? string.Empty);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return TypedResults.Ok(new CheckoutResponseDto
            {
                LeaseId = alloc.LeaseId ?? string.Empty,
                Token = result.Tokens[0],
                ExpiresAt = alloc.ExpiresAt,
                Seat = alloc.SeatNo,
                Entitlements = expandedFeatures
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
                priority: dto.Priority ?? 0,
                ct).ConfigureAwait(false);

            var qStatus = await queueManager.GetStatusAsync(ticket.Ticket, ct).ConfigureAwait(false);
            int retryAfter = qStatus?.RetryAfterSeconds ?? 2;
            context.Response.Headers.RetryAfter = retryAfter.ToString(CultureInfo.InvariantCulture);

            return TypedResults.Accepted($"/v1/queue/{ticket.Ticket}", new QueuedResponseDto
            {
                Ticket = ticket.Ticket,
                Position = qStatus?.Position ?? 1,
                Priority = ticket.Priority,
                EstimatedWait = qStatus?.EstimatedWait ?? "PT2M",
                RetryAfterSeconds = retryAfter
            });
        }

        metrics.RecordCheckoutDenied(license.Id);
        await alertService.RecordDenialSpikeAsync(license.Id, license.TenantId, result.Reason ?? "seat-pool-exhausted", ct).ConfigureAwait(false);

        await webhooks.PublishEventAsync(WebhookEventTypes.LeaseDenied, new
        {
            licenseId = license.Id,
            tenantId = license.TenantId,
            customerRef = license.CustomerRef,
            fingerprint,
            reason = result.Reason ?? "seat-pool-exhausted",
            requestedQuantity = quantity,
            maxSeats = license.MaxSeats
        }, license.TenantId, ct).ConfigureAwait(false);

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
        using var activity = SymbolonTracing.ActivitySource.StartActivity(SymbolonTracing.OpRenew);
        activity?.SetTag(SymbolonTracing.TagLeaseId, id);

        string? fp = dto.ResolveFingerprintHash();
        if (string.IsNullOrWhiteSpace(fp))
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Missing Fingerprint");
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
            activity?.SetStatus(ActivityStatusCode.Ok);
            return TypedResults.Ok(new RenewResponseDto
            {
                Token = result.Token,
                ExpiresAt = result.Allocation.ExpiresAt,
                LeaseSeq = result.Allocation.LeaseSeq
            });
        }

        activity?.SetStatus(ActivityStatusCode.Error, result.Reason);
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
        FeatureEntitlementEngine featureEngine,
        Queuing.IQueueManager queueManager,
        Observability.SymbolonMetrics metrics,
        CancellationToken ct)
    {
        using var activity = SymbolonTracing.ActivitySource.StartActivity(SymbolonTracing.OpRelease);
        activity?.SetTag(SymbolonTracing.TagLeaseId, id);

        var seat = await db.Seats.FirstOrDefaultAsync(s => s.LeaseId == id, ct).ConfigureAwait(false);
        string? licenseId = seat?.LicenseId;

        bool released = await engine.ReleaseAsync(id, ct).ConfigureAwait(false);
        if (released)
        {
            await featureEngine.ReleaseAllFeaturesForLeaseAsync(id, ct).ConfigureAwait(false);
            metrics.RecordSeatReleased(1);
            if (!string.IsNullOrWhiteSpace(licenseId))
            {
                await queueManager.TryPromoteNextAsync(licenseId, ct).ConfigureAwait(false);
            }
            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        else
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Release failed");
        }
        return TypedResults.Ok(new ReleaseResponseDto { Success = released });
    }

    private static async Task<IResult> AcquireFeatureAsync(
        string id,
        AcquireFeatureRequestDto dto,
        SymbolonDbContext db,
        FeatureEntitlementEngine featureEngine,
        TimeProvider time,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (string.IsNullOrWhiteSpace(dto.FeatureCode))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid Feature Code",
                detail: "Feature code cannot be empty.",
                type: ProblemTypes.InvalidRequest);
        }

        var now = time.GetUtcNow();
        var seat = await db.Seats
            .Include(s => s.License)
            .FirstOrDefaultAsync(s => s.LeaseId == id, ct)
            .ConfigureAwait(false);

        if (seat is null || !seat.ExpiresAt.HasValue || seat.ExpiresAt.Value <= now)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Lease Not Found or Expired",
                detail: $"No active lease found with ID '{id}'.",
                type: ProblemTypes.LeaseUnknown);
        }

        var leaseRemaining = seat.ExpiresAt.Value - now;
        var ttl = dto.TtlSeconds.HasValue && dto.TtlSeconds.Value > 0
            ? TimeSpan.FromSeconds(Math.Min(dto.TtlSeconds.Value, leaseRemaining.TotalSeconds))
            : leaseRemaining;

        string tenantId = seat.License?.TenantId ?? "default";

        var req = new FeatureAcquisitionRequest(
            LeaseId: id,
            LicenseId: seat.LicenseId,
            TenantId: tenantId,
            FeatureCode: dto.FeatureCode,
            Version: dto.Version,
            Ttl: ttl);

        var result = await featureEngine.AcquireFeatureAsync(req, ct).ConfigureAwait(false);

        var response = new FeatureAcquisitionResponseDto
        {
            Success = result.Success,
            FeatureCode = result.FeatureCode,
            Version = result.Version,
            Reason = result.Reason,
            InUse = result.InUse,
            MaxSeats = result.MaxSeats
        };

        if (result.Success)
        {
            return TypedResults.Ok(response);
        }

        return TypedResults.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Feature Acquisition Denied",
            detail: result.Reason ?? "Unable to acquire feature seat.",
            type: ProblemTypes.FeatureCapacityExceeded);
    }

    private static async Task<IResult> ReleaseFeatureAsync(
        string id,
        ReleaseFeatureRequestDto dto,
        FeatureEntitlementEngine featureEngine,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (string.IsNullOrWhiteSpace(dto.FeatureCode))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid Feature Code",
                detail: "Feature code cannot be empty.",
                type: ProblemTypes.InvalidRequest);
        }

        var result = await featureEngine.ReleaseFeatureAsync(id, dto.FeatureCode, ct).ConfigureAwait(false);
        var response = new ReleaseFeatureResponseDto
        {
            Success = result.Success,
            FeatureCode = result.FeatureCode,
            Reason = result.Reason
        };

        if (result.Success)
        {
            return TypedResults.Ok(response);
        }

        return TypedResults.NotFound(response);
    }

    private static async Task<IResult> GetLeaseFeaturesAsync(
        string id,
        IFeatureEntitlementStore store,
        CancellationToken ct)
    {
        var activeFeatures = await store.GetActiveFeaturesForLeaseAsync(id, ct).ConfigureAwait(false);
        var dtoList = activeFeatures.Select(f => new ActiveFeatureInfoDto
        {
            FeatureCode = f.FeatureCode,
            Version = f.AcquiredVersion,
            AcquiredAt = f.AcquiredAt,
            ExpiresAt = f.ExpiresAt
        }).ToList();

        return TypedResults.Ok(dtoList);
    }

    private static async Task<IResult> BorrowAsync(
        string id,
        BorrowRequestDto dto,
        SymbolonDbContext db,
        ILeaseTokenIssuer tokenIssuer,
        TimeProvider time,
        CancellationToken ct)
    {
        using var activity = SymbolonTracing.ActivitySource.StartActivity(SymbolonTracing.OpBorrow);
        activity?.SetTag(SymbolonTracing.TagLeaseId, id);
        activity?.SetTag(SymbolonTracing.TagBorrowDays, dto.Days);

        var seat = await db.Seats
            .Include(s => s.License)
            .FirstOrDefaultAsync(s => s.LeaseId == id, ct)
            .ConfigureAwait(false);

        if (seat is null)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Lease Not Found");
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
        activity?.SetStatus(ActivityStatusCode.Ok);
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
        long? since,
        SymbolonDbContext db,
        ISignatureProvider signingKey,
        TimeProvider time,
        CancellationToken ct)
    {
        long currentMaxSeq = await db.Revocations.MaxAsync(r => (long?)r.Sequence, ct).ConfigureAwait(false) ?? 0;
        bool full = true;
        List<RevocationEntity> revs;

        if (since.HasValue && since.Value >= 0 && since.Value <= currentMaxSeq)
        {
            full = false;
            revs = await db.Revocations
                .Where(r => r.Sequence > since.Value)
                .OrderBy(r => r.Sequence)
                .ToListAsync(ct)
                .ConfigureAwait(false);
        }
        else
        {
            revs = await db.Revocations
                .OrderBy(r => r.Sequence)
                .ToListAsync(ct)
                .ConfigureAwait(false);
        }

        var now = time.GetUtcNow().ToUnixTimeSeconds();
        var items = revs.Select(r => new RevocationItem(
            T: r.SubjectType == "key" ? RevocationItem.TypeKid : r.SubjectType,
            Id: r.SubjectId,
            At: r.RevokedAt.ToUnixTimeSeconds(),
            Reason: string.IsNullOrWhiteSpace(r.Reason) ? null : r.Reason
        )).ToList();

        var claims = new RevocationListClaims(
            Iss: "symbolon:control-plane",
            Iat: now,
            Exp: now + 86400,
            Symrl: new RevocationPayload(
                V: 1,
                Seq: currentMaxSeq,
                Full: full,
                Since: full ? null : since,
                Revoked: items
            )
        );

        var signer = new RevocationListSigner([signingKey]);
        string pem = signer.Sign(claims);

        return TypedResults.Ok(new
        {
            pem,
            claims
        });
    }

    private static async Task<IResult> GetRevocationsSymrlAsync(
        long? since,
        SymbolonDbContext db,
        ISignatureProvider signingKey,
        TimeProvider time,
        CancellationToken ct)
    {
        long currentMaxSeq = await db.Revocations.MaxAsync(r => (long?)r.Sequence, ct).ConfigureAwait(false) ?? 0;
        bool full = true;
        List<RevocationEntity> revs;

        if (since.HasValue && since.Value >= 0 && since.Value <= currentMaxSeq)
        {
            full = false;
            revs = await db.Revocations
                .Where(r => r.Sequence > since.Value)
                .OrderBy(r => r.Sequence)
                .ToListAsync(ct)
                .ConfigureAwait(false);
        }
        else
        {
            revs = await db.Revocations
                .OrderBy(r => r.Sequence)
                .ToListAsync(ct)
                .ConfigureAwait(false);
        }

        var now = time.GetUtcNow().ToUnixTimeSeconds();
        var items = revs.Select(r => new RevocationItem(
            T: r.SubjectType == "key" ? RevocationItem.TypeKid : r.SubjectType,
            Id: r.SubjectId,
            At: r.RevokedAt.ToUnixTimeSeconds(),
            Reason: string.IsNullOrWhiteSpace(r.Reason) ? null : r.Reason
        )).ToList();

        var claims = new RevocationListClaims(
            Iss: "symbolon:control-plane",
            Iat: now,
            Exp: now + 86400,
            Symrl: new RevocationPayload(
                V: 1,
                Seq: currentMaxSeq,
                Full: full,
                Since: full ? null : since,
                Revoked: items
            )
        );

        var signer = new RevocationListSigner([signingKey]);
        string pem = signer.Sign(claims);

        return TypedResults.Content(pem, "application/x-pem-file");
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
        HttpContext context,
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

        if (status.Status == "waiting")
        {
            context.Response.Headers.RetryAfter = (status.RetryAfterSeconds ?? 2).ToString(CultureInfo.InvariantCulture);
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
