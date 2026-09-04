# 5. Formát licencie — návrh variantov a rozhodnutie

> Časť zadania **Symbolon — licenčný server na .NET 10**. Späť na [obsah](README.md) · [prehľad projektu](../README.md).

Toto je najdôležitejšia časť dokumentu, lebo formát je jediná vec, ktorú **nemôžeš neskôr zmeniť** — je zapečený v nasadenom softvéri zákazníkov.

## 5.0 Kľúčový poznatok: štyri artefakty, nie jeden

Väčšina domácich riešení mieša dve nezlučiteľné požiadavky do jedného reťazca: *„musí sa dať prepísať z papiera"* a *„musí niesť podpísané dáta"*. S post-kvantovými podpismi je to definitívne nemožné — ML-DSA-65 podpis má **3 309 bajtov**, čo je ~5 300 znakov v Base32. Preto:

| Artefakt | Prípona | Nesie | Životnosť | Podpis |
|---|---|---|---|---|
| **License Key** | — | *nič* (je to len identifikátor) | trvalá | žiadny |
| **License File** | `.symlic` | entitlements, limity, viazanie | 30 d – 20 r | hybrid ES256 + ML-DSA-65 |
| **Lease Token** | — (in-memory) | sedadlo, exp, fingerprint hash | 30 s – 10 min | ES256 (voliteľne + ML-DSA-44) |
| **Seat Grant** | `.symgrant` | delegovaná kapacita relayu | hodiny – dni | hybrid |
| **Revocation List** | `.symrl` | zoznam revokovaných | krátke TTL | hybrid |

Toto rozdelenie je samo osebe architektonické rozhodnutie a rieši 80 % problémov, ktoré ostatné produkty riešia kompromismi.

## 5.1 Artefakt A — License Key

**Varianty:**

| # | Návrh | Za | Proti | Verdikt |
|---|---|---|---|---|
| A1 | **Nepriehľadný náhodný identifikátor.** 128 bitov CSPRNG → Crockford Base32, 5 skupín po 5 znakov + 2 kontrolné znaky (CRC-32C). `SYM-4K7QT-9M2XA-B8ZH3-P6RWY-C1J8N` | Konštantná dĺžka nezávislá od krypto; Crockford odstraňuje zámenu `I/L/1`, `O/0`; offline kontrola preklepu; server ho hashuje | Nedá sa validovať bez servera/licenčného súboru | ✅ **Vybrané** |
| A2 | **Partial key verification** (klasický produkt key so zabudovaným čiastočným podpisom) | Historicky populárne | Kryptograficky mŕtve — jeden keygen zrušuje celý produktový rad | ❌ |
| A3 | **Podpísaný kompaktný kľúč** (payload + ES256 podpis v kľúči, ~140 znakov) | Offline overiteľný bez ďalšieho súboru | Neprepisovateľné; s PQC úplne nemožné; verejný kľúč nerotovateľný | ❌ |

**Špecifikácia A1:**

```
SYM-XXXXX-XXXXX-XXXXX-XXXXX-XXCCC
    └────── 100 bitov entropie ──────┘└ CRC-32C(20 znakov), 25 bitov
```
- Abeceda: Crockford Base32 (`0123456789ABCDEFGHJKMNPQRSTVWXYZ`), vstup case-insensitive, normalizácia `I,L→1`, `O→0`.
- Prefix `SYM-` je konfigurovateľný na tenant (napr. `ACME-`), aby vedel používateľ na prvý pohľad, kam kľúč patrí.
- **Uloženie na serveri:** nikdy plaintext. `key_hash = Argon2id(key, tenant_pepper)` + `key_lookup = HMAC-SHA256(pepper, key)[0..8]` ako indexovateľný stĺpec. Dump databázy nedáva použiteľné kľúče.

## 5.2 Artefakt B — License File (`.symlic`)

### 5.2.1 Zvažované varianty

| # | Formát | Veľkosť* | Multi-podpis | PQC cesta | .NET podpora | Čitateľnosť | Verdikt |
|---|---|---|---|---|---|---|---|
| **B1** | **JWS General JSON Serialization** (RFC 7515 §7.2), alg podľa **RFC 9964** | ~5,4 kB | ✅ natívne (pole `signatures`) | ✅ štandardizovaná (`ML-DSA-44/65/87`, `kty: AKP`) | Podpis ručne nad `MLDsa`/`ECDsa` (žiadna knižnica nutná) | Vysoká (JSON) | ✅ **Vybrané** |
| **B2** | **COSE_Sign** (RFC 9052) / CBOR, COSE alg `-49` | ~3,5 kB | ✅ natívne | ✅ RFC 9964 pokrýva aj COSE | ✅ .NET 10 pridal `CoseKey` s konštruktorom `CoseKey(MLDsa)` → `CoseSigner` funguje aj s ML-DSA | Nízka (binárne) | ⭕ **Voliteľný profil** pre embedded |
| B3 | **PASETO v4.public** | ~0,3 kB | ❌ | ❌ (viazané na Ed25519) | Knižnica tretej strany | Stredná | ❌ |
| B4 | Vlastný binárny TLV | ~3,4 kB | vlastné | vlastné | vlastné | Žiadna | ❌ |
| B5 | **CMS SignedData / X.509 attribute cert** | ~6 kB | ✅ | ⚠️ `SignedCms` bez ML-DSA | Áno pre klasiku | Nízka | ❌ |
| B6 | **Biscuit / macaroon** (atenuovateľné tokeny) | ~1 kB | čiastočne | ❌ | Nie | Stredná | ❌ v1, **výskum pre v2** (delegácia relayu) |

\* Odhad pre licenciu s 10 entitlementmi, hybridný podpis ES256 + ML-DSA-65, Base64.

**Prečo B1 a nie B2, aj keď je B2 o 35 % menšie a .NET 10 ho podporuje natívne** (`CoseKey(MLDsa)` je novinka .NET 10, takže technická prekážka odpadla)**:** licenčný súbor sa prenáša raz za 30 dní, nie 1 000×/s — 2 kB rozdielu je irelevantné. Rozhodujú dva iné dôvody: (1) **čitateľnosť** — zákazník aj podpora si vedia licenciu otvoriť v editore a vidieť, čo bolo kúpené, čo pri CBOR neplatí; (2) **veľkosť ekosystému** — JWS má nástroje, knižnice a ľudí v každom jazyku, do ktorého budeme raz robiť SDK. B2 ponúkame ako profil `symlic+cose` pre embedded ciele s obmedzenou pamäťou, so **zdieľanou množinou claimov**, aby konverzia bola mechanická.

**Prečo NIE `CompositeMLDsa` (natívne v .NET 10):** je `[Experimental]` (SYSLIB5006), viaže verifikátora na rovnakú platformovú podporu ako signatára (macOS v .NET 10 PQC nemá vôbec) a **znemožňuje degradovanú validáciu** — starý klient bez PQC nevie overiť ani klasickú polovicu. Dva nezávislé podpisy v jednom JWS to riešia: klient overí, čo vie, a politika rozhodne, čo stačí. Cena za to je útok odstránením podpisu — riešime v 9.4.

### 5.2.2 Špecifikácia `symlic/1`

**Obálka (PEM armor, RFC 7468 štýl):**

```
-----BEGIN SYMBOLON LICENSE-----
eyJwYXlsb2FkIjoiZXlKcGMzTWlPaUp6ZVc0dGRHVnpkQ0lzSW5OMVlpSTZJbXhwWXlY...
-----END SYMBOLON LICENSE-----
```
Base64 (bez URL-safe, s wrap na 64 znakov) JWS General JSON dokumentu.

**JWS General JSON:**

```jsonc
{
  "payload": "<base64url(claims JSON)>",
  "signatures": [
    {
      "protected": "<base64url({\"alg\":\"ES256\",\"kid\":\"prd-acme-2026-ec\",\"typ\":\"symlic+jws\",\"crit\":[\"symlic\"],\"symlic\":\"1\"})>",
      "signature": "<base64url(64 B)>"
    },
    {
      "protected": "<base64url({\"alg\":\"ML-DSA-65\",\"kid\":\"prd-acme-2026-pq\",\"typ\":\"symlic+jws\",\"crit\":[\"symlic\"],\"symlic\":\"1\"})>",
      "signature": "<base64url(3309 B)>"
    }
  ]
}
```

**Claims (payload):**

```jsonc
{
  "iss": "https://licenses.acme.example",     // vydavateľ
  "sub": "lic_01JQ8ZK4N9V2X6M0",              // ID licencie (ULID)
  "aud": "acme-cad",                          // product code
  "jti": "lf_01JQ8ZK5T3P7Q1R4",               // ID tohto konkrétneho súboru (nonce)
  "iat": 1788480000,
  "nbf": 1788480000,
  "exp": 1791072000,                          // TTL SÚBORU (nie licencie!)
  "symlic": {
    "v": 1,
    "profile": "hybrid-v1",
    "requiredAlgs": ["ES256", "ML-DSA-65"],   // politika verifikácie, chránená podpisom
    "license": {
      "key": "SYM-4K7QT-…",                   // len ak je súbor viazaný na kľúč
      "model": "floating",
      "state": "active",
      "issuedAt": "2026-09-03T00:00:00Z",
      "expiresAt": null,                      // null = perpetual
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
    "binding": {                              // voliteľné, pre node-locked/offline
      "fingerprint": "sha256:9f2c…",
      "matching": "match-most",
      "components": { "machineId": "…", "cpu": "…", "board": "…" }
    },
    "policy": {                               // pre offline klienta bez servera
      "lease": { "ttl": "PT10M", "graceTtl": "PT4H" },
      "clockSkewTolerance": "PT5M",
      "revocation": { "url": "https://…/v1/revocations/latest", "maxAge": "P7D" }
    }
  }
}
```

**Kritické pravidlá validácie (normatívne):**

1. Verifikátor **musí** odmietnuť súbor, ak nedokáže overiť **všetky** algoritmy uvedené v `symlic.requiredAlgs`, ktoré sú v jeho zozname podporovaných. Ak `requiredAlgs` obsahuje algoritmus, ktorý verifikátor nepozná vôbec, správanie určuje jeho konfigurácia (`strict` = odmietnuť, `lenient` = overiť zvyšok) — **default je `strict` pre `exp > 1 rok`.**
2. `requiredAlgs` je vnútri podpísaného payloadu → útočník ho nevie zmenšiť bez zneplatnenia oboch podpisov. Toto je obrana proti downgrade/stripping útoku.
3. Overuje sa **nezávisle** každý podpis nad `ASCII(BASE64URL(protected) || '.' || BASE64URL(payload))` podľa RFC 7515 §5.2. Podpisy sa **nekombinujú**.
4. `exp` súboru ≠ `expiresAt` licencie. Súbor je perishable snapshot; jeho krátke TTL je jediný funkčný mechanizmus revokácie v offline režime.
5. Klient si perzistuje **najvyšší videný `iat`** a odmietne súbor s `iat` menším než uložená hodnota mínus `clockSkewTolerance` (obrana proti rollbacku hodín a replayu starého súboru).

### 5.2.3 Verziovanie formátu

- `symlic.v` je celé číslo. Zmena, ktorá **odoberá** alebo **mení sémantiku** existujúceho poľa → nová major verzia a nový `typ` (`symlic2+jws`).
- Pridanie poľa je vždy povolené; neznáme polia sa **ignorujú**, pokiaľ nie sú v `crit`.
- Klienti deklarujú `X-Symbolon-Accept: symlic/1` a server vydá najvyššiu verziu, ktorú klient vie.

## 5.3 Artefakt C — Lease Token

Kompaktný JWS (RFC 7515 §7.1), `alg: ES256`, typicky **~450 bajtov**.

```jsonc
// protected header
{ "alg": "ES256", "kid": "lease-2026-09", "typ": "symlease+jwt" }
// payload
{
  "iss": "relay:rly_01JQ…", "sub": "lic_01JQ8ZK4N9V2X6M0",
  "jti": "lse_01JQ9A…",          // lease id
  "iat": 1788480000, "exp": 1788480600,
  "seat": 7,
  "fp": "sha256:9f2c…",          // hash fingerprintu držiteľa
  "ent": ["core", "module.cad-export"],
  "gnt": "gnt_01JQ…",            // z ktorého Seat Grantu bol vydaný
  "seq": 1043                    // monotónne per-lease, proti replayu obnovy
}
```

**Prečo tu NEPOUŽÍVAME post-kvantový podpis (a je to obhájiteľné):**
Falšovanie podpisu lease tokenu musí prebehnúť **počas jeho platnosti** (30 s – 10 min). „Harvest now, decrypt later" sa na podpisy nevzťahuje — kvantový počítač v roku 2035 nedokáže spätne sfalšovať token, ktorý expiroval v roku 2026. PQC je nutné tam, kde je **artefakt dlhoveký** alebo kde je **verejný kľúč dlhoveký a nemenný**. Lease kľúč rotujeme mesačne. Konfiguračne sa dá zapnúť `ML-DSA-44` aj tu (profil `pq-only-v1`) pre zákazníkov s formálnou požiadavkou — ale je to compliance, nie bezpečnosť.

## 5.4 Artefakt D — Seat Grant (`.symgrant`)

Jadro hybridnej topológie. Control plane podpíše: *„relay R smie vydať najviac N súbežných sedadiel pre licenciu L v okne [t0, t1], sekvencia S"*.

```jsonc
{
  "iss": "https://licenses.acme.example",
  "sub": "lic_01JQ8ZK4N9V2X6M0",
  "aud": "rly_01JQ8ZM2…",
  "jti": "gnt_01JQ8ZN7…",
  "iat": 1788480000, "nbf": 1788480000, "exp": 1788566400,   // 24 h
  "symgrant": {
    "v": 1,
    "seats": 10,                 // disjunktná časť z max_seats=25
    "seatRange": [0, 9],         // explicitné, aby sa granty nemohli prekrývať
    "seq": 42,                   // monotónne per (license, relay)
    "supersedes": 41,
    "entitlements": ["core", "module.cad-export"],
    "leasePolicy": { "ttl": "PT10M", "graceTtl": "PT4H" },
    "leaseKey": { "kty": "EC", "crv": "P-256", "x": "…", "y": "…", "kid": "lease-rly1-2026-09" },
    "offlineExtension": { "allowed": true, "maxExtensions": 7 }
  }
}
```

**Vlastnosti, ktoré z toho plynú:**
- **Žiadny distribuovaný konsenzus.** Súčet `seats` cez všetky platné granty ≤ `maxSeats`. Relaye sa navzájom nemusia vidieť ani poznať.
- **HA bez triadu.** Dva relaye × 12 sedadiel = 24 z 25. Padne jeden → polovica kapacity ďalej funguje. Žiadne kvórum, žiadny „tertiary, ktorý sa nikdy nestane masterom", žiadnych 5 minút štartu, ako to má FlexNet.
- **Air-gapped floating.** Grant sa dá preniesť USB kľúčom. Relay funguje 24 h (alebo `maxExtensions` × 24 h pri autorizovanom predĺžení) bez akéhokoľvek spojenia.
- **Bezpečná degradácia.** Po `exp` relay prestáva vydávať nové leases a existujúce dobehnú do svojho `graceTtl`.
- **Auditovateľnosť.** Sekvencia `seq` s `supersedes` znemožňuje replay starého grantu (relay aj control plane si držia `last_seq`).

## 5.5 Artefakt E — Revocation List (`.symrl`)

```jsonc
{
  "iss": "https://licenses.acme.example",
  "iat": 1788480000, "exp": 1788566400,
  "symrl": {
    "v": 1, "seq": 1187,                 // monotónne, klient odmietne seq < last_seen
    "full": false, "since": 1187 - 1,    // delta zoznam
    "revoked": [
      { "t": "license", "id": "lic_01JQ…", "at": 1788470000, "reason": "non-payment" },
      { "t": "machine", "id": "mch_01JQ…", "at": 1788471000, "reason": "fraud" },
      { "t": "kid",     "id": "prd-acme-2025-ec", "at": 1788400000, "reason": "key-compromise" }
    ]
  }
}
```
Revokácia `kid` je dôležitá — je to jediný spôsob, ako v teréne zabiť kompromitovaný podpisový kľúč bez update binárky.

---

[← 4. Doménový model a licenčné modely](04-domenovy-model.md) · [Obsah](README.md) · [6. Architektúra →](06-architektura.md)
