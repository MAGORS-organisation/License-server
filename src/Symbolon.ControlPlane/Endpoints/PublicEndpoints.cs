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
using Symbolon.Domain.PolicyRules;
using Symbolon.Domain.Security;
using Symbolon.Domain.Webhooks;
using Symbolon.Format;
using Symbolon.Protocol;
using Symbolon.Protocol.Tracing;
using Symbolon.Domain.Borrow;
using Symbolon.Domain.Experiments;

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

        group.MapPost("/leases/{id}/return-challenge", ReturnChallengeAsync)
            .WithName("ReturnChallenge")
            .WithSummary("Vygeneruje jednorazovú kryptografickú výzvu (nonce) pre predčasné vrátenie výpožičky.");

        group.MapPost("/leases/{id}/return", ReturnEarlyAsync)
            .WithName("ReturnEarly")
            .WithSummary("Predčasne vráti vypožičané sedadlo s proof-of-possession.");

        group.MapPost("/activations", ActivateAsync)
            .WithName("ActivateMachine")
            .WithSummary("Aktivuje uzol/stroj pre node-locked licenciu.");

        group.MapDelete("/activations/{id}", DeactivateAsync)
            .WithName("DeactivateMachine")
            .WithSummary("Deaktivuje stroj.");

        group.MapPost("/activations/verify-match", VerifyActivationMatch)
            .WithName("VerifyActivationMatch")
            .WithSummary("Overí zhodu dvoch fingerprintov podľa FPR-5 až FPR-9.");

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
        IExperimentStore experimentStore,
        CancellationToken ct)
    {
        if (dto is null || string.IsNullOrWhiteSpace(dto.LicenseKey) || dto.FingerprintComponents is null || dto.FingerprintComponents.Count == 0)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid Request",
                detail: "License key and fingerprint components are required.",
                type: ProblemTypes.InvalidRequest);
        }

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
            activity?.SetStatus(ActivityStatusCode.Error, "License Not Found");
            return TypedResults.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "License Not Found",
                type: ProblemTypes.LicenseNotFound);
        }

        var now = time.GetUtcNow();
        if (license.ExpiresAt.HasValue && license.ExpiresAt.Value < now)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "License Expired");
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
                activity?.SetStatus(ActivityStatusCode.Error, "User Identification Required");
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
                activity?.SetStatus(ActivityStatusCode.Error, "User Not Authorized");
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

        // ====================================================================
        // A/B Testing & Experimentation Evaluation (AB-1 .. AB-15, §13.4)
        // ====================================================================
        var checkoutSw = Stopwatch.StartNew();
        string machineId = dto.MachineId ?? fingerprint;
        string? osPlatform = context.Request.Headers["X-Symbolon-Platform"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(osPlatform) && dto.FingerprintComponents.TryGetValue("os", out var osComp))
        {
            osPlatform = osComp;
        }

        var clientContext = new ExperimentClientContext
        {
            SdkLanguage = context.Request.Headers["X-Symbolon-Sdk-Language"].FirstOrDefault(),
            SdkVersion = context.Request.Headers["X-Symbolon-Sdk-Version"].FirstOrDefault(),
            OsPlatform = osPlatform,
            ClientIp = clientIp
        };

        ExperimentEvaluationResult? expResult = null;
        Experiment? activeExp = null;
        var activeExperiments = await experimentStore.GetActiveAsync(license.TenantId, ct).ConfigureAwait(false);
        if (activeExperiments.Count > 0)
        {
            foreach (var exp in activeExperiments)
            {
                var eval = DeterministicBucketRouter.Route(exp, license.TenantId, dto.LicenseKey, machineId, clientContext);
                if (eval.IsInExperiment)
                {
                    expResult = eval;
                    activeExp = exp;
                    context.Response.Headers["X-Symbolon-Experiment"] = $"{eval.ExperimentId}={eval.VariantId}";
                    break;
                }
            }
        }

        async Task RecordExperimentAsync(bool isSuccess, bool isDenial, bool isError)
        {
            if (expResult is null || activeExp is null) return;
            double latency = checkoutSw.Elapsed.TotalMilliseconds;
            await experimentStore.RecordMetricAsync(
                expResult.ExperimentId,
                expResult.VariantId,
                isSuccess,
                isRenewal: false,
                isDenial,
                isError,
                latency,
                ct).ConfigureAwait(false);

            if (activeExp.CircuitBreaker.AutoRollback && isError)
            {
                var curMetrics = await experimentStore.GetMetricsAsync(activeExp.Id, ct).ConfigureAwait(false);
                var vm = curMetrics.FirstOrDefault(m => m.VariantId == expResult.VariantId);
                if (vm is not null &&
                    vm.TotalRequests >= activeExp.CircuitBreaker.MinSamplesThreshold &&
                    vm.ErrorRate > activeExp.CircuitBreaker.MaxErrorRate)
                {
                    activeExp.Status = ExperimentStatus.RolledBack;
                    activeExp.EndedAt = time.GetUtcNow();
                    await experimentStore.SaveAsync(activeExp, license.TenantId, ct).ConfigureAwait(false);
                }
            }
        }

        // ====================================================================
        // Options File & Policy Rules Evaluation (FLT-23, FLT-24, FLT-25, §7.5)
        // ====================================================================
        string? rulesYaml = expResult?.Overrides.PolicyRulesYaml ?? license.RulesYaml ?? license.Policy?.RulesYaml;
        string? reservationTarget = null;
        int? ruleDerivedPriority = null;

        if (!string.IsNullOrWhiteSpace(rulesYaml))
        {
            var ruleSet = PolicyRuleSerializer.Parse(rulesYaml);

            var activeSeats = await db.Seats
                .Where(s => s.LicenseId == license.Id && s.LeaseId != null && s.ExpiresAt > now)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            int GetActiveCount(string targetName)
            {
                if (targetName.StartsWith("group:", StringComparison.OrdinalIgnoreCase))
                {
                    string grp = targetName["group:".Length..];
                    var groupDef = ruleSet.Groups.FirstOrDefault(g => string.Equals(g.Name, grp, StringComparison.OrdinalIgnoreCase));
                    if (groupDef != null)
                    {
                        return activeSeats.Count(s =>
                            (s.UserId != null && groupDef.Members.Any(m => WildcardMatcher.Matches(s.UserId, m))) ||
                            (s.MachineId != null && groupDef.Hosts != null && groupDef.Hosts.Any(h => WildcardMatcher.Matches(s.MachineId, h))) ||
                            (s.ReservedFor != null && string.Equals(s.ReservedFor, grp, StringComparison.OrdinalIgnoreCase)));
                    }
                    return activeSeats.Count(s => (s.ReservedFor != null && string.Equals(s.ReservedFor, grp, StringComparison.OrdinalIgnoreCase)) ||
                                                 (s.UserId != null && string.Equals(s.UserId, grp, StringComparison.OrdinalIgnoreCase)));
                }
                else if (targetName.StartsWith("user:", StringComparison.OrdinalIgnoreCase))
                {
                    string u = targetName["user:".Length..];
                    return activeSeats.Count(s => s.UserId != null && WildcardMatcher.Matches(s.UserId, u));
                }
                return 0;
            }

            var evalContext = new RuleEvaluationContext(
                LicenseId: license.Id,
                UserId: dto.UserId,
                MachineId: dto.MachineId,
                HostName: dto.MachineId,
                ClientIp: clientIp,
                Features: dto.Features,
                CurrentlyHeldByClient: 0,
                GetActiveCountForTarget: GetActiveCount);

            var evalResult = PolicyRuleEngine.Evaluate(ruleSet, evalContext);

            if (!evalResult.Allowed)
            {
                await RecordExperimentAsync(isSuccess: false, isDenial: true, isError: false).ConfigureAwait(false);
                metrics.RecordCheckoutDenied(license.Id);
                await alertService.RecordDenialSpikeAsync(license.Id, license.TenantId, evalResult.DenyType ?? "rule-denied", ct).ConfigureAwait(false);

                await webhooks.PublishEventAsync(WebhookEventTypes.LeaseDenied, new
                {
                    licenseId = license.Id,
                    tenantId = license.TenantId,
                    customerRef = license.CustomerRef,
                    fingerprint,
                    reason = evalResult.DenyReason ?? "Access denied by policy rule",
                    denyType = evalResult.DenyType ?? "rule-denied",
                    requestedQuantity = dto.Quantity ?? 1,
                    userId = dto.UserId,
                    machineId = dto.MachineId
                }, license.TenantId, ct).ConfigureAwait(false);

                activity?.SetStatus(ActivityStatusCode.Error, evalResult.DenyReason ?? "Access Denied by Policy Rule");
                if (evalResult.DenyType == "group-quota-exceeded")
                {
                    return TypedResults.Problem(
                        statusCode: StatusCodes.Status409Conflict,
                        title: "Group Quota Exceeded",
                        detail: evalResult.DenyReason,
                        type: ProblemTypes.GroupQuotaExceeded);
                }

                return TypedResults.Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Access Denied by Policy Rule",
                    detail: evalResult.DenyReason,
                    type: ProblemTypes.RuleDenied);
            }

            reservationTarget = evalResult.MatchedReservationTarget;
            ruleDerivedPriority = evalResult.ResolvedPriority;
        }

        int quantity = dto.Quantity ?? 1;
        int leaseTtlSeconds = expResult?.Overrides.LeaseTtlSeconds ?? license.Policy?.LeaseTtlSeconds ?? 600;
        var ttl = TimeSpan.FromSeconds(leaseTtlSeconds);

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
            ttl,
            reservationTarget);

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
                    s.UserId = dto.UserId;
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
            await RecordExperimentAsync(isSuccess: true, isDenial: false, isError: false).ConfigureAwait(false);
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
                priority: dto.Priority ?? ruleDerivedPriority ?? 0,
                ct).ConfigureAwait(false);

            var qStatus = await queueManager.GetStatusAsync(ticket.Ticket, ct).ConfigureAwait(false);
            int retryAfter = qStatus?.RetryAfterSeconds ?? 2;
            context.Response.Headers.RetryAfter = retryAfter.ToString(CultureInfo.InvariantCulture);

            await RecordExperimentAsync(isSuccess: true, isDenial: false, isError: false).ConfigureAwait(false);
            return TypedResults.Accepted($"/v1/queue/{ticket.Ticket}", new QueuedResponseDto
            {
                Ticket = ticket.Ticket,
                Position = qStatus?.Position ?? 1,
                Priority = ticket.Priority,
                EstimatedWait = qStatus?.EstimatedWait ?? "PT2M",
                RetryAfterSeconds = retryAfter
            });
        }

        await RecordExperimentAsync(isSuccess: false, isDenial: true, isError: false).ConfigureAwait(false);
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

        activity?.SetStatus(ActivityStatusCode.Error, result.Reason ?? "Unable to acquire seat.");
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
        HttpContext context,
        LeaseEngine engine,
        Observability.SymbolonMetrics metrics,
        IExperimentStore experimentStore,
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

        string? expHeader = context.Request.Headers["X-Symbolon-Experiment"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(expHeader))
        {
            var parts = expHeader.Split('=', 2);
            if (parts.Length == 2)
            {
                await experimentStore.RecordMetricAsync(
                    parts[0],
                    parts[1],
                    isSuccess: result.IsSuccess,
                    isRenewal: true,
                    isDenial: !result.IsSuccess,
                    isError: false,
                    latencyMs: 0.0,
                    ct).ConfigureAwait(false);
            }
        }

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

        if (seat is not null && seat.BorrowedUntil.HasValue && seat.BorrowedUntil.Value > DateTimeOffset.UtcNow)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Proof of Possession Required");
            return TypedResults.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Proof of Possession Required",
                detail: "Borrowed seats cannot be released early via standard DELETE (FLT-22). Use POST /v1/leases/{id}/return with a valid proof-of-possession challenge signature.",
                type: ProblemTypes.ProofOfPossessionRequired);
        }

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
        ISignatureProvider signatureProvider,
        IAuditLedger audit,
        TimeProvider time,
        CancellationToken ct)
    {
        using var activity = SymbolonTracing.ActivitySource.StartActivity(SymbolonTracing.OpBorrow);
        activity?.SetTag(SymbolonTracing.TagLeaseId, id);
        activity?.SetTag(SymbolonTracing.TagBorrowDays, dto.Days);

        var seat = await db.Seats
            .Include(s => s.License)
                .ThenInclude(l => l!.Policy)
            .FirstOrDefaultAsync(s => s.LeaseId == id, ct)
            .ConfigureAwait(false);

        if (seat is null)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Lease Not Found");
            return TypedResults.Problem(statusCode: 404, title: "Lease Not Found", type: ProblemTypes.LeaseUnknown);
        }

        var policy = seat.License?.Policy;
        var now = time.GetUtcNow();

        // Count current active borrows for this license
        int activeBorrows = await db.Seats.CountAsync(s => s.LicenseId == seat.LicenseId && s.BorrowedUntil != null && s.BorrowedUntil > now, ct).ConfigureAwait(false);

        // FLT-18: Validate borrow policy constraints
        var eval = BorrowPolicyEvaluator.Evaluate(
            borrowEnabled: policy?.BorrowEnabled ?? false,
            maxDurationDays: policy?.BorrowMaxDurationDays ?? 7,
            maxConcurrent: policy?.BorrowMaxConcurrent ?? 5,
            requestedDays: dto.Days,
            currentActiveBorrowsCount: activeBorrows);

        if (!eval.IsAllowed)
        {
            int status = eval.ProblemType switch
            {
                ProblemTypes.BorrowDisabled => StatusCodes.Status403Forbidden,
                ProblemTypes.BorrowLimitExceeded => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status400BadRequest
            };
            activity?.SetStatus(ActivityStatusCode.Error, eval.FailureReason);
            return TypedResults.Problem(statusCode: status, title: "Borrow Denied", detail: eval.FailureReason, type: eval.ProblemType);
        }

        // Ephemeral possession key pair
        string possessionPublicJwk;
        string? possessionPrivateJwk = null;

        if (!string.IsNullOrWhiteSpace(dto.PossessionPublicKeyJwk))
        {
            possessionPublicJwk = dto.PossessionPublicKeyJwk;
        }
        else
        {
            var keyPair = ProofOfPossessionEngine.GenerateKeyPair();
            possessionPublicJwk = keyPair.PublicKeyJwk;
            possessionPrivateJwk = keyPair.PrivateKeyJwk;
        }

        var borrowedUntil = now.AddDays(dto.Days);
        seat.BorrowedUntil = borrowedUntil;
        seat.ExpiresAt = borrowedUntil;
        seat.PossessionKey = possessionPublicJwk;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // Issue FLT-19 .symlease artifact
        var entitlements = new List<string> { "core" };
        if (!string.IsNullOrWhiteSpace(policy?.EntitlementsJson))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<List<string>>(policy.EntitlementsJson);
                if (parsed is { Count: > 0 }) entitlements = parsed;
            }
            catch (JsonException) { }
        }

        string holderFpHash = seat.HolderFp is not null
            ? $"sha256:{Convert.ToHexString(SHA256.HashData(seat.HolderFp)).ToLowerInvariant()}"
            : "sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

        var symleaseClaims = new SymleaseClaims(
            Iss: "symbolon:control-plane",
            Sub: seat.LicenseId,
            Jti: seat.LeaseId!,
            Iat: now.ToUnixTimeSeconds(),
            Nbf: now.AddMinutes(-5).ToUnixTimeSeconds(),
            Exp: borrowedUntil.ToUnixTimeSeconds(),
            Seat: seat.SeatNo,
            Fp: holderFpHash,
            Ent: entitlements,
            Borrow: new SymleaseBorrowPayload(
                Days: dto.Days,
                BorrowedAt: now.ToUnixTimeSeconds(),
                BorrowedUntil: borrowedUntil.ToUnixTimeSeconds(),
                PossessionKeyJwk: possessionPublicJwk
            )
        );

        var signer = new SymleaseSigner([signatureProvider]);
        string symleasePem = signer.Sign(symleaseClaims);

        var alloc = new SeatAllocation
        {
            SeatId = seat.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            SeatNo = seat.SeatNo,
            LicenseId = seat.LicenseId,
            LeaseId = seat.LeaseId,
            HolderFingerprint = seat.HolderFp is not null ? Encoding.UTF8.GetString(seat.HolderFp) : string.Empty,
            ExpiresAt = borrowedUntil,
            IsOverage = seat.IsOverage,
            LeaseSeq = seat.LeaseSeq,
            BorrowedUntil = borrowedUntil,
            PossessionKey = possessionPublicJwk
        };

        string token = tokenIssuer.Issue(alloc);
        await audit.AppendAsync(new AuditEvent("seat.borrowed", seat.LicenseId, seat.LeaseId, null, now, $"days={dto.Days}"), ct).ConfigureAwait(false);

        activity?.SetStatus(ActivityStatusCode.Ok);
        return TypedResults.Ok(new BorrowResponseDto(seat.LeaseId!, borrowedUntil, token, symleasePem, possessionPrivateJwk));
    }

    private static async Task<IResult> ReturnChallengeAsync(
        string id,
        SymbolonDbContext db,
        Symbolon.Domain.Borrow.IReturnChallengeStore challengeStore,
        TimeProvider time,
        CancellationToken ct)
    {
        var seat = await db.Seats
            .FirstOrDefaultAsync(s => s.LeaseId == id, ct)
            .ConfigureAwait(false);

        if (seat is null || !seat.BorrowedUntil.HasValue || seat.BorrowedUntil.Value <= time.GetUtcNow())
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Active Borrow Not Found",
                detail: $"No active offline roaming borrow was found for lease {id}.",
                type: ProblemTypes.NotFound);
        }

        var (nonce, expiresAt) = await challengeStore.CreateChallengeAsync(id, TimeSpan.FromMinutes(5), ct).ConfigureAwait(false);
        return TypedResults.Ok(new ReturnChallengeResponseDto
        {
            LeaseId = id,
            Nonce = nonce,
            ExpiresAt = expiresAt
        });
    }

    private static async Task<IResult> ReturnEarlyAsync(
        string id,
        EarlyReturnRequestDto dto,
        SymbolonDbContext db,
        SymbolonKeyRing keyRing,
        Symbolon.Domain.Borrow.IReturnChallengeStore challengeStore,
        FeatureEntitlementEngine featureEngine,
        Queuing.IQueueManager queueManager,
        Observability.SymbolonMetrics metrics,
        IAuditLedger audit,
        TimeProvider time,
        CancellationToken ct)
    {
        using var activity = SymbolonTracing.ActivitySource.StartActivity(SymbolonTracing.OpReturnBorrowed);
        activity?.SetTag(SymbolonTracing.TagLeaseId, id);

        var seat = await db.Seats
            .FirstOrDefaultAsync(s => s.LeaseId == id, ct)
            .ConfigureAwait(false);

        var now = time.GetUtcNow();
        if (seat is null || !seat.BorrowedUntil.HasValue || seat.BorrowedUntil.Value <= now)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Active Borrow Not Found");
            return TypedResults.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Active Borrow Not Found",
                detail: $"No active offline roaming borrow was found for lease {id}.",
                type: ProblemTypes.NotFound);
        }

        // 1. Consume challenge nonce (single-use, anti-replay)
        bool validNonce = await challengeStore.TryConsumeChallengeAsync(id, dto.Nonce, now, ct).ConfigureAwait(false);
        if (!validNonce)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Challenge Expired or Invalid");
            return TypedResults.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Challenge Expired or Invalid",
                detail: "The provided challenge nonce is invalid, expired, or already consumed.",
                type: ProblemTypes.ChallengeExpired);
        }

        // 2. Verify proof-of-possession signature
        if (string.IsNullOrWhiteSpace(seat.PossessionKey))
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Missing Possession Key");
            return TypedResults.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Missing Possession Key",
                detail: "Seat has no possession key associated with the active borrow.",
                type: ProblemTypes.InvalidRequest);
        }

        bool validProof = ProofOfPossessionEngine.VerifyProof(dto.Nonce, dto.Signature, seat.PossessionKey);
        if (!validProof)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Invalid Proof of Possession");
            return TypedResults.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Invalid Proof of Possession",
                detail: "Cryptographic signature over the challenge nonce is invalid.",
                type: ProblemTypes.InvalidProofOfPossession);
        }

        // 3. Verify .symlease artifact
        var verifier = new SymleaseVerifier(keyRing, time, new SymleaseVerifierOptions
        {
            ExpectedLicenseId = seat.LicenseId
        });
        var symleaseResult = verifier.Verify(dto.Symlease);
        if (!symleaseResult.IsValid || symleaseResult.Claims?.Jti != id)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Invalid Symlease Artifact");
            return TypedResults.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Invalid Symlease Artifact",
                detail: symleaseResult.FailureReason ?? "The provided .symlease artifact does not match the active lease.",
                type: ProblemTypes.InvalidProofOfPossession);
        }

        // 4. Release borrowed seat early
        string licenseId = seat.LicenseId;
        seat.LeaseId = null;
        seat.HolderFp = null;
        seat.MachineId = null;
        seat.AcquiredAt = null;
        seat.ExpiresAt = null;
        seat.BorrowedUntil = null;
        seat.PossessionKey = null;
        seat.UserId = null;
        seat.LeaseSeq = 0;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await featureEngine.ReleaseAllFeaturesForLeaseAsync(id, ct).ConfigureAwait(false);
        metrics.RecordSeatReleased(1);

        await audit.AppendAsync(new AuditEvent("seat.borrow_returned_early", licenseId, id, null, now, null), ct).ConfigureAwait(false);
        await queueManager.TryPromoteNextAsync(licenseId, ct).ConfigureAwait(false);

        activity?.SetStatus(ActivityStatusCode.Ok);
        return TypedResults.Ok(new EarlyReturnResponseDto
        {
            Success = true,
            ReturnedAt = now,
            LeaseId = id
        });
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
        var now = time.GetUtcNow();
        string strategy = license.Policy?.MachineMatching ?? MatchingStrategies.MatchMost;

        // 1. Check if candidate machine matches by ID, exact fingerprint, or fuzzy matching (FPR-5 to FPR-9)
        MachineEntity? matchedMachine = null;

        // Check exact fingerprint match among active machines
        matchedMachine = license.Machines.FirstOrDefault(m => m.State == "active" && string.Equals(m.Fingerprint, fp, StringComparison.OrdinalIgnoreCase));

        // If MachineId is provided, check if machine exists on this license
        if (matchedMachine is null && !string.IsNullOrWhiteSpace(dto.MachineId))
        {
            var targetMachine = license.Machines.FirstOrDefault(m => m.State == "active" && (string.Equals(m.Id, dto.MachineId, StringComparison.OrdinalIgnoreCase) || string.Equals(m.Fingerprint, dto.MachineId, StringComparison.OrdinalIgnoreCase)));
            if (targetMachine is not null)
            {
                // Target machine identified! Evaluate fuzzy matching (FPR-17)
                if (!string.IsNullOrWhiteSpace(targetMachine.ComponentsJson))
                {
                    try
                    {
                        var storedComp = JsonSerializer.Deserialize<Dictionary<string, string>>(targetMachine.ComponentsJson);
                        var eval = FingerprintMatchingEngine.EvaluateMatch(storedComp, dto.FingerprintComponents, strategy);
                        if (eval.IsMatch)
                        {
                            matchedMachine = targetMachine;
                        }
                        else
                        {
                            // FPR-17: Fingerprint mismatch MUST lead to HTTP 403 Forbidden with fingerprint-mismatch (FLT-29)
                            return TypedResults.Problem(
                                statusCode: StatusCodes.Status403Forbidden,
                                title: "Fingerprint Mismatch",
                                detail: $"Machine fingerprint failed verification against registered machine under strategy '{strategy}': {eval.FailureReason}",
                                type: ProblemTypes.FingerprintMismatch);
                        }
                    }
                    catch (JsonException)
                    {
                    }
                }
            }
        }

        // Check other active machines on this license for fuzzy matching (hardware upgrade / drift tolerance)
        if (matchedMachine is null)
        {
            foreach (var candidate in license.Machines.Where(m => m.State == "active" && !string.IsNullOrWhiteSpace(m.ComponentsJson)))
            {
                try
                {
                    var storedComp = JsonSerializer.Deserialize<Dictionary<string, string>>(candidate.ComponentsJson!);
                    var eval = FingerprintMatchingEngine.EvaluateMatch(storedComp, dto.FingerprintComponents, strategy);
                    if (eval.IsMatch)
                    {
                        matchedMachine = candidate;
                        break;
                    }
                }
                catch (JsonException)
                {
                }
            }
        }

        // If matched machine found: refresh heartbeat and update components
        if (matchedMachine is not null)
        {
            matchedMachine.LastHeartbeat = now;
            matchedMachine.Fingerprint = fp;
            matchedMachine.ComponentsJson = JsonSerializer.Serialize(dto.FingerprintComponents);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            return TypedResults.Ok(new ActivationResponseDto
            {
                ActivationId = matchedMachine.Id,
                LicenseId = license.Id,
                Fingerprint = fp,
                State = matchedMachine.State,
                ActivatedAt = matchedMachine.FirstSeen
            });
        }

        // 2. MachineUniqueness policy enforcement (FPR-16)
        string uniqueness = license.Policy?.MachineUniqueness ?? "per-license";
        if (string.Equals(uniqueness, "unique", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(uniqueness, "global", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(uniqueness, "per-tenant", StringComparison.OrdinalIgnoreCase))
        {
            bool usedElsewhere = await db.Machines
                .Include(m => m.License)
                .AnyAsync(m => m.LicenseId != license.Id && m.License != null && m.License.TenantId == license.TenantId && m.State == "active" && m.Fingerprint == fp, ct)
                .ConfigureAwait(false);

            if (usedElsewhere)
            {
                return TypedResults.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Machine Already Registered",
                    detail: "This machine fingerprint is already active on another license under uniqueness policy (FPR-16).",
                    type: ProblemTypes.PoolExhausted);
            }
        }

        // 3. Quota check: ensure active machines do not exceed license limit
        int activeMachines = license.Machines.Count(m => m.State == "active");
        if (activeMachines >= license.MaxSeats)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Max Machine Activations Reached",
                detail: $"License has reached its maximum machine activation limit ({activeMachines}/{license.MaxSeats}).",
                type: ProblemTypes.PoolExhausted);
        }

        // 4. Create new activation
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

        await audit.AppendAsync(new AuditEvent("activate", license.Id, null, fp, now, $"Node-lock machine activated: {dto.MachineId ?? machineId} (strategy: {strategy})"), ct).ConfigureAwait(false);

        await webhooks.PublishEventAsync("machine.activated", new
        {
            activationId = machine.Id,
            licenseId = license.Id,
            tenantId = license.TenantId,
            fingerprint = fp,
            machineId = dto.MachineId ?? machineId
        }, license.TenantId, ct).ConfigureAwait(false);

        return TypedResults.Ok(new ActivationResponseDto
        {
            ActivationId = machine.Id,
            LicenseId = license.Id,
            Fingerprint = fp,
            State = "active",
            ActivatedAt = now
        });
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

        return TypedResults.Ok(new DeactivateResponseDto
        {
            Success = true,
            ActivationId = machine.Id
        });
    }

    private static IResult VerifyActivationMatch(VerifyFingerprintMatchRequestDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        var result = FingerprintMatchingEngine.EvaluateMatch(
            dto.StoredComponents,
            dto.IncomingComponents,
            dto.Strategy);

        return TypedResults.Ok(new VerifyFingerprintMatchResponseDto
        {
            IsMatch = result.IsMatch,
            StrategyUsed = result.StrategyUsed,
            CommonComponentsCount = result.CommonComponentsCount,
            MatchedComponentsCount = result.MatchedComponentsCount,
            MatchedKeys = result.MatchedKeys,
            MismatchedKeys = result.MismatchedKeys,
            FailureReason = result.FailureReason,
            MatchRatio = result.MatchRatio
        });
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
