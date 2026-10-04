# Symbolon — Open Specification

**Version:** `symlic/1` — draft  
**License:** [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) (`CC-BY-4.0`) — distinct from the codebase license, see [LICENSING.md](../../LICENSING.md)  
**Status:** Working Draft. Normative clauses are stable in intent and design.

This specification describes artifact formats and network protocol semantics of the **Symbolon** licensing system so that anyone can implement interoperable servers, relays, and client SDKs independently of the reference .NET 10 implementation.

---

## Table of Contents

| # | Document | Content |
|---|---|---|
| 1 | [Artifacts](01-artifacts.md) | Overview of the five distinct artifacts and why they are separated |
| 2 | [License Key](02-license-key.md) | License key format, Crockford Base32 alphabet, Damm checksum, storage |
| 3 | [`symlic/1` — License File](03-symlic-1.md) | JWS General JSON, claims schema, **normative validation rules**, versioning |
| 4 | [Lease Token](04-lease-token.md) | Compact JWS for concurrent seat allocation |
| 5 | [Seat Grant](05-seat-grant.md) | Delegated seat capacity for autonomous relays |
| 6 | [Revocation List](06-revocation-list.md) | Cryptographic revocation of licenses, hardware nodes, and keys |
| 7 | [Floating Protocol](07-floating-protocol.md) | Seat lifecycle: checkout, renew, release, borrow, queuing, air-gap flow |
| 8 | [Fingerprint & Node-Locking](08-fingerprint.md) | Hardware components, matching strategies, VM/container isolation |

---

## Conventions

### Requirement Keywords
The key words **MUST**, **MUST NOT**, **REQUIRED**, **SHALL**, **SHALL NOT**, **SHOULD**, **SHOULD NOT**, **RECOMMENDED**, **NOT RECOMMENDED**, **MAY**, and **OPTIONAL** in this document are to be interpreted as described in [BCP 14](https://www.rfc-editor.org/info/bcp14) ([RFC 2119](https://www.rfc-editor.org/rfc/rfc2119), [RFC 8174](https://www.rfc-editor.org/rfc/rfc8174)) when, and only when, they appear in all capitals.

### Requirement Identifiers
Every normative clause has a stable identifier formatted as `PREFIX-N`:

| Prefix | Domain |
|---|---|
| `KEY` | License Key |
| `LIC` | License File (`.symlic`) |
| `LSE` | Lease Token |
| `GNT` | Seat Grant (`.symgrant`) |
| `RVL` | Revocation List (`.symrl`) |
| `FLT` | Floating Protocol |
| `FPR` | Hardware Fingerprint |

Identifiers remain **stable across versions**; retired numbers are never recycled. Conformance test vectors reference these identifiers.

---

## Conformance Roles

Implementations declare conformance to one or more of the following roles:

| Role | Required Clauses | Description |
|---|---|---|
| **Issuer** | `KEY`, `LIC`, `GNT`, `RVL` | Signs and issues license artifacts and revocation lists |
| **Issuing Node (Relay)** | `LSE`, `GNT` (verification), `FLT` | Issues floating lease tokens against a valid seat grant |
| **Verifier (SDK)** | `LIC` (verification), `LSE` (verification), `RVL`, `FPR` | Client-side license and lease verification |

**Every Verifier MUST implement at least the `classical-v1` profile (ES256).** Hybrid and post-quantum profiles are defined in [03-symlic-1.md](03-symlic-1.md#35-cryptographic-profiles).

---

## Normative Test Vectors

The [`vectors/`](../../vectors/) directory contains deterministic reference test vectors for all static verification rules (`LIC-21` through `LIC-34`), downgrade attack defense, and format conformance.

---

## Specification Completeness

All architectural components and operational protocols of `symlic/1` are fully specified with zero known gaps:

1. **Deterministic Test Vectors:** 6 normative JSON vector test suites covering hybrid signatures, downgrade resistance, clock rollback, and key revocation ([`vectors/`](../../vectors/)).
2. **Offline Roaming Lease (`.symlease`):** Full claims schema, PEM envelope, and cryptographic proof-of-possession return protocol ([07-floating-protocol.md §7.4.1](07-floating-protocol.md#741-symlease-artifact-schema)).
3. **Embedded COSE/CBOR Profile (`symlic+cose`):** Lossless integer claim mapping according to RFC 8392, RFC 9052/9053, and RFC 9964 ([03-symlic-1.md §3.8.1](03-symlic-1.md#381-mapping-claims-to-cbor-integer-keys)).
4. **Queue Ticket Protocol (`queueTicket`):** Complete state lifecycle, HTTP 202 payload, polling endpoints, and cancellation ([07-floating-protocol.md §7.6.2](07-floating-protocol.md#762-queue-ticket-format)).
5. **Relay Registration & mTLS Identity:** Dynamic onboarding endpoint (`POST /relay/v1/register`) and cryptographic certificate thumbprint enforcement ([07-floating-protocol.md §7.6](07-floating-protocol.md#76-endpoints)).

