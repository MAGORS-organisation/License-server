# 8. Fingerprint & Node-Locking

> [← Floating Protocol](07-floating-protocol.md) · [Table of Contents](README.md)

The fingerprint is the hardware identifier of the machine to which a node-locked license or lease is bound. This chapter defines its components, matching algorithms, and — most importantly — when hardware identifiers **must not** be used.

---

## 8.1 Components

**FPR-1.** A fingerprint consists of named components. Each component is OPTIONAL; client SDKs collect whichever components are accessible on the host operating system.

| Code | Windows | Linux | macOS |
|---|---|---|---|
| `machineId` | `HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid` | `/etc/machine-id` | `IOPlatformUUID` |
| `board` | `Win32_BaseBoard SerialNumber` | DMI `board_serial` | — |
| `cpu` | CPUID vendor + model string | `/proc/cpuinfo` | `sysctl machdep.cpu` |
| `disk` | System drive volume serial | `/dev/disk/by-id` | IOKit BSD storage serial |
| `mac` | First non-virtual physical MAC address | `/sys/class/net/*/address` | `ifconfig` hardware address |
| `host` | Hostname (**pseudonymized**) | | |

**FPR-2.** The `host` component MUST NOT be transmitted or stored in plaintext. It MUST be pseudonymized by hashing with a license-specific salt.

**FPR-3.** An unavailable component MUST be omitted. An implementation MUST NOT substitute placeholder values (such as empty strings, `"unknown"`, or all zeros).

> **Non-Normative Rationale.** Placeholders are worse than omitted components: under `match-most`, two distinct machines with identical fallback `"unknown"` motherboard serials would appear more identical than they actually are.

**FPR-4.** The canonical summary hash of a fingerprint MUST follow the format `{algo}:{hex}`, where `algo` is `sha256`. Components enter the hash sorted lexicographically by code key to produce deterministic digests.

---

## 8.2 Matching Strategies

**FPR-5.** Implementations MUST support the following matching strategies:

| Strategy | Match Condition |
|---|---|
| `match-any` | At least **one** component matches |
| `match-two` | At least **two** components match |
| `match-most` | A **majority** of present components match (**default**) |
| `match-all` | **All** present components match |

**FPR-6.** The default matching strategy MUST be `match-most`.

**FPR-7.** In `match-most` and `match-all`, component counts evaluate only over components present **in both** compared fingerprints.

**FPR-8.** If the count of mutually present components is less than 2, `match-most` MUST behave identically to `match-all`.

> **Non-Normative Rationale.** Without `FPR-8`, a machine sharing only a single mutual component would trivially pass `match-most` — because "majority of one" is one.

**FPR-9.** The matching strategy is declared within the signed license file (`symlic.binding.matching`). The client SDK MUST NOT override it through local configuration.

---

## 8.3 Containers, Virtual Machines, and Autoscaling

**Hardware fingerprinting inside a container is an anti-pattern.** Golden container images propagate identical `MachineGuid` and `/etc/machine-id` values to hundreds of running instances; cloned VMs duplicate physical attributes.

**FPR-10.** The SDK MUST detect containerized and virtualized cloud environments before collecting hardware components.

**FPR-11.** Detection MUST check at minimum: presence of `/.dockerenv`, `/proc/1/cgroup` content, the `KUBERNETES_SERVICE_HOST` environment variable, and reachability of standard cloud metadata endpoints.

**FPR-12.** If an environment is detected as a container or cloud instance, the SDK **MUST NOT** use a hardware fingerprint. It MUST instead use:

1. A persisted random UUID stored in a mounted persistent volume, or
2. The cloud provider instance-id.

```
if (IsContainerized() || IsCloudInstance())
    → fingerprint = persisted random UUID in storage volume
      (or cloud instance-id), NEVER raw hardware
    → log recommendation for floating license with short TTL
```

**FPR-13.** In such environments, the SDK SHOULD log an operational recommendation to utilize **floating leases with short TTL** rather than node-locking.

**FPR-14.** This container behavior MUST be clearly documented in public user guides.

> **Non-Normative Rationale.** Hardware binding inside containers is the single largest generator of customer support tickets across the license management industry. `FPR-14` is essential to prevent costly operational mistakes.

---

## 8.4 Activation and Deactivation

**FPR-15.** Node-lock machine activation is performed via `POST /v1/activations`, and deactivation via `DELETE /v1/activations/{id}`.

**FPR-16.** The issuer MUST enforce `machineUniqueness` from license policy — determining whether the same machine fingerprint may be bound to multiple distinct licenses.

**FPR-17.** A fingerprint mismatch during node-locked validation MUST produce an HTTP `403` response with problem type `fingerprint-mismatch` conforming to [`FLT-29`](07-floating-protocol.md#761-error-handling).

---

## 8.5 Privacy and Data Protection

**FPR-18.** Machine fingerprint components constitute device telemetry belonging to the customer's infrastructure and personnel. The issuer MUST NOT transmit raw components outside the customer boundary; only the **canonical summary hash** per `FPR-4` is transmitted to central systems.

**FPR-19.** Raw components MAY be cached locally by an on-premise relay solely to execute `matching` logic. The relay MUST NOT include raw hardware components in upstream telemetry reports ([`FLT-36`](07-floating-protocol.md#78-usage-telemetry-reporting)).

---

> [← Floating Protocol](07-floating-protocol.md) · [Table of Contents](README.md)
