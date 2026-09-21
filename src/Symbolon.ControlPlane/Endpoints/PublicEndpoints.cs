using System.Buffers.Text;
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

        return group;
    }

    private static async Task<IResult> CheckoutAsync(
        CheckoutRequestDto dto,
        HttpContext context,
        SymbolonDbContext db,
        LeaseEngine engine,
        TimeProvider time,
        Observability.SymbolonMetrics metrics,
        CancellationToken ct)
    {
        string? idempotencyKey = context.Request.Headers["Idempotency-Key"].FirstOrDefault();
        byte[] lookup = SHA256.HashData(Encoding.UTF8.GetBytes(dto.LicenseKey.Trim()))[..4];

        var license = await db.Licenses
            .Include(l => l.Policy)
            .FirstOrDefaultAsync(l => l.KeyLookup == lookup, ct)
            .ConfigureAwait(false);

        if (license is null || license.State != "active")
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

        string fingerprint = dto.ToFingerprintHash();
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

        if (result.QueueTicket is not null)
        {
            return TypedResults.Accepted($"/v1/queue/{result.QueueTicket}", new QueuedResponseDto
            {
                Ticket = result.QueueTicket,
                Position = 1,
                EstimatedWait = result.EstimatedWait?.ToString()
            });
        }

        metrics.RecordCheckoutDenied(license.Id);
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
        LeaseEngine engine,
        Observability.SymbolonMetrics metrics,
        CancellationToken ct)
    {
        bool released = await engine.ReleaseAsync(id, ct).ConfigureAwait(false);
        if (released)
        {
            metrics.RecordSeatReleased(1);
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
        TimeProvider time,
        CancellationToken ct)
    {
        byte[] lookup = SHA256.HashData(Encoding.UTF8.GetBytes(dto.LicenseKey.Trim()))[..4];

        var license = await db.Licenses
            .Include(l => l.Policy)
            .Include(l => l.Machines)
            .FirstOrDefaultAsync(l => l.KeyLookup == lookup, ct)
            .ConfigureAwait(false);

        if (license is null || license.State != "active")
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

        return TypedResults.Ok(new ActivationResponseDto(machine.Id, license.Id, fp, "active", now));
    }

    private static async Task<IResult> DeactivateAsync(
        string id,
        SymbolonDbContext db,
        IAuditLedger audit,
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

        return TypedResults.NoContent();
    }

    private static async Task<IResult> GetLicenseFileAsync(
        string key,
        SymbolonDbContext db,
        ISignatureProvider signingKey,
        TimeProvider time,
        CancellationToken ct)
    {
        byte[] lookup = SHA256.HashData(Encoding.UTF8.GetBytes(key.Trim()))[..4];

        var license = await db.Licenses
            .Include(l => l.Policy)
            .FirstOrDefaultAsync(l => l.KeyLookup == lookup, ct)
            .ConfigureAwait(false);

        if (license is null || license.State != "active")
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
}
