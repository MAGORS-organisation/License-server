# 8. Referenčná implementácia (.NET 10)

> Časť zadania **Symbolon — licenčný server na .NET 10**. Späť na [obsah](README.md) · [prehľad projektu](../README.md).

## 8.1 Štruktúra riešenia

```
symbolon/
├─ Directory.Build.props            # TargetFramework net10.0, Nullable, TreatWarningsAsErrors,
│                                   # AnalysisLevel latest-all, InvariantGlobalization
├─ Directory.Packages.props         # Central Package Management
├─ src/
│  ├─ Symbolon.Format/              # Apache-2.0 — claims, JWS, PEM obálka, verziovanie
│  ├─ Symbolon.Crypto/              # Apache-2.0 — signer/verifier, key ring, JWKS(AKP)
│  ├─ Symbolon.Client/              # Apache-2.0 — SDK: checkout, heartbeat, grace, fingerprint
│  ├─ Symbolon.Protocol/            # Apache-2.0 — DTO kontrakty (zdieľané server↔klient)
│  ├─ Symbolon.Domain/              # AGPL — lease engine, policy engine, invarianty
│  ├─ Symbolon.Data/                # AGPL — EF Core 10, migrácie
│  ├─ Symbolon.ControlPlane/        # AGPL — Minimal API host
│  ├─ Symbolon.Relay/               # AGPL — Minimal API host, SQLite, AOT
│  └─ Symbolon.Cli/                 # AGPL — `symbolon` CLI
├─ tests/
│  ├─ Symbolon.Format.Tests/
│  ├─ Symbolon.Crypto.Tests/        # + FIPS 204 KAT vektory
│  ├─ Symbolon.Domain.Tests/        # + CsCheck property/concurrency
│  ├─ Symbolon.Integration.Tests/   # Testcontainers (Postgres)
│  ├─ Symbolon.Application.Tests/   # WebApplicationFactory E2E scenáre
│  └─ Symbolon.Load/                # NBomber / k6
├─ spec/                            # CC BY 4.0 — symlic/1, protokol, testovacie vektory
└─ deploy/                          # Helm chart, docker-compose, systemd unit
```

## 8.2 Kryptografická vrstva

```csharp
// Symbolon.Crypto/SignatureAlgorithms.cs
namespace Symbolon.Crypto;

/// <summary>JOSE "alg" hodnoty. ML-DSA podľa RFC 9964.</summary>
public static class Alg
{
    public const string Es256   = "ES256";
    public const string MlDsa44 = "ML-DSA-44";
    public const string MlDsa65 = "ML-DSA-65";
    public const string MlDsa87 = "ML-DSA-87";
}

public interface ISignatureProvider : IDisposable
{
    string Alg { get; }
    string Kid { get; }
    bool CanSign { get; }
    int SignatureSize { get; }
    void Sign(ReadOnlySpan<byte> signingInput, Span<byte> destination);
    bool Verify(ReadOnlySpan<byte> signingInput, ReadOnlySpan<byte> signature);
    JsonWebKey ExportPublicJwk();
}
```

```csharp
// Symbolon.Crypto/MlDsaSignatureProvider.cs
#pragma warning disable SYSLIB5006 // ML-DSA je v .NET 10 [Experimental]; izolované v tomto súbore
using System.Security.Cryptography;

namespace Symbolon.Crypto;

public sealed class MlDsaSignatureProvider : ISignatureProvider
{
    private readonly MLDsa _key;
    private readonly bool _canSign;

    public string Alg { get; }
    public string Kid { get; }
    public bool CanSign => _canSign;
    public int SignatureSize { get; }

    private MlDsaSignatureProvider(MLDsa key, string kid, bool canSign)
    {
        _key = key;
        Kid = kid;
        _canSign = canSign;
        Alg = key.Algorithm.Name switch
        {
            "ML-DSA-44" => Symbolon.Crypto.Alg.MlDsa44,
            "ML-DSA-65" => Symbolon.Crypto.Alg.MlDsa65,
            "ML-DSA-87" => Symbolon.Crypto.Alg.MlDsa87,
            var n => throw new NotSupportedException($"Neznámy ML-DSA parameter set: {n}")
        };
        SignatureSize = key.Algorithm.SignatureSizeInBytes;
    }

    /// <summary>Platformová brána. Bez nej padne až pri prvom podpise.</summary>
    public static bool IsSupported => MLDsa.IsSupported;

    public static MlDsaSignatureProvider GenerateKey(MLDsaAlgorithm alg, string kid)
    {
        EnsureSupported();
        return new(MLDsa.GenerateKey(alg), kid, canSign: true);
    }

    /// <summary>Import zo seedu (32 B) — jediný formát privátneho kľúča, ktorý RFC 9964 pripúšťa.</summary>
    public static MlDsaSignatureProvider ImportSeed(MLDsaAlgorithm alg, ReadOnlySpan<byte> seed, string kid)
    {
        EnsureSupported();
        return new(MLDsa.ImportMLDsaPrivateSeed(alg, seed), kid, canSign: true);
    }

    public static MlDsaSignatureProvider ImportPublic(MLDsaAlgorithm alg, ReadOnlySpan<byte> pub, string kid)
    {
        EnsureSupported();
        return new(MLDsa.ImportMLDsaPublicKey(alg, pub), kid, canSign: false);
    }

    public void Sign(ReadOnlySpan<byte> signingInput, Span<byte> destination)
    {
        if (!_canSign) throw new InvalidOperationException("Verify-only kľúč.");
        // Kontext (3. parameter) necháme prázdny — RFC 9964 pre JOSE/COSE
        // nepoužíva FIPS 204 context string. Nepridávať vlastný, rozbilo by to interoperabilitu.
        _key.SignData(signingInput, destination, ReadOnlySpan<byte>.Empty);
    }

    public bool Verify(ReadOnlySpan<byte> signingInput, ReadOnlySpan<byte> signature)
        => signature.Length == SignatureSize
        && _key.VerifyData(signingInput, signature, ReadOnlySpan<byte>.Empty);

    public JsonWebKey ExportPublicJwk() => new()
    {
        Kty = "AKP",                                   // RFC 9964 Algorithm Key Pair
        Alg = Alg,
        Kid = Kid,
        Pub = Base64Url.EncodeToString(_key.ExportMLDsaPublicKey())
    };

    private static void EnsureSupported()
    {
        if (!MLDsa.IsSupported)
            throw new PlatformNotSupportedException(
                "ML-DSA nie je na tejto platforme dostupné. Vyžaduje sa Linux s OpenSSL 3.5+ " +
                "alebo Windows s CNG PQC. macOS nie je v .NET 10 podporovaný. " +
                "Pre overovanie použite SymbolonVerifierOptions.EnableManagedPqcFallback.");
    }

    public void Dispose() => _key.Dispose();
}
#pragma warning restore SYSLIB5006
```

```csharp
// Symbolon.Crypto/Es256SignatureProvider.cs
using System.Security.Cryptography;

public sealed class Es256SignatureProvider(ECDsa key, string kid, bool canSign) : ISignatureProvider
{
    public string Alg => Symbolon.Crypto.Alg.Es256;
    public string Kid => kid;
    public bool CanSign => canSign;
    public int SignatureSize => 64;      // P-256, IEEE P1363 r||s

    public void Sign(ReadOnlySpan<byte> input, Span<byte> dst)
        => key.SignData(input, dst, HashAlgorithmName.SHA256,
                        DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

    public bool Verify(ReadOnlySpan<byte> input, ReadOnlySpan<byte> sig)
        => sig.Length == 64
        && key.VerifyData(input, sig, HashAlgorithmName.SHA256,
                          DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    // …ExportPublicJwk() → {"kty":"EC","crv":"P-256","x":…,"y":…}
    public void Dispose() => key.Dispose();
}
```

## 8.3 Podpisovanie a overovanie licenčného dokumentu

```csharp
// Symbolon.Format/LicenseDocumentSigner.cs
using System.Buffers.Text;
using System.Text;
using System.Text.Json;

public sealed class LicenseDocumentSigner(IReadOnlyList<ISignatureProvider> signers)
{
    public string Sign(LicenseClaims claims, string typ = "symlic+jws")
    {
        byte[] payloadJson = JsonSerializer.SerializeToUtf8Bytes(
            claims, SymbolonJsonContext.Default.LicenseClaims);
        string payloadB64 = Base64Url.EncodeToString(payloadJson);

        var sigs = new List<JwsSignature>(signers.Count);
        foreach (var s in signers)
        {
            var header = new JwsProtectedHeader(
                Alg: s.Alg, Kid: s.Kid, Typ: typ,
                Crit: ["symlic"], Symlic: "1");

            string protectedB64 = Base64Url.EncodeToString(
                JsonSerializer.SerializeToUtf8Bytes(header, SymbolonJsonContext.Default.JwsProtectedHeader));

            // RFC 7515 §5.2: ASCII(BASE64URL(protected) || '.' || BASE64URL(payload))
            byte[] signingInput = Encoding.ASCII.GetBytes($"{protectedB64}.{payloadB64}");

            byte[] signature = new byte[s.SignatureSize];
            s.Sign(signingInput, signature);

            sigs.Add(new JwsSignature(protectedB64, Base64Url.EncodeToString(signature)));
        }

        var doc = new JwsGeneralJson(payloadB64, sigs);
        return PemArmor.Wrap("SYMBOLON LICENSE",
            JsonSerializer.SerializeToUtf8Bytes(doc, SymbolonJsonContext.Default.JwsGeneralJson));
    }
}
```

```csharp
// Symbolon.Format/LicenseDocumentVerifier.cs
public sealed record VerificationResult(
    bool IsValid,
    LicenseClaims? Claims,
    IReadOnlyList<string> VerifiedAlgs,
    IReadOnlyList<string> UnverifiableAlgs,
    string? FailureReason);

public sealed class LicenseDocumentVerifier(
    IKeyRing keyRing,
    TimeProvider time,
    SymbolonVerifierOptions options)
{
    public VerificationResult Verify(string pem)
    {
        if (!PemArmor.TryUnwrap(pem, "SYMBOLON LICENSE", out byte[]? raw))
            return Fail("malformed-pem");

        var doc = JsonSerializer.Deserialize(raw, SymbolonJsonContext.Default.JwsGeneralJson);
        if (doc is null || doc.Signatures.Count == 0) return Fail("no-signatures");

        byte[] payloadBytes = Base64Url.DecodeFromChars(doc.Payload);
        var claims = JsonSerializer.Deserialize(payloadBytes, SymbolonJsonContext.Default.LicenseClaims);
        if (claims is null) return Fail("malformed-payload");

        // ---- 1. Overenie každého podpisu nezávisle -------------------------
        var verified = new List<string>();
        foreach (var sig in doc.Signatures)
        {
            var hdr = ParseProtected(sig.Protected);
            if (hdr is null) continue;

            // crit: neznáme povinné rozšírenie ⇒ tvrdé odmietnutie (RFC 7515 §4.1.11)
            if (hdr.Crit is { } crit && crit.Any(c => c is not "symlic"))
                return Fail($"unknown-crit:{string.Join(',', crit)}");

            if (!keyRing.TryGet(hdr.Kid, hdr.Alg, out ISignatureProvider? key))
                continue;                               // kľúč nepoznáme → alg zostáva neoverený
            if (keyRing.IsRevoked(hdr.Kid))
                return Fail($"revoked-kid:{hdr.Kid}");

            byte[] input = Encoding.ASCII.GetBytes($"{sig.Protected}.{doc.Payload}");
            if (!key.Verify(input, Base64Url.DecodeFromChars(sig.Signature)))
                return Fail($"bad-signature:{hdr.Alg}");  // ZLÝ podpis = fatal, nie "preskoč"

            verified.Add(hdr.Alg);
        }
        if (verified.Count == 0) return Fail("no-verifiable-signature");

        // ---- 2. Obrana proti stripping/downgrade útoku ---------------------
        var required = claims.Symlic.RequiredAlgs;
        var missing  = required.Except(verified).ToArray();
        var unverifiable = missing.Where(a => !options.SupportedAlgs.Contains(a)).ToArray();

        // Chýba algoritmus, ktorý PODPORUJEME ⇒ podpis bol odstránený. Vždy fatal.
        if (missing.Except(unverifiable).Any())
            return Fail($"stripped-signature:{string.Join(',', missing.Except(unverifiable))}");

        // Chýba algoritmus, ktorý NEPODPORUJEME ⇒ rozhoduje politika
        if (unverifiable.Length > 0)
        {
            bool longLived = claims.Exp - claims.Iat > TimeSpan.FromDays(365).TotalSeconds;
            if (options.Strictness is Strictness.Strict || (longLived && options.Strictness is Strictness.Auto))
                return Fail($"unsupported-required-alg:{string.Join(',', unverifiable)}");
        }

        // ---- 3. Časové kontroly (server-authoritative + monotónny detektor) -
        var now = time.GetUtcNow().ToUnixTimeSeconds();
        long skew = (long)options.ClockSkewTolerance.TotalSeconds;
        if (claims.Nbf - skew > now) return Fail("not-yet-valid");
        if (claims.Exp + skew < now) return Fail("expired");
        if (claims.Iat < options.HighWaterMarkIat - skew) return Fail("replayed-older-document");

        return new(true, claims, verified, unverifiable, null);
    }

    private static VerificationResult Fail(string reason) => new(false, null, [], [], reason);
}
```

> **Pozn. pre code review:** riadok `if (!key.Verify(...)) return Fail(...)` musí byť **fatal**, nie `continue`. Ak by sa neplatný podpis len preskočil, útočník pripojí ku platnému ES256 podpisu poškodený ML-DSA podpis a `requiredAlgs` kontrola sa obíde. Toto je najpravdepodobnejšia chyba pri implementácii tohto formátu a patrí do testov ako povinný prípad.

## 8.4 Lease engine — jadro floatingu

```csharp
// Symbolon.Domain/LeaseEngine.cs
public sealed class LeaseEngine(
    ISeatStore seats,
    ILeaseTokenIssuer tokens,
    IAuditLedger audit,
    TimeProvider time,
    ILogger<LeaseEngine> log)
{
    public async Task<CheckoutResult> CheckoutAsync(CheckoutRequest req, CancellationToken ct)
    {
        var now = time.GetUtcNow();

        if (req.Quantity is < 1 or > 64)
            return CheckoutResult.Invalid("quantity-out-of-range");

        // Idempotencia: rovnaký kľúč v okne ⇒ vráť ten istý lease
        if (req.IdempotencyKey is { } ik &&
            await seats.TryGetIdempotentAsync(req.LicenseId, ik, now, ct) is { } existing)
            return CheckoutResult.Ok(existing, tokens.Issue(existing));

        SeatAllocation[]? allocated = req.Quantity == 1
            ? await seats.TryAcquireOneAsync(req.LicenseId, req.Fingerprint, now, req.Ttl, ct)
                is { } one ? [one] : null
            : await seats.TryAcquireManyAsync(req.LicenseId, req.Fingerprint, req.Quantity, now, req.Ttl, ct);

        if (allocated is null)
        {
            await audit.AppendAsync(AuditEvent.Deny(req, now, "seat-pool-exhausted"), ct);
            return req.AllowQueue
                ? CheckoutResult.Queued(await seats.EnqueueAsync(req, now, ct))
                : CheckoutResult.PoolExhausted(await seats.EstimateWaitAsync(req.LicenseId, now, ct));
        }

        await audit.AppendAsync(AuditEvent.Checkout(req, allocated, now), ct);
        return CheckoutResult.Ok(allocated, tokens.Issue(allocated));
    }

    public async Task<RenewResult> RenewAsync(LeaseId id, long clientSeq, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var outcome = await seats.TryRenewAsync(id, clientSeq, now, ct);
        return outcome switch
        {
            RenewOutcome.Renewed r       => RenewResult.Ok(r.Allocation, tokens.Issue(r.Allocation)),
            RenewOutcome.Resurrected r   => RenewResult.Ok(r.Allocation, tokens.Issue(r.Allocation)),
            RenewOutcome.Unknown         => RenewResult.Gone("lease-unknown"),
            RenewOutcome.Taken           => RenewResult.Gone("seat-reassigned"),
            RenewOutcome.SeqReplay       => RenewResult.Conflict("stale-sequence"),
            _                            => RenewResult.Gone("unknown")
        };
    }
}
```

**Kritická SQL časť** (`SeatStore`, Postgres). Toto je miesto, kde sa rozhodne, či je produkt korektný:

```sql
-- TryAcquireOne: atomické, bez COUNT(*), bez explicitného zámku na licencii
UPDATE seats s
   SET lease_id    = @leaseId,
       holder_fp   = @fingerprint,
       machine_id  = @machineId,
       acquired_at = @now,
       expires_at  = @now + @ttl,
       lease_seq   = 0
 WHERE s.id = (
       SELECT id FROM seats
        WHERE license_id = @licenseId
          AND (reserved_for IS NULL OR reserved_for = ANY(@groups))
          AND (lease_id IS NULL OR expires_at < @now - @resurrectionWindow)
          AND (borrowed_until IS NULL OR borrowed_until < @now)
          AND NOT is_overage                       -- overage riadky až v druhom kole
        ORDER BY seat_no
          FOR UPDATE SKIP LOCKED
        LIMIT 1)
RETURNING s.id, s.seat_no, s.expires_at, s.is_overage;
```

```sql
-- TryRenew: obrana proti replayu (lease_seq) aj proti krádeži sedadla iným klientom
UPDATE seats
   SET expires_at = @now + @ttl,
       lease_seq  = lease_seq + 1
 WHERE lease_id  = @leaseId
   AND holder_fp = @fingerprint            -- sedadlo patrí tomu, kto ho žiada
   AND lease_seq = @clientSeq              -- monotónnosť: starý token neobnoví
   AND expires_at > @now - @resurrectionWindow
RETURNING seat_no, expires_at, lease_seq;
```

Pre `Quantity > 1` sa transakcia otvorí s `SELECT pg_advisory_xact_lock(hashtextextended(@licenseId, 0))`, aby N sedadiel padlo buď všetkých, alebo žiadne. Serializuje sa iba per licencia.

Pre relay (SQLite) je ekvivalent jednoduchší — SQLite serializuje zápisy globálne, takže stačí `BEGIN IMMEDIATE` + `UPDATE … WHERE id = (SELECT … LIMIT 1)`. Rozdiel je zapuzdrený za `ISeatStore`; **doménová logika je identická** a testy sa spúšťajú proti obom implementáciám.

## 8.5 Minimal API endpoint

```csharp
// Symbolon.ControlPlane/Endpoints/LeaseEndpoints.cs
public static class LeaseEndpoints
{
    public static RouteGroupBuilder MapLeases(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1/leases")
                   .WithTags("Leases")
                   .RequireRateLimiting("client")
                   .WithRequestTimeout(TimeSpan.FromSeconds(10));

        g.MapPost("/", CheckoutAsync)
         .WithName("CheckoutSeat")
         .WithSummary("Vyžiada sedadlo z floating poolu licencie.")
         .Produces<CheckoutResponse>(StatusCodes.Status200OK)
         .Produces<QueuedResponse>(StatusCodes.Status202Accepted)
         .ProducesProblem(StatusCodes.Status409Conflict)
         .ProducesValidationProblem();

        g.MapPost("/{id}/renew", RenewAsync).WithName("RenewLease");
        g.MapDelete("/{id}",     ReleaseAsync).WithName("ReleaseLease");
        g.MapPost("/{id}/borrow", BorrowAsync).WithName("BorrowLease");
        return g;
    }

    private static async Task<Results<Ok<CheckoutResponse>, Accepted<QueuedResponse>, ProblemHttpResult>>
        CheckoutAsync(
            CheckoutRequestDto dto,                    // validovaný zdrojovo generovanou validáciou
            [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
            LicenseResolver resolver,
            LeaseEngine engine,
            CancellationToken ct)
    {
        var license = await resolver.ResolveAsync(dto.LicenseKey, ct);
        if (license is null)
            return TypedResults.Problem(Problems.LicenseNotFound);   // zámerne rovnaká odpoveď
                                                                     // ako pri neaktívnej licencii
                                                                     // (neúnikať existenciu kľúča)
        var result = await engine.CheckoutAsync(
            new CheckoutRequest(license, dto.ToFingerprint(), dto.Quantity ?? 1,
                                dto.Features, idempotencyKey, dto.AllowQueue ?? false), ct);

        return result switch
        {
            CheckoutResult.Success s  => TypedResults.Ok(CheckoutResponse.From(s)),
            CheckoutResult.QueuedR q  => TypedResults.Accepted($"/v1/queue/{q.Ticket}", QueuedResponse.From(q)),
            CheckoutResult.Exhausted e => TypedResults.Problem(Problems.PoolExhausted(e.EstimatedWait)),
            _                          => TypedResults.Problem(Problems.Invalid(result.Reason))
        };
    }
}
```

```csharp
// Symbolon.Protocol/CheckoutRequestDto.cs
[ValidatableType]
public sealed record CheckoutRequestDto
{
    [Required, RegularExpression(@"^[A-Z0-9]{2,8}-[0-9A-HJKMNP-TV-Z]{5}(-[0-9A-HJKMNP-TV-Z]{5}){4}$")]
    public required string LicenseKey { get; init; }

    [Required, MinLength(1)]
    public required IReadOnlyDictionary<string, string> FingerprintComponents { get; init; }

    [Range(1, 64)] public int? Quantity { get; init; }
    public IReadOnlyList<string>? Features { get; init; }
    public bool? AllowQueue { get; init; }
}
```

## 8.6 Klientske SDK — použitie

```csharp
// Program.cs aplikácie ISV
var licensing = new SymbolonClient(new SymbolonClientOptions
{
    ServerUri     = new Uri("https://licenses.acme.local"),
    LicenseKey    = config["Licensing:Key"]!,
    ProductCode   = "acme-cad",
    // Verejné kľúče zapečené v binárke — koreň dôvery klienta
    TrustedKeys   = SymbolonKeyRing.FromEmbedded(typeof(Program).Assembly, "acme.keys.jwks"),
    Strictness    = Strictness.Auto,          // dlhoveké licencie vyžadujú PQ podpis
    EnableManagedPqcFallback = true           // BouncyCastle verify-only na macOS
});

await using var seat = await licensing.AcquireSeatAsync(
    features: ["module.cad-export"], ct);

if (!seat.Acquired)
{
    // seat.Reason: PoolExhausted | Expired | Revoked | Offline | Denied
    ShowLicensingDialog(seat);
    return;
}

// heartbeat beží na pozadí; grace pri výpadku servera je automatický
seat.StateChanged += (_, e) => log.LogWarning("Licencia: {State} ({Detail})", e.State, e.Detail);

RunApplication(seat.Entitlements);
// Dispose ⇒ explicitné uvoľnenie sedadla (nečaká sa na TTL)
```

**Vlastnosti SDK, ktoré rozhodujú o adopcii:**
- `await using` ⇒ sedadlo sa vždy vráti, aj pri výnimke. Toto samo eliminuje väčšinu „zombie seat" tiketov.
- Heartbeat na `PeriodicTimer` s jitterom ±10 % (proti thundering herd pri hromadnom štarte 500 staníc ráno o 8:00).
- Offline cache: posledný `.symlic` + posledný lease token v `%LOCALAPPDATA%`/`$XDG_STATE_HOME` s DPAPI/keyring ochranou; pri štarte bez siete sa použije grace.
- Žiadne blokujúce volania v UI vlákne, žiadne `.Result`.
- Kompletne `TimeProvider`-ové, teda testovateľné bez `Thread.Sleep`.

## 8.7 CLI

```bash
# Kľúčový kruh
symbolon keys generate --role product --alg ES256      --kid prd-acme-2026-ec
symbolon keys generate --role product --alg ML-DSA-65  --kid prd-acme-2026-pq
symbolon keys export-jwks --out acme.keys.jwks         # do binárky aplikácie

# Licencie
symbolon license issue --policy pro-floating-annual --customer CUST-4711 \
    --seats 25 --expires 2027-09-03 --out acme-4711.symlic
symbolon license inspect acme-4711.symlic              # dekóduje a overí, vypíše claims
symbolon license revoke lic_01JQ… --reason non-payment

# Air-gapped
symbolon grant issue --in relay.symreq --out relay.symgrant
symbolon relay import-grant relay.symgrant

# Diagnostika u zákazníka — najdôležitejší príkaz pre support
symbolon doctor --server https://licenses.acme.local
#  ✔ konektivita        ✔ TLS reťazec       ✔ hodiny (odchýlka 0.4 s)
#  ✔ fingerprint        ⚠ kontajner zistený → odporúčam floating, nie node-lock
#  ✔ ES256 overenie     ✖ ML-DSA: nepodporované (macOS) → použitý managed fallback
```

---

[← 7. Floating licencie — protokol](07-floating-protokol.md) · [Obsah](README.md) · [9. Bezpečnosť →](09-bezpecnost.md)
