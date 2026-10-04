# 4. Lease Token

> [← `symlic/1`](03-symlic-1.md) · [Table of Contents](README.md) · [Seat Grant →](05-seat-grant.md)

The Lease Token is a **short-lived in-memory credential** certifying active concurrent seat allocation. It is minted by a relay or control plane during checkout and refreshed with each heartbeat. It is never stored on disk.

---

## 4.1 Format

**LSE-1.** The Lease Token MUST be a JWS using **Compact Serialization** ([RFC 7515](https://www.rfc-editor.org/rfc/rfc7515) §7.1).

**LSE-2.** The `typ` header parameter MUST be `symlease+jwt`.

**LSE-3.** The `alg` header parameter MUST be `ES256`. Implementations MAY support `ML-DSA-44` under the `pq-only-v1` profile.

```jsonc
// Protected Header
{ "alg": "ES256", "kid": "lease-2026-09", "typ": "symlease+jwt" }

// Payload
{
  "iss": "relay:rly_01JQ…",
  "sub": "lic_01JQ8ZK4N9V2X6M0",
  "jti": "lse_01JQ9A…",
  "iat": 1788480000,
  "exp": 1788480600,
  "seat": 7,
  "fp":  "sha256:9f2c…",
  "ent": ["core", "module.cad-export"],
  "gnt": "gnt_01JQ…",
  "seq": 1043
}
```

---

## 4.2 Claims Schema

| Claim | Requirement | Semantic Meaning |
|---|---|---|
| `iss` | MUST | Minting node: `relay:{relayId}` or control plane URI |
| `sub` | MUST | Target License ID (`lic_...`) |
| `jti` | MUST | Unique Lease ID (`lse_...`) |
| `iat` | MUST | Issuance timestamp (UTC seconds) |
| `exp` | MUST | Expiration timestamp (typically `iat + 10 min`) |
| `seat` | MUST | Assigned seat index (1-based integer) |
| `fp` | MUST | Canonical SHA-256 hash of client machine hardware components |
| `ent` | MUST | Array of granted entitlement codes |
| `gnt` | MUST | ID of authorizing Seat Grant (`gnt_...`) |
| `seq` | MUST | Monotonically incrementing heartbeat sequence number |

---

## 4.3 Client Verification

**LSE-4.** The client SDK MUST verify:
1. Compact JWS signature using the issuing node's public key.
2. `now < exp + clockSkewTolerance`.
3. `fp` matches the local machine's canonical hardware fingerprint.
4. `seq` is strictly greater than the previously recorded sequence number.
