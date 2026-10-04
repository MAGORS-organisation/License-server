# 3. `symlic/1` — License File

> [← License Key](02-license-key.md) · [Table of Contents](README.md) · [Lease Token →](04-lease-token.md)

The License File (`.symlic`) is a **long-lived cryptographically signed artifact** that carries entitlements, limits, and optional hardware binding. Once deployed to customer environments, it is the only part of the system that cannot be changed dynamically.

---

## 3.1 Envelope

**LIC-1.** The file MUST be a PEM armor in the style of [RFC 7468](https://www.rfc-editor.org/rfc/rfc7468) with the label `SYMBOLON LICENSE`:

```
-----BEGIN SYMBOLON LICENSE-----
eyJwYXlsb2FkIjoiZXlKcGMzTWlPaUp6ZVc0dGRHVnpkQ0lzSW5OMVlpSTZJbXhwWXlY...
-----END SYMBOLON LICENSE-----
```

**LIC-2.** The envelope body MUST be **standard Base64** (not base64url), wrapped at 64 characters per line.

**LIC-3.** The decoded body MUST be a JWS document in **General JSON Serialization** ([RFC 7515](https://www.rfc-editor.org/rfc/rfc7515) §7.2).

**LIC-4.** A verifier MUST accept bare JWS JSON documents without PEM armor. The armor is a transport convention, not part of the signed payload.

**LIC-5.** The recommended file extension is `.symlic`. A verifier MUST NOT make validity decisions based on file extension or filename.

---

## 3.2 JWS Structure

```jsonc
{
  "payload": "<base64url(claims JSON)>",
  "signatures": [
    {
      "protected": "<base64url({\"alg\":\"ES256\",\"kid\":\"prd-acme-2026-09-ec\",\"typ\":\"symlic+jws\",\"crit\":[\"symlic\"],\"symlic\":\"1\"})>",
      "signature": "<base64url(64 B)>"
    },
    {
      "protected": "<base64url({\"alg\":\"ML-DSA-65\",\"kid\":\"prd-acme-2026-09-pq\",\"typ\":\"symlic+jws\",\"crit\":[\"symlic\"],\"symlic\":\"1\"})>",
      "signature": "<base64url(3309 B)>"
    }
  ]
}
```

**LIC-6.** The `signatures` array MUST contain at least one element.

**LIC-7.** Every signature element MUST have a `protected` header containing at least `alg`, `kid`, `typ`, and `symlic`.

**LIC-8.** The `typ` parameter MUST be `symlic+jws`.

**LIC-9.** The `crit` parameter MUST contain `"symlic"`. The header parameter `symlic` MUST be a string matching the major format version (`"1"`).

> **Non-Normative Rationale.** `crit` guarantees that any third-party JWS implementation unaware of Symbolon will **reject** the document immediately instead of silently accepting it without verifying format version semantics (RFC 7515 §4.1.11).

**LIC-10.** The `unprotected` header MUST NOT be used. All header parameters MUST be placed in `protected`.

**LIC-11.** An issuer MUST NOT place two signatures with the same `alg` in a single document.

---

## 3.3 Claims (Payload)

```jsonc
{
  "iss": "https://licenses.acme.example",
  "sub": "lic_01JQ8ZK4N9V2X6M0",
  "aud": "acme-cad",
  "jti": "lf_01JQ8ZK5T3P7Q1R4",
  "iat": 1788480000,
  "nbf": 1788480000,
  "exp": 1791072000,
  "symlic": {
    "v": 1,
    "profile": "hybrid-v1",
    "requiredAlgs": ["ES256", "ML-DSA-65"],
    "license": {
      "key": "SYM-4K7QT-…",
      "model": "floating",
      "state": "active",
      "issuedAt": "2026-09-03T00:00:00Z",
      "expiresAt": null,
      "maintenanceUntil": "2027-09-03T00:00:00Z",
      "customer": { "ref": "CUST-4711", "name": "ACME Engineering GmbH" }
    },
    "limits": {
      "maxSeats": 25, "seatUnit": "machine",
      "overageStrategy": "no-overage",
      "maxRelays": 3
    },
    "entitlements": [
      { "code": "core" },
      { "code": "module.cad-export" },
      { "code": "quota.render-minutes", "value": 5000, "period": "P1M" }
    ],
    "binding": {
      "fingerprint": "sha256:9f2c…",
      "matching": "match-most",
      "components": { "machineId": "…", "cpu": "…", "board": "…" }
    },
    "policy": {
      "lease": { "ttl": "PT10M", "graceTtl": "PT4H" },
      "clockSkewTolerance": "PT5M",
      "revocation": { "url": "https://…/v1/revocations/latest", "maxAge": "P7D" }
    }
  }
}
```

### 3.3.1 Registered Claims

| Claim | Requirement | Semantic Meaning |
|---|---|---|
| `iss` | MUST | Issuer URI |
| `sub` | MUST | License ID (ULID with `lic_` prefix) |
| `aud` | MUST | Target Product Code |
| `jti` | MUST | Unique ID of **this specific file** (ULID with `lf_` prefix), acts as nonce |
| `iat` | MUST | Issuance timestamp (UTC seconds) |
| `nbf` | SHOULD | Not before timestamp (defaults to `iat` if omitted) |
| `exp` | MUST | **File snapshot TTL**, not license validity — see `LIC-14` |

**LIC-12.** `jti` MUST be globally unique across all files issued for a given license.

**LIC-13.** `aud` MUST match the application's product code. A verifier MUST reject any file whose `aud` does not match the host product.

**LIC-14.** The `exp` claim specifies the **validity of the file snapshot**, not the license. Perpetual validity is declared via `symlic.license.expiresAt: null`.

---

## 3.4 Normative Validation Pipeline

**LIC-21.** Each signature MUST be verified independently over:
```
ASCII(BASE64URL(protected) || '.' || BASE64URL(payload))
```
Signatures MUST NOT be combined or aggregated.

**LIC-22.** The verifier MUST check all algorithms listed in `symlic.requiredAlgs` supported by its cryptographic suite.

**LIC-23.** If an algorithm listed in `requiredAlgs` is supported by the verifier but its signature in the document is **missing or invalid**, the document MUST be rejected (**Downgrade Defense**).

**LIC-25.** If `requiredAlgs` contains an algorithm completely unknown to the verifier:
- `strict`: MUST reject.
- `auto` (default): MUST reject if `exp - iat > 1 year`; otherwise MAY accept and MUST log `degraded-verification`.
- `lenient`: MAY accept and MUST log `degraded-verification`.

**LIC-29.** The verifier MUST reject a file if `now < nbf - skew` or `now > exp + skew`.

**LIC-30.** The verifier MUST persist the **highest seen `iat`** for the license and MUST reject any file whose `iat < HighWaterMarkIat - skew` (**Monotonic Replay Defense**).

**LIC-31.** The verifier MUST reject any file signed with a `kid` present on a valid revocation list.

**LIC-34.** Validation MUST execute in this exact sequence:
1. Envelope unwrap and JWS parsing (`LIC-1`–`LIC-5`)
2. Header and `crit` validation (`LIC-7`–`LIC-11`, `LIC-15`)
3. Independent signature verification (`LIC-21`–`LIC-28`)
4. Time validation (`LIC-29`, `LIC-30`)
5. Revocation checks (`LIC-31`, `LIC-32`)
6. State and audience matching (`LIC-13`, `LIC-17`)
7. Machine hardware binding (`LIC-33`)

---

## 3.5 Cryptographic Profiles

| Profile | `requiredAlgs` | Target Usage |
|---|---|---|
| `classical-v1` | `["ES256"]` | Legacy platforms without PQC capability |
| `hybrid-v1` | `["ES256", "ML-DSA-65"]` | **Default** enterprise standard |
| `pq-only-v1` | `["ML-DSA-65"]` | CNSA 2.0 / Strict post-quantum mandate |

**LIC-39.** Classical and post-quantum signatures MUST be produced by independent private keys. Composite key schemes are NOT permitted in `symlic/1`.

---

## 3.6 Keys and JWKS

**LIC-40.** The `kid` parameter MUST follow the naming convention `{role}-{tenant}-{yyyy-MM}-{alg}`, e.g., `prd-acme-2026-09-pq`.

**LIC-41.** Public keys MUST be published as JWKS at `GET /v1/.well-known/symbolon-keys`.

**LIC-42.** Post-quantum keys in JWKS MUST use key type `kty: AKP` conforming to RFC 9964.

---

## 3.7 Format Versioning

**LIC-43.** Adding new fields to `symlic` is always permitted and MUST NOT increment `symlic.v`.

**LIC-44.** Removing fields or modifying existing field semantics MUST increment `symlic.v` and MUST change the document type header (`symlic2+jws`).

**LIC-45.** A client SHOULD declare supported versions using the header `X-Symbolon-Accept: symlic/1`. The server MUST issue the highest version mutually supported.

---

## 3.8 Optional Profile `symlic+cose`

**LIC-46.** An implementation MAY support the alternative serialization `COSE_Sign` ([RFC 9052](https://www.rfc-editor.org/rfc/rfc9457)) with header `typ: symlic+cose` for constrained and embedded memory-limited environments.

**LIC-47.** The `symlic+cose` profile MUST carry an **identical set of claims** to `symlic+jws`. Transcoding between profiles MUST be strictly mechanical and lossless.

**LIC-48.** COSE algorithms for ML-DSA MUST follow RFC 9964 (`-48` / `-49` / `-50`) and for ECDSA RFC 9053 (`-7` for ES256).

### 3.8.1 Mapping Claims to CBOR Integer Keys

To minimize binary footprint on microcontrollers and embedded targets, text claim names map to standard CWT ([RFC 8392](https://www.rfc-editor.org/rfc/rfc8392)) and proprietary Symbolon integer keys:

| Scope | JSON Claim | CBOR Key | Value Type | Standard Reference |
|---|---|---|---|---|
| Root CWT | `iss` | `1` | text string | RFC 8392 |
| Root CWT | `sub` | `2` | text string | RFC 8392 |
| Root CWT | `aud` | `3` | text string | RFC 8392 |
| Root CWT | `exp` | `4` | unsigned integer (NumericDate) | RFC 8392 |
| Root CWT | `nbf` | `5` | unsigned integer (NumericDate) | RFC 8392 |
| Root CWT | `iat` | `6` | unsigned integer (NumericDate) | RFC 8392 |
| Root CWT | `jti` | `7` | byte string / text string | RFC 8392 |
| Root CWT | `symlic` | `-65700` | map | Symbolon |
| `symlic` | `v` | `1` | unsigned integer | Symbolon |
| `symlic` | `profile` | `2` | unsigned int (`1`: classical, `2`: hybrid, `3`: pqc) | Symbolon |
| `symlic` | `requiredAlgs` | `3` | array of int (`-7`: ES256, `-49`: ML-DSA-65) | RFC 9964 / 9053 |
| `symlic` | `license` | `4` | map (`1`: key, `2`: model, `3`: state, `4`: issuedAt, `5`: customer) | Symbolon |
| `symlic` | `limits` | `5` | map (`1`: maxSeats, `2`: seatUnit, `3`: overageStrategy, `4`: maxRelays) | Symbolon |
| `symlic` | `entitlements` | `6` | array of maps (`1`: code, `2`: value, `3`: period) | Symbolon |
| `symlic` | `binding` | `7` | map (`1`: fingerprint, `2`: matching, `3`: components) | Symbolon |
| `symlic` | `policy` | `8` | map (`1`: lease, `2`: clockSkewTolerance, `3`: revocation) | Symbolon |

---

> [← License Key](02-license-key.md) · [Table of Contents](README.md) · [Lease Token →](04-lease-token.md)

