# Symbolon Normative Conformance Test Vectors

This directory contains the normative test vectors for the **Symbolon License File Specification (`symlic/1`)**, as mandated by [spec/README.md](../spec/README.md#testovacie-vektory) and [spec/03-symlic-1.md](../spec/03-symlic-1.md).

These test vectors provide reference artifacts and deterministic test cases to ensure that third-party implementations and client SDKs (in .NET, Go, Java, Python, Rust, C/C++, and WASM) conform strictly to the security, signature, and validation semantics of Symbolon.

---

## Vector Schema

Each vector is stored in JSON format with the following structure:

```json
{
  "VectorId": "string (unique test case identifier)",
  "Requirement": "string (normative spec clauses tested, e.g. LIC-21, LIC-23)",
  "Description": "string (human-readable explanation of test scenario and security properties)",
  "Keys": [
    {
      "Kid": "string (Key Identifier)",
      "Alg": "string (JWS algorithm: ES256, ML-DSA-65, etc.)",
      "PublicJwk": {
        "kty": "EC or OKP or AKY",
        "alg": "...",
        "kid": "...",
        ...
      }
    }
  ],
  "DocumentPem": "string (PEM-armored or bare JWS General JSON license file)",
  "HighWaterMarkIat": "integer or null (optional high-water mark timestamp for replay defense)",
  "RevokedKids": ["array of revoked key identifiers or null"],
  "Expected": {
    "IsValid": "boolean (true if document passes verification, false if rejected)",
    "FailureReason": "string or null (exact normative error string if verification fails)",
    "VerifiedAlgs": ["array of successfully verified algorithms, or null"]
  }
}
```

---

## Vector Catalog

| Vector ID | Normative Clause | Scenario | Expected Verdict |
|---|---|---|---|
| `lic-21-valid-hybrid.json` | `LIC-21`, `LIC-22` | Valid hybrid license signed with both classical **ES256** and post-quantum **ML-DSA-65** (FIPS 204). | `IsValid: true`, `VerifiedAlgs: ["ES256", "ML-DSA-65"]` |
| `lic-23-downgrade-attack.json` | `LIC-23` | **Downgrade Attack Defense**: An attacker strips the post-quantum ML-DSA-65 signature, leaving only the classical signature when `requiredAlgs` mandates both. | `IsValid: false`, `FailureReason: "stripped-signature:ML-DSA-65"` |
| `lic-25-unknown-alg.json` | `LIC-25` | Document specifies an unknown/unsupported algorithm in `requiredAlgs` for a long-lived license (> 1 year) evaluated under `Strictness.Auto`. | `IsValid: false`, `FailureReason: "unsupported-required-alg:FALCON-512-UNKNOWN"` |
| `lic-15-version-mismatch.json` | `LIC-15` | Protected header `symlic` claim differs from payload `symlic.v` claim (e.g. `2` vs `1`). | `IsValid: false`, `FailureReason: "symlic-version-mismatch"` |
| `lic-30-clock-rollback.json` | `LIC-30` | **Monotonic Time & Replay Defense**: Document `iat` is older than the client's monotonic high-water mark. | `IsValid: false`, `FailureReason: "replayed-older-document"` |
| `lic-31-revoked-kid.json` | `LIC-31` | Valid cryptographic signature, but the signing key `kid` is listed as revoked in the key ring. | `IsValid: false`, `FailureReason: "revoked-kid:prd-acme-2026-ec"` |

---

## Running Verification

All vectors are automatically validated during continuous integration by `Symbolon.Format.Tests.VectorConformanceTests`:

```bash
dotnet test tests/Symbolon.Format.Tests/Symbolon.Format.Tests.csproj --filter "FullyQualifiedName~VectorConformanceTests"
```
