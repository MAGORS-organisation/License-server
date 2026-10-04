# 2. License Key

> [← Artifacts](01-artifacts.md) · [Table of Contents](README.md) · [`symlic/1` →](03-symlic-1.md)

The License Key is an **opaque identifier**. It contains no payload data and cannot be verified offline without a license file or contact with an authorization server.

---

## 2.1 Format

```
SYM-XXXXX-XXXXX-XXXXX-XXXXX-CCCCC
    └───── 20 chars = 100 bits ───────┘└─ CRC-32C(20 chars)
                                          truncated to 25 bits
```

**KEY-1.** The key MUST follow the format `{PREFIX}-{G1}-{G2}-{G3}-{G4}-{G5}`, where `G1`–`G5` are 5-character groups encoded in the alphabet defined by `KEY-4`.

**KEY-2.** Groups `G1`–`G4` (20 characters) MUST carry **100 bits** of entropy from a cryptographically secure pseudorandom number generator (CSPRNG).

**KEY-3.** Group `G5` (5 characters) MUST carry a **CRC-32C** (Castagnoli) checksum computed over the 20 characters of `G1`–`G4` in their normalized form (`KEY-6`), truncated to the 25 most significant bits and encoded into 5 Crockford Base32 characters.

**KEY-4.** The alphabet MUST be **Crockford Base32**: `0123456789ABCDEFGHJKMNPQRSTVWXYZ`.

**KEY-5.** The prefix MUST be configurable at the issuer/tenant level (default `SYM`). The prefix MUST contain 2–8 characters from the alphabet defined by `KEY-4`.

**Example:**
```
SYM-4K7QT-9M2XA-B8ZH3-P6RWY-C1J8N
```

---

## 2.2 Input Normalization

To ensure smooth manual entry without user frustration, parsers MUST normalize inputs before checksum validation:

**KEY-6.** Prior to validation, the parser MUST apply the following deterministic normalization pipeline:
1. Strip leading and trailing whitespace.
2. Convert all characters to uppercase.
3. Map confusing lookalike characters:
   - `O` (letter) and `o` → `0` (zero)
   - `I` (letter), `i`, `L`, `l` → `1` (one)
4. Remove internal hyphens (`-`) and spaces.

**KEY-7.** Checksum verification MUST reject any key where CRC-32C evaluation does not match group `G5`.

---

## 2.3 Storage and Privacy

**KEY-8.** A license server MUST NEVER store license keys in plaintext in logs or public audit ledgers.

**KEY-9.** Database storage MUST store:
- A cryptographic hash (SHA-256) of the normalized key for exact matching.
- A masked representation for human administration (e.g. `SYM-4K7QT-*****-*****-*****-C1J8N`).
