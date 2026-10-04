# 5. Seat Grant (`.symgrant`)

> [← Lease Token](04-lease-token.md) · [Table of Contents](README.md) · [Revocation List →](06-revocation-list.md)

A Seat Grant is an issuer's signed assertion certifying:

> *"Relay R is authorized to issue up to N concurrent seats for license L during window [t0, t1], sequence S."*

This delegated capacity model enables on-premise relays to operate in **100% air-gapped isolation** without maintaining WAN connections to the central control plane and without requiring distributed consensus between relays.

---

## 5.1 Format

**GNT-1.** The Seat Grant MUST be a JWS using General JSON Serialization with the same header structure as the [License File](03-symlic-1.md#32-jws-structure), with `typ: symgrant+jws`.

**GNT-2.** The grant MUST be signed with the hybrid scheme (`ES256` + `ML-DSA-65`), conforming to License File rules.

```jsonc
{
  "iss": "https://licenses.acme.example",
  "sub": "lic_01JQ8ZK4N9V2X6M0",
  "aud": "rly_01JQ8ZM2…",
  "jti": "gnt_01JQ8ZN7…",
  "iat": 1788480000, "nbf": 1788480000, "exp": 1788566400,   // 24 h
  "symgrant": {
    "v": 1,
    "seats": 10,
    "seatRange": [0, 9],
    "seq": 42,
    "supersedes": 41,
    "entitlements": ["core", "module.cad-export"],
    "leasePolicy": { "ttl": "PT10M", "graceTtl": "PT4H" },
    "leaseKey": { "kty": "EC", "crv": "P-256", "x": "…", "y": "…", "kid": "lease-rly1-2026-09" },
    "offlineExtension": { "allowed": true, "maxExtensions": 7 }
  }
}
```

---

## 5.2 Fields

| Field | Requirement | Semantic Meaning |
|---|---|---|
| `aud` | MUST | ID of the relay for which the grant is issued |
| `seats` | MUST | Number of seats delegated to this relay |
| `seatRange` | MUST | Inclusive interval `[from, to]` of seat indices |
| `seq` | MUST | Monotonically increasing sequence number per *(license, relay)* pair |
| `supersedes` | SHOULD | `seq` of the grant that this grant replaces |
| `entitlements` | MUST | Array of entitlement codes the relay is permitted to include in lease tokens |
| `leasePolicy` | MUST | `ttl` and `graceTtl` parameters for issued leases |
| `leaseKey` | MUST | Public **and private** key material used to sign lease tokens |
| `offlineExtension` | OPTIONAL | Permission and limit for autonomous offline extensions |

**GNT-3.** `seatRange` MUST contain exactly `seats` count, i.e., `seatRange[1] - seatRange[0] + 1 == seats`.

---

## 5.3 Disjointness — The Main Invariant

**GNT-4.** The issuer MUST ensure that the sum of `seats` across all **currently valid** grants for a given license does not exceed `limits.maxSeats` specified in its [License File](03-symlic-1.md).

**GNT-5.** The `seatRange` intervals of any two concurrently valid grants for the same license **MUST NOT overlap**.

> **Non-Normative Rationale.** `GNT-4` and `GNT-5` are the architectural reason why the system requires neither quorum nor communication between relays. Two relays handling 12 seats each out of a 25-seat pool operate completely independently; if one crashes, the other continues serving its 12 seats uninterrupted. An explicit `seatRange` (instead of just seat count) makes capacity overlap instantly detectable during audits.

**GNT-6.** A grant is valid if `nbf <= now <= exp`, its signatures verify cryptographically, its `seq` is the highest seen for the given *(license, relay)* pair, and it is not revoked.

---

## 5.4 Sequence and Replay Protection

**GNT-7.** The relay MUST persist `last_seq` for each license and MUST reject any grant with `seq` less than or equal to `last_seq`.

**GNT-8.** The issuer MUST persist `last_seq` for each *(license, relay)* pair and MUST increment it upon issuing each new grant.

**GNT-9.** If a grant contains `supersedes`, the relay MUST immediately cease using the grant with the specified `seq`, even if it has not yet expired.

---

## 5.5 Relay Behavior

**GNT-10.** A relay MUST NOT issue a lease token for any seat number outside the `seatRange` of its valid grant.

**GNT-11.** A relay MUST NOT hold more concurrently active leases than `seats`.

**GNT-12.** When a grant expires (`now > exp`), the relay MUST stop issuing **new** leases. Existing leases MUST be allowed to continue through their `graceTtl`.

> **Non-Normative Rationale.** This provides graceful degradation: a network outage to the control plane will not disrupt ongoing user work, while strictly preventing capacity expansion beyond authorized limits.

**GNT-13.** `leaseKey` contains private cryptographic material. The relay MUST store it encrypted at rest and MUST NOT write it to logs or export it via API endpoints.

**GNT-14.** If `offlineExtension.allowed` is `true`, the relay MAY extend the validity of the grant at most `maxExtensions` times by the original duration window (`exp - nbf`), without requiring contact with the control plane.

**GNT-15.** The relay MUST persist the count of executed offline extensions and MUST report it in the next usage telemetry payload.

---

## 5.6 Deployment Topologies

Non-normative overview of supported operational topologies:

| # | Topology | Description |
|---|---|---|
| T1 | Control plane only | No relays deployed; clients call `/v1` directly |
| T2 | Control plane + 1 relay | Single relay holds a grant for the full `maxSeats` pool |
| T3 | Control plane + N relays with disjoint grants | High availability and/or geographical segmentation across customer sites |
| T4 | Standalone air-gapped relay | No WAN access; long-lived grant imported via secure USB exchange |
| T5 | Offline license files only | No floating concurrency; pure node-locked validation |

**Trade-off for T3:** Seats **cannot** be dynamically reallocated between relays without contacting the control plane. This is mitigated in practice by using shorter grant intervals with automatic continuous rebalancing.

---

> [← Lease Token](04-lease-token.md) · [Table of Contents](README.md) · [Revocation List →](06-revocation-list.md)
