using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Symbolon.ControlPlane.Models;
using Symbolon.Crypto;
using Symbolon.Data;
using Symbolon.Data.Entities;
using Symbolon.Domain;
using Symbolon.Format;
using Symbolon.Protocol;

namespace Symbolon.ControlPlane.Endpoints;

public static class RelaySyncEndpoints
{
    public static RouteGroupBuilder MapRelaySyncEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/relay/v1").WithTags("RelaySync");

        group.MapPost("/register", RegisterRelayAsync)
            .WithName("RegisterRelay")
            .WithSummary("Zaregistruje on-premise relay uzol.");

        group.MapPost("/grants:request", RequestSeatGrantAsync)
            .WithName("RequestSeatGrant")
            .WithSummary("Požiada o delegovanú kapacitu sedadiel pre relay.");

        group.MapPost("/usage", UploadUsageBatchAsync)
            .WithName("UploadRelayUsage")
            .WithSummary("Dávkový príjem auditných a usage dát z relayu.");

        return group;
    }

    private static async Task<IResult> RegisterRelayAsync(
        RegisterRelayDto dto,
        HttpContext context,
        SymbolonDbContext db,
        TimeProvider time,
        CancellationToken ct)
    {
        string tenantId = context.Request.Headers["X-Tenant-Id"].FirstOrDefault()
            ?? (await db.Tenants.Select(t => t.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false))
            ?? "default";

        string relayId = $"rly_{Guid.NewGuid():N}";
        string apiKey = $"rlykey_{Guid.NewGuid():N}";

        var relay = new RelayEntity
        {
            Id = relayId,
            TenantId = tenantId,
            Name = dto.Name.Trim(),
            MtlsThumbprint = dto.MtlsThumbprint,
            ApiKey = apiKey,
            LastSync = time.GetUtcNow(),
            Version = "1.0.0"
        };

        db.Relays.Add(relay);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return TypedResults.Created($"/relay/v1/relays/{relayId}", new RegisterRelayResponseDto(relayId, apiKey));
    }

    private static async Task<IResult> RequestSeatGrantAsync(
        RequestSeatGrantDto dto,
        SymbolonDbContext db,
        ISignatureProvider signingKey,
        TimeProvider time,
        CancellationToken ct)
    {
        var relay = await db.Relays.FirstOrDefaultAsync(r => r.Id == dto.RelayId, ct).ConfigureAwait(false);
        if (relay is null) return TypedResults.NotFound($"Relay '{dto.RelayId}' not found.");

        var license = await db.Licenses.FirstOrDefaultAsync(l => l.Id == dto.LicenseId, ct).ConfigureAwait(false);
        if (license is null || license.State != "active")
        {
            return TypedResults.NotFound($"License '{dto.LicenseId}' not active or not found.");
        }

        // Find available seats for delegation
        var freeSeats = await db.Seats
            .Where(s => s.LicenseId == dto.LicenseId && s.GrantId == null && s.LeaseId == null)
            .OrderBy(s => s.SeatNo)
            .Take(dto.Seats)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (freeSeats.Count < dto.Seats)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Not Enough Seats To Delegate",
                detail: $"Requested {dto.Seats} seats, but only {freeSeats.Count} available.");
        }

        string grantId = $"gnt_{Guid.NewGuid():N}";
        var now = time.GetUtcNow();
        var notAfter = now.AddDays(dto.DurationDays);
        int seatFrom = freeSeats[0].SeatNo;
        int seatTo = freeSeats[^1].SeatNo;

        // Assign grantId to these seats
        foreach (var seat in freeSeats)
        {
            seat.GrantId = grantId;
        }

        long seq = (await db.SeatGrants.CountAsync(g => g.LicenseId == dto.LicenseId && g.RelayId == dto.RelayId, ct).ConfigureAwait(false)) + 1;

        // Generate signed token
        var claims = new SeatGrantDocumentClaims
        {
            Iss = "symbolon:control-plane",
            Sub = dto.LicenseId,
            Aud = $"relay:{dto.RelayId}",
            Jti = grantId,
            Iat = now.ToUnixTimeSeconds(),
            Nbf = now.ToUnixTimeSeconds(),
            Exp = notAfter.ToUnixTimeSeconds(),
            Symgrant = new SeatGrantPayload
            {
                V = 1,
                Seats = dto.Seats,
                SeatRange = [seatFrom, seatTo],
                Seq = seq,
                Entitlements = ["core"],
                LeasePolicy = new LeasePolicyClaim
                {
                    Ttl = "PT10M",
                    GraceTtl = "PT4H"
                },
                LeaseKey = signingKey.ExportPublicJwk()
            }
        };

        // Create compact or JWS document
        string payloadJson = JsonSerializer.Serialize(claims);
        string payloadB64 = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payloadJson));
        string headerB64 = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { alg = "ES256", typ = "symgrant+jws" })));
        byte[] toSign = Encoding.ASCII.GetBytes($"{headerB64}.{payloadB64}");

        byte[] sig = new byte[signingKey.SignatureSize];
        signingKey.Sign(toSign, sig);
        string sigB64 = Base64Url.EncodeToString(sig);
        string token = $"{headerB64}.{payloadB64}.{sigB64}";

        var grant = new SeatGrantEntity
        {
            Id = grantId,
            LicenseId = dto.LicenseId,
            RelayId = dto.RelayId,
            Seats = dto.Seats,
            SeatFrom = seatFrom,
            SeatTo = seatTo,
            Seq = seq,
            NotBefore = now,
            NotAfter = notAfter,
            Document = token
        };

        db.SeatGrants.Add(grant);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return TypedResults.Ok(new SeatGrantResponseDto(grantId, token, seatFrom, seatTo, now, notAfter));
    }

    private static async Task<IResult> UploadUsageBatchAsync(
        RelayUsageBatchDto dto,
        SymbolonDbContext db,
        IAuditLedger audit,
        TimeProvider time,
        CancellationToken ct)
    {
        var relay = await db.Relays.FirstOrDefaultAsync(r => r.Id == dto.RelayId, ct).ConfigureAwait(false);
        if (relay is not null)
        {
            relay.LastSync = time.GetUtcNow();
        }

        foreach (var ev in dto.Events)
        {
            await audit.AppendAsync(new AuditEvent(
                ev.Type,
                ev.LicenseId,
                null,
                ev.Fingerprint,
                ev.Timestamp,
                $"Relay [{dto.RelayId}]: {ev.Detail}"), ct).ConfigureAwait(false);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(new { processed = dto.Events.Count });
    }
}
