# Security Policy & Vulnerability Disclosure

## 1. Our Commitment to Security

The **Symbolon** project develops enterprise-grade cryptographic licensing software designed to safeguard intellectual property and revenue for software publishers worldwide. Security and verifiable integrity are foundational pillars of the system.

In compliance with the **EU Cyber Resilience Act (Regulation (EU) 2024/2847 - CRA)** and industry best practices, we maintain a coordinated vulnerability disclosure policy with a structured remediation process.

---

## 2. Supported Versions

Security updates and patches are actively provided for the following releases:

| Version | Supported | Minimum Security Support End Date |
| :--- | :--- | :--- |
| `1.x` (LTS / Current) | :white_check_mark: | November 2028 (.NET 10 LTS lifecycle) |
| `< 1.0` | :x: | Discontinued |

---

## 3. Reporting a Vulnerability

If you discover a security vulnerability or weakness in Symbolon, please report it privately:

- **Primary Contact**: Open a confidential security advisory directly on GitHub via [Security Advisories](https://github.com/MAGORS-organisation/License-server/security/advisories/new).
- **Email**: `security@symbolon.dev` (or the repository security team).

**Please do NOT disclose vulnerabilities via public GitHub issues, discussions, or pull requests.**

### What to include in your report:
1. Clear description of the vulnerability, including affected components (e.g. `Symbolon.Crypto`, `Symbolon.Relay`, `Symbolon.ControlPlane`).
2. Step-by-step reproduction instructions or a minimal Proof of Concept (PoC).
3. Impact assessment (e.g., key compromise, lease tampering, seat overage, unauthorized privilege escalation).
4. Any potential mitigations or suggested fixes.

---

## 4. Remediation SLA & 90-Day Disclosure Window

- **Initial Acknowledgement**: Within **48 hours**.
- **Triage & Reproduction**: Within **5 business days**.
- **Remediation & Fix Target**:
  - **Critical / High (CRA actively exploited)**: Within **24 to 72 hours** after confirmation, with notification submitted to the relevant CSIRTs / ENISA as mandated by the Cyber Resilience Act.
  - **Medium / Low**: Within **30 calendar days**.
- **Coordinated Public Disclosure**: We adhere to a **90-day coordinated disclosure policy**. Public release notes and CVE identifiers are published simultaneously with the patched release.

---

## 5. Software Supply Chain & CRA Compliance

To guarantee supply chain integrity, every release of Symbolon provides:
1. **Machine-readable Software Bill of Materials (SBOM)** generated in the **CycloneDX v1.6 (JSON)** standard, available via `/v1/compliance/sbom` and attached to release assets.
2. **Cryptographic Signatures & Checksums**: SHA-256 / SHA-512 hashes and Sigstore cosign signatures for OCI container images and binaries.
3. **Verifiable Transparency Log**: RFC 6962 compliant Merkle Tree inclusion proofs available via `/v1/transparency/root` and `/v1/transparency/inclusion/{id}`.
4. **Deterministic & Reproducible Builds**: All .NET artifacts are built deterministically with pinned dependencies and source-linked symbols.
