# 6. Revocation List (`.symrl`)

> [← Seat Grant](05-seat-grant.md) · [Table of Contents](README.md) · [Floating Protocol →](07-floating-protocol.md)

The Revocation List is the sole mechanism to invalidate a license, a machine, or a **compromised signing key** in the field without releasing a new binary version.

---

## 6.1 Format

**RVL-1.** The Revocation List MUST be a JWS in General JSON Serialization with `typ: symrl+jws`.

**RVL-2.** The list MUST be signed with the hybrid scheme (`ES256` + `ML-DSA-65`).

**RVL-3.** `exp` MUST be short. 24 hours is RECOMMENDED.

```jsonc
{
  "iss": "https://licenses.acme.example",
  "iat": 1788480000, "exp": 1788566400,
  "symrl": {
    "v": 1, "seq": 1187,
    "full": false, "since": 1186,
    "revoked": [
      { "t": "license", "id": "lic_01JQ…", "at": 1788470000, "reason": "non-payment" },
      { "t": "machine", "id": "mch_01JQ…", "at": 1788471000, "reason": "fraud" },
      { "t": "kid",     "id": "prd-acme-2025-ec", "at": 1788400000, "reason": "key-compromise" }
    ]
  }
}
```

---

## 6.2 Fields

| Field | Requirement | Semantic Meaning |
|---|---|---|
| `seq` | MUST | Monotonically increasing sequence number of the list |
| `full` | MUST | `true` = complete list, `false` = delta |
| `since` | MUST*, if `full: false` | `seq` from which this delta builds upon |
| `revoked` | MUST | Array of revoked entries; MAY be empty |

**RVL-4.** Each entry MUST contain `t` (entity type), `id`, and `at` (revocation timestamp). `reason` is OPTIONAL.

**RVL-5.** `t` MUST be one of: `license`, `machine`, `kid`, `relay`, `lease`.

**RVL-6.** An unknown `t` value MUST be **ignored** by the verifier, not cause rejection of the entire list.

> **Non-Normative Rationale.** Otherwise, adding a new revoked entity type in the future would block revocation processing on all older deployed clients — precisely in a scenario where timely revocation is critical.

---

## 6.3 Processing

**RVL-7.** A verifier MUST persist `last_seen_seq` and MUST reject any list with `seq` lower than the stored value.

**RVL-8.** If `full` is `false`, the verifier MUST verify that `since` equals its `last_seen_seq`. In case of a sequence gap, it MUST request a full list (`full: true`).

**RVL-9.** Revocation is **permanent**. An entity that was once present in a revocation list MUST be treated as revoked even if omitted from subsequent deltas.

**RVL-10.** A verifier SHOULD NOT trust a list older than `symlic.policy.revocation.maxAge` specified in the license file (default `P7D`).

**RVL-11.** If a verifier has no revocation list or only a stale list, it MUST behave according to its configured verification strictness — adhering to the same scale as `LIC-25`. The default `auto` mode proceeds with operation and logs `stale-revocation-list`.

> **Non-Normative Rationale.** Hard failure on a missing or stale revocation list would convert a temporary network disruption into a catastrophic customer production outage. That is a far worse outcome than a brief window during which a revoked license remains operational.

---

## 6.4 Signing Key Revocation

**RVL-12.** A revocation entry with `t: "kid"` MUST lead to rejection of **all** artifacts signed with the specified `kid`, even if their cryptographic signatures are valid and they have not expired.

**RVL-13.** Upon revoking a `kid`, the issuer MUST publish a new revocation list immediately, rather than waiting for the standard periodic publication window.

**RVL-14.** A verifier MUST NOT apply `kid` revocation to the revocation list itself if doing so would prevent the list's verification. A revocation list signed by a revoked `kid` MUST be rejected.

> **Non-Normative Rationale.** `RVL-12` is the reason why `maxAge` must remain short. If `maxAge` spanned months, revoking a compromised key would be purely theoretical — deployed fleets would only learn of the compromise long after an attacker had forged arbitrary valid licenses.

---

## 6.5 Distribution

**RVL-15.** The revocation list MUST be accessible at `GET /v1/revocations/latest?since={seq}`.

**RVL-16.** The server MUST return a delta if `since` corresponds to a known sequence; otherwise it MUST return a full list.

**RVL-17.** A relay MUST distribute the revocation list to connected clients and MUST include it in air-gapped synchronization bundles conforming to [07-floating-protocol.md](07-floating-protocol.md#77-air-gapped-flow).

---

> [← Seat Grant](05-seat-grant.md) · [Table of Contents](README.md) · [Floating Protocol →](07-floating-protocol.md)
