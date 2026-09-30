using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Symbolon.Crypto;
using Symbolon.Data;
using Symbolon.Data.Entities;
using Symbolon.Domain;
using Symbolon.Domain.Grants;
using Symbolon.Format;
using Symbolon.Protocol;

namespace Symbolon.ControlPlane.Endpoints;

public static class OfflineEndpoints
{
    public static RouteGroupBuilder MapOfflineEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/offline").WithTags("AirGap");

        group.MapPost("/requests", ProcessAirGapRequestAsync).WithName("ProcessAirGapRequest");
        group.MapPost("/grants", IssueAirGapGrantAsync).WithName("IssueAirGapGrant");
        group.MapPost("/activations", IssueAirGapActivationAsync).WithName("IssueAirGapActivation");

        return group;
    }

    /// <summary>
    /// Processes a signed .symreq air-gapped capacity transfer request (FLT-32..35, GNT-1..10).
    /// </summary>
    private static async Task<IResult> ProcessAirGapRequestAsync(
        OfflineAirGapRequestDto dto,
        SymbolonDbContext db,
        ISeatGrantStore grantStore,
        IKeyRing keyRing,
        ISignatureProvider signingKey,
        IAuditLedger audit,
        TimeProvider time,
        CancellationToken ct)
    {
        if (dto is null || string.IsNullOrWhiteSpace(dto.RequestPem))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid Request",
                detail: "RequestPem is required.");
        }

        var now = time.GetUtcNow();

        // 1. Verify .symreq artifact
        var reqVerifier = new AirGapRequestVerifier(keyRing, time);
        var verifyResult = reqVerifier.Verify(dto.RequestPem);
        if (!verifyResult.IsValid || verifyResult.Claims is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid Grant Request",
                detail: verifyResult.FailureReason ?? "Failed to verify .symreq document.");
        }

        var symreq = verifyResult.Claims.Symreq;

        // 2. Anti-replay nonce check (FLT-34)
        if (await grantStore.HasNonceBeenSeenAsync(symreq.Nonce, ct).ConfigureAwait(false))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Replay Detected",
                detail: $"Request nonce '{symreq.Nonce}' has already been processed (FLT-34).");
        }

        // 3. Usage digest continuity check (FLT-33)
        if (symreq.LastSeq > 0 && string.IsNullOrWhiteSpace(symreq.UsageDigest))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Usage Digest Required",
                detail: "Grant request with lastSeq > 0 must provide a valid usageDigest (FLT-33).");
        }

        // 4. Record nonce
        await grantStore.RecordNonceAsync(symreq.Nonce, symreq.RelayId, now, now.AddDays(30), ct).ConfigureAwait(false);

        // 5. Locate license
        byte[] rawKeyBytes = Encoding.UTF8.GetBytes(symreq.LicenseKey.Trim());
        byte[] lookup = SHA256.HashData(rawKeyBytes)[..4];
        string fullHashHex = Convert.ToHexString(SHA256.HashData(rawKeyBytes));

        var license = await db.Licenses
            .Include(l => l.Policy)
            .FirstOrDefaultAsync(l => l.KeyLookup == lookup || l.Id == symreq.LicenseKey.Trim(), ct)
            .ConfigureAwait(false);

        if (license is null || license.State != "active" ||
            (license.Id != symreq.LicenseKey.Trim() && !string.Equals(license.KeyHash, fullHashHex, StringComparison.OrdinalIgnoreCase)))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "License Not Found",
                detail: $"License for key '{symreq.LicenseKey}' not found or inactive.");
        }

        // 6. Ensure relay exists or auto-register
        var relay = await db.Relays.FirstOrDefaultAsync(r => r.Id == symreq.RelayId, ct).ConfigureAwait(false);
        if (relay is null)
        {
            relay = new RelayEntity
            {
                Id = symreq.RelayId,
                TenantId = license.TenantId,
                Name = $"Air-Gapped Relay {symreq.RelayId}",
                ApiKey = $"rly_airgap_{Guid.NewGuid():N}",
                LastSync = now,
                Version = "1.0.0"
            };
            db.Relays.Add(relay);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        // 7. Record usage audit event (anti-abuse reporting)
        await audit.AppendAsync(new AuditEvent(
            "airgap_usage_digest",
            license.Id,
            null,
            null,
            now,
            $"Air-gapped relay usage digest reported. RelayId={symreq.RelayId}, LastSeq={symreq.LastSeq}, Digest={symreq.UsageDigest}"),
            ct).ConfigureAwait(false);

        // 8. Allocate contiguous non-overlapping seat range (GNT-4, GNT-5)
        var activeGrants = await grantStore.GetActiveGrantsForLicenseAsync(license.Id, now, ct).ConfigureAwait(false);
        var activeRanges = activeGrants.Select(g => new SeatRange(g.SeatFrom, g.SeatTo)).ToList();

        var allocatedRange = DisjunctiveGrantAllocator.AllocateContiguousRange(license.MaxSeats, activeRanges, symreq.RequestedSeats);
        if (allocatedRange is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Insufficient Capacity",
                detail: $"Cannot allocate {symreq.RequestedSeats} contiguous non-overlapping seats from license with limit {license.MaxSeats} (GNT-4, GNT-5).");
        }

        int seatFrom = allocatedRange.Value.From;
        int seatTo = allocatedRange.Value.To;

        // 9. Sequence & supersedes tracking (GNT-8, GNT-9)
        long highestSeq = await grantStore.GetHighestSeqAsync(license.Id, symreq.RelayId, ct).ConfigureAwait(false);
        long seq = highestSeq + 1;
        long? supersedes = highestSeq > 0 ? highestSeq : null;

        // 10. Generate ephemeral relay lease signing key
        using var relayLeaseKey = Es256SignatureProvider.GenerateKey($"lease-{symreq.RelayId}-{seq}");
        var leaseKeyJwk = relayLeaseKey.ExportPrivateJwk();

        // 11. Build SeatGrant document claims
        string grantId = $"gnt_{Guid.NewGuid():N}";
        var notAfter = now.AddDays(14); // 14-day air-gap grant window

        var grantClaims = new SeatGrantDocumentClaims
        {
            Iss = "symbolon:control-plane",
            Sub = license.Id,
            Aud = symreq.RelayId,
            Jti = grantId,
            Iat = now.ToUnixTimeSeconds(),
            Nbf = now.ToUnixTimeSeconds(),
            Exp = notAfter.ToUnixTimeSeconds(),
            Symgrant = new SeatGrantPayload
            {
                V = 1,
                Seats = symreq.RequestedSeats,
                SeatRange = [seatFrom, seatTo],
                Seq = seq,
                Supersedes = supersedes,
                Entitlements = ["core", "offline"],
                LeasePolicy = new LeasePolicyClaim
                {
                    Ttl = "PT10M",
                    GraceTtl = "PT24H"
                },
                LeaseKey = leaseKeyJwk,
                OfflineExtension = new OfflineExtensionClaim
                {
                    Allowed = true,
                    MaxExtensions = 7
                }
            }
        };

        // 12. Hybrid signing (ES256 + ML-DSA-65) per GNT-1, GNT-2
        var signers = new List<ISignatureProvider> { signingKey };
        if (keyRing.TryGet("cp-pqc-key", Alg.MlDsa65, out var pqcKey))
        {
            signers.Add(pqcKey);
        }
        var grantSigner = new SeatGrantSigner(signers);
        string symgrantPem = grantSigner.Sign(grantClaims);

        // 13. Persist grant
        var grantRecord = new SeatGrantRecord(
            Id: grantId,
            LicenseId: license.Id,
            RelayId: symreq.RelayId,
            Seats: symreq.RequestedSeats,
            SeatFrom: seatFrom,
            SeatTo: seatTo,
            Seq: seq,
            Supersedes: supersedes,
            NotBefore: now,
            NotAfter: notAfter,
            RevokedAt: null,
            Document: symgrantPem
        );
        await grantStore.SaveGrantAsync(grantRecord, ct).ConfigureAwait(false);

        // 14. Return bundled artifact (FLT-35)
        var response = new OfflineAirGapResponseDto
        {
            GrantId = grantId,
            LicenseId = license.Id,
            RelayId = symreq.RelayId,
            Seats = symreq.RequestedSeats,
            SeatFrom = seatFrom,
            SeatTo = seatTo,
            Seq = seq,
            Supersedes = supersedes,
            ExpiresAt = notAfter,
            SymgrantPem = symgrantPem
        };

        return Results.Ok(response);
    }

    private static async Task<IResult> IssueAirGapGrantAsync(
        OfflineGrantRequestDto dto,
        SymbolonDbContext db,
        ISeatGrantStore grantStore,
        IKeyRing keyRing,
        ISignatureProvider signingKey,
        IAuditLedger audit,
        TimeProvider time,
        CancellationToken ct)
    {
        var now = time.GetUtcNow();

        // 1. Locate license by KeyLookup or ID
        byte[] rawKeyBytes = Encoding.UTF8.GetBytes(dto.LicenseKey.Trim());
        byte[] lookup = SHA256.HashData(rawKeyBytes)[..4];
        string fullHashHex = Convert.ToHexString(SHA256.HashData(rawKeyBytes));

        var license = await db.Licenses
            .Include(l => l.Policy)
            .FirstOrDefaultAsync(l => l.KeyLookup == lookup || l.Id == dto.LicenseKey.Trim(), ct)
            .ConfigureAwait(false);

        if (license is null || license.State != "active" ||
            (license.Id != dto.LicenseKey.Trim() && !string.Equals(license.KeyHash, fullHashHex, StringComparison.OrdinalIgnoreCase)))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "License Not Found",
                detail: $"License for key '{dto.LicenseKey}' not found or inactive.");
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

        // 3. Record the usageDigest in the immutable audit ledger
        await audit.AppendAsync(new AuditEvent(
            "airgap_usage_digest",
            license.Id,
            null,
            null,
            now,
            $"Air-gapped relay usage digest reported. LastSeq={dto.LastSeq}, Digest={dto.UsageDigest}"),
            ct).ConfigureAwait(false);

        // 4. Allocate contiguous non-overlapping seats (GNT-4, GNT-5)
        var activeGrants = await grantStore.GetActiveGrantsForLicenseAsync(license.Id, now, ct).ConfigureAwait(false);
        var activeRanges = activeGrants.Select(g => new SeatRange(g.SeatFrom, g.SeatTo)).ToList();

        var allocatedRange = DisjunctiveGrantAllocator.AllocateContiguousRange(license.MaxSeats, activeRanges, dto.RequestedSeats);
        if (allocatedRange is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Not Enough Seats Available",
                detail: $"Requested {dto.RequestedSeats} seats, but cannot allocate contiguous non-overlapping range in limit {license.MaxSeats}.");
        }

        int seatFrom = allocatedRange.Value.From;
        int seatTo = allocatedRange.Value.To;

        long highestSeq = await grantStore.GetHighestSeqAsync(license.Id, dto.RelayId, ct).ConfigureAwait(false);
        long seq = highestSeq + 1;
        long? supersedes = highestSeq > 0 ? highestSeq : null;
        var notAfter = now.AddDays(14); // 14-day air-gap grant window
        string grantId = $"gnt_{Guid.NewGuid():N}";

        using var relayLeaseKey = Es256SignatureProvider.GenerateKey($"lease-{dto.RelayId}-{seq}");
        var leaseKeyJwk = relayLeaseKey.ExportPrivateJwk();

        var claims = new SeatGrantDocumentClaims
        {
            Iss = "symbolon:control-plane",
            Sub = license.Id,
            Aud = dto.RelayId,
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
                Supersedes = supersedes,
                Entitlements = ["core", "offline"],
                LeasePolicy = new LeasePolicyClaim
                {
                    Ttl = "PT10M",
                    GraceTtl = "PT24H"
                },
                LeaseKey = leaseKeyJwk,
                OfflineExtension = new OfflineExtensionClaim
                {
                    Allowed = true,
                    MaxExtensions = 7
                }
            }
        };

        var signers = new List<ISignatureProvider> { signingKey };
        if (keyRing.TryGet("cp-pqc-key", Alg.MlDsa65, out var pqcKey))
        {
            signers.Add(pqcKey);
        }
        var grantSigner = new SeatGrantSigner(signers);
        string pemArmored = grantSigner.Sign(claims);

        var grantRecord = new SeatGrantRecord(
            Id: grantId,
            LicenseId: license.Id,
            RelayId: dto.RelayId,
            Seats: dto.RequestedSeats,
            SeatFrom: seatFrom,
            SeatTo: seatTo,
            Seq: seq,
            Supersedes: supersedes,
            NotBefore: now,
            NotAfter: notAfter,
            RevokedAt: null,
            Document: pemArmored
        );
        await grantStore.SaveGrantAsync(grantRecord, ct).ConfigureAwait(false);

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

        return Results.Ok(response);
    }

    private static async Task<IResult> IssueAirGapActivationAsync(
        OfflineActivationRequestDto dto,
        SymbolonDbContext db,
        ISignatureProvider signingKey,
        TimeProvider time,
        CancellationToken ct)
    {
        var now = time.GetUtcNow();

        byte[] rawKeyBytes = Encoding.UTF8.GetBytes(dto.LicenseKey.Trim());
        byte[] lookup = SHA256.HashData(rawKeyBytes)[..4];
        string fullHashHex = Convert.ToHexString(SHA256.HashData(rawKeyBytes));

        var license = await db.Licenses
            .Include(l => l.Policy)
            .Include(l => l.Machines)
            .FirstOrDefaultAsync(l => l.KeyLookup == lookup || l.Id == dto.LicenseKey.Trim(), ct)
            .ConfigureAwait(false);

        if (license is null || license.State != "active" ||
            (license.Id != dto.LicenseKey.Trim() && !string.Equals(license.KeyHash, fullHashHex, StringComparison.OrdinalIgnoreCase)))
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
