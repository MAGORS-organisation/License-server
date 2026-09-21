using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Symbolon.Crypto;
using Symbolon.Data;
using Symbolon.Data.Entities;
using Symbolon.Domain;
using Symbolon.Format;
using Symbolon.Protocol;

namespace Symbolon.ControlPlane.Endpoints;

public static class OfflineEndpoints
{
    public static RouteGroupBuilder MapOfflineEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/offline").WithTags("AirGap");

        group.MapPost("/grants", IssueAirGapGrantAsync).WithName("IssueAirGapGrant");
        group.MapPost("/activations", IssueAirGapActivationAsync).WithName("IssueAirGapActivation");

        return group;
    }

    private static async Task<IResult> IssueAirGapGrantAsync(
        OfflineGrantRequestDto dto,
        SymbolonDbContext db,
        ISignatureProvider signingKey,
        IAuditLedger audit,
        TimeProvider time,
        CancellationToken ct)
    {
        var now = time.GetUtcNow();

        // 1. Locate license by KeyLookup or ID
        byte[] lookup = SHA256.HashData(Encoding.UTF8.GetBytes(dto.LicenseKey.Trim()))[..4];
        var license = await db.Licenses
            .Include(l => l.Policy)
            .FirstOrDefaultAsync(l => l.KeyLookup == lookup || l.Id == dto.LicenseKey.Trim(), ct)
            .ConfigureAwait(false);

        if (license is null || license.State != "active")
        {
            return TypedResults.NotFound($"License for key '{dto.LicenseKey}' not found or inactive.");
        }

        // 2. Ensure relay exists or auto-register relay placeholder
        var relay = await db.Relays.FirstOrDefaultAsync(r => r.Id == dto.RelayId, ct).ConfigureAwait(false);
        if (relay is null)
        {
            relay = new RelayEntity
            {
                Id = dto.RelayId,
                TenantId = license.TenantId,
                Name = $"Air-Gapped Relay {dto.RelayId}",
                ApiKey = $"rly_airgap_{Guid.NewGuid():N}",
                LastSync = now,
                Version = "1.0.0"
            };
            db.Relays.Add(relay);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        // 3. Record the usageDigest in the immutable audit ledger (anti-abuse guarantee)
        await audit.AppendAsync(new AuditEvent(
            "airgap_usage_digest",
            license.Id,
            null,
            null,
            now,
            $"Air-gapped relay usage digest reported. LastSeq={dto.LastSeq}, Digest={dto.UsageDigest}"),
            ct).ConfigureAwait(false);

        // 4. Find available seats
        var freeSeats = await db.Seats
            .Where(s => s.LicenseId == license.Id && s.GrantId == null && s.LeaseId == null)
            .OrderBy(s => s.SeatNo)
            .Take(dto.RequestedSeats)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (freeSeats.Count < dto.RequestedSeats)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Not Enough Seats Available",
                detail: $"Requested {dto.RequestedSeats} seats, but only {freeSeats.Count} available for delegation.");
        }

        string grantId = $"gnt_{Guid.NewGuid():N}";
        int seatFrom = freeSeats[0].SeatNo;
        int seatTo = freeSeats[^1].SeatNo;

        foreach (var seat in freeSeats)
        {
            seat.GrantId = grantId;
        }

        long seq = (await db.SeatGrants.CountAsync(g => g.LicenseId == license.Id && g.RelayId == dto.RelayId, ct).ConfigureAwait(false)) + 1;
        var notAfter = now.AddDays(14); // 14-day air-gap grant window

        var claims = new SeatGrantDocumentClaims
        {
            Iss = "symbolon:control-plane",
            Sub = license.Id,
            Aud = $"relay:{dto.RelayId}",
            Jti = grantId,
            Iat = now.ToUnixTimeSeconds(),
            Nbf = now.ToUnixTimeSeconds(),
            Exp = notAfter.ToUnixTimeSeconds(),
            Symgrant = new SeatGrantPayload
            {
                V = 1,
                Seats = dto.RequestedSeats,
                SeatRange = [seatFrom, seatTo],
                Seq = seq,
                Entitlements = ["core", "offline"],
                LeasePolicy = new LeasePolicyClaim
                {
                    Ttl = "PT10M",
                    GraceTtl = "PT24H"
                },
                LeaseKey = signingKey.ExportPublicJwk(),
                OfflineExtension = new OfflineExtensionClaim
                {
                    Allowed = true,
                    MaxExtensions = 7
                }
            }
        };

        string payloadJson = JsonSerializer.Serialize(claims);
        string payloadB64 = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payloadJson));
        string headerB64 = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { alg = "ES256", typ = "symgrant+jws" })));
        byte[] toSign = Encoding.ASCII.GetBytes($"{headerB64}.{payloadB64}");

        byte[] sig = new byte[signingKey.SignatureSize];
        signingKey.Sign(toSign, sig);
        string sigB64 = Base64Url.EncodeToString(sig);
        string token = $"{headerB64}.{payloadB64}.{sigB64}";

        string pemArmored = PemArmor.Wrap("SYMBOLON SEAT GRANT", Encoding.UTF8.GetBytes(token));

        var grantEntity = new SeatGrantEntity
        {
            Id = grantId,
            LicenseId = license.Id,
            RelayId = dto.RelayId,
            Seats = dto.RequestedSeats,
            SeatFrom = seatFrom,
            SeatTo = seatTo,
            Seq = seq,
            NotBefore = now,
            NotAfter = notAfter,
            Document = token
        };

        db.SeatGrants.Add(grantEntity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var response = new OfflineGrantResponseDto
        {
            GrantId = grantId,
            LicenseId = license.Id,
            RelayId = dto.RelayId,
            SeatFrom = seatFrom,
            SeatTo = seatTo,
            Seq = seq,
            ExpiresAt = notAfter,
            SymgrantPem = pemArmored
        };

        return TypedResults.Ok(response);
    }

    private static async Task<IResult> IssueAirGapActivationAsync(
        OfflineActivationRequestDto dto,
        SymbolonDbContext db,
        ISignatureProvider signingKey,
        TimeProvider time,
        CancellationToken ct)
    {
        var now = time.GetUtcNow();

        byte[] lookup = SHA256.HashData(Encoding.UTF8.GetBytes(dto.LicenseKey.Trim()))[..4];
        var license = await db.Licenses
            .Include(l => l.Policy)
            .Include(l => l.Machines)
            .FirstOrDefaultAsync(l => l.KeyLookup == lookup || l.Id == dto.LicenseKey.Trim(), ct)
            .ConfigureAwait(false);

        if (license is null || license.State != "active")
        {
            return TypedResults.NotFound($"License '{dto.LicenseKey}' not found or inactive.");
        }

        string machineId = $"mch_{Guid.NewGuid():N}";
        var machine = new MachineEntity
        {
            Id = machineId,
            LicenseId = license.Id,
            Fingerprint = dto.Fingerprint,
            ComponentsJson = JsonSerializer.Serialize(new Dictionary<string, string> { { "machine", dto.MachineName ?? "AirGapStation" } }),
            FirstSeen = now,
            LastHeartbeat = now,
            State = "active"
        };

        db.Machines.Add(machine);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        string customer = license.CustomerRef ?? "AirGapCustomer";
        string productId = license.Policy?.ProductId ?? "airgap-product";
        string type = license.Policy?.LicenseModel ?? "nodelock";

        var docClaims = new
        {
            iss = "symbolon:control-plane",
            sub = license.Id,
            aud = dto.Fingerprint,
            jti = machineId,
            iat = now.ToUnixTimeSeconds(),
            exp = license.ExpiresAt?.ToUnixTimeSeconds() ?? now.AddYears(1).ToUnixTimeSeconds(),
            symlic = new
            {
                v = 1,
                customer,
                product = productId,
                type,
                seats = license.MaxSeats,
                fingerprint = dto.Fingerprint
            }
        };

        string claimsJson = JsonSerializer.Serialize(docClaims);
        string pB64 = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(claimsJson));
        string hB64 = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { alg = "ES256", typ = "symlic+jws" })));
        byte[] input = Encoding.ASCII.GetBytes($"{hB64}.{pB64}");
        byte[] s = new byte[signingKey.SignatureSize];
        signingKey.Sign(input, s);
        string sB64 = Base64Url.EncodeToString(s);
        string jws = $"{hB64}.{pB64}.{sB64}";
        string pemFile = PemArmor.Wrap("SYMBOLON LICENSE KEY", Encoding.UTF8.GetBytes(jws));

        var response = new OfflineActivationResponseDto
        {
            ActivationId = machineId,
            LicenseId = license.Id,
            Fingerprint = dto.Fingerprint,
            ExpiresAt = license.ExpiresAt,
            SymlicPem = pemFile
        };

        return TypedResults.Ok(response);
    }
}
