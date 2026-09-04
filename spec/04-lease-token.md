# 4. Lease Token

> [← `symlic/1`](03-symlic-1.md) · [Obsah](README.md) · [Seat Grant →](05-seat-grant.md)

Lease Token je **krátkodobý** dôkaz o držaní sedadla. Vydáva ho relay (alebo control
plane) pri checkoute a obnovuje pri každom heartbeate. Neukladá sa na disk.

---

## 4.1 Formát

**LSE-1.** Lease Token MUSÍ byť JWS v **Compact Serialization** (RFC 7515 §7.1).

**LSE-2.** `typ` MUSÍ byť `symlease+jwt`.

**LSE-3.** `alg` MUSÍ byť `ES256`. Implementácia MÔŽE podporovať `ML-DSA-44`
pre profil `pq-only-v1`.

```jsonc
// protected header
{ "alg": "ES256", "kid": "lease-2026-09", "typ": "symlease+jwt" }
```

```jsonc
// payload
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

Typická veľkosť je ~450 bajtov.

---

## 4.2 Claims

| Claim | Povinnosť | Význam |
|---|---|---|
| `iss` | MUSÍ | vydávajúci uzol: `relay:{relayId}` alebo URI control plane |
| `sub` | MUSÍ | ID licencie |
| `jti` | MUSÍ | ID lease (ULID s prefixom `lse_`) |
| `iat` | MUSÍ | čas vydania |
| `exp` | MUSÍ | koniec platnosti tokenu |
| `seat` | MUSÍ | číslo sedadla v rámci licencie |
| `fp` | MUSÍ | hash fingerprintu držiteľa |
| `ent` | MUSÍ | entitlements platné pre toto sedadlo |
| `gnt` | MUSÍ*, ak vydal relay | ID [Seat Grantu](05-seat-grant.md), z ktorého bol token vydaný |
| `seq` | MUSÍ | monotónne rastúce číslo per lease |

**LSE-4.** `fp` MUSÍ byť hash, nie surový fingerprint. Formát je `{algo}:{hex}`,
kde `algo` je `sha256`.

> **Zdôvodnenie (nenormatívne).** Lease token prechádza sieťou a môže sa ocitnúť
> v logoch. Surový fingerprint obsahuje sériové čísla hardvéru a hostname —
> pseudonymizácia je požiadavka GDPR, nie hygiena.

**LSE-5.** `seq` MUSÍ pri každom `renew` rásť. Overovateľ MUSÍ odmietnuť token so
`seq` menším alebo rovným naposledy videnému pre rovnaké `jti`.

> **Zdôvodnenie (nenormatívne).** Bez `seq` by útočník vedel zopakovať staršiu,
> ešte platnú odpoveď na `renew` a udržať sedadlo po jeho uvoľnení.

**LSE-6.** `seat` je celé číslo od `0`. Musí padnúť do `seatRange` grantu uvedeného
v `gnt`, ak je token vydaný relayom.

---

## 4.3 Podpisový kľúč

**LSE-7.** Ak token vydáva relay, MUSÍ ho podpísať kľúčom **doručeným v Seat Grante**
(`symgrant.leaseKey`), nie vlastným kľúčom.

**LSE-8.** Relay NESMIE vydať lease token, ktorého `exp` presahuje `exp` grantu,
z ktorého bol vydaný.

**LSE-9.** Overovateľ MUSÍ overiť, že `kid` tokenu zodpovedá `leaseKey.kid` grantu,
ktorý pozná.

---

## 4.4 Prečo tu nie je post-kvantový podpis

Toto je vedomé rozhodnutie, nie opomenutie.

Falšovanie podpisu lease tokenu musí prebehnúť **počas jeho platnosti** — teda
v okne 30 s až 10 min. Model hrozby *„harvest now, decrypt later"* sa na podpisy
nevzťahuje: kvantový počítač v roku 2035 nedokáže spätne sfalšovať token, ktorý
expiroval v roku 2026.

PQC je nutné tam, kde je **artefakt dlhoveký** ([License File](03-symlic-1.md)) alebo
kde je **verejný kľúč dlhoveký a nemenný**. Lease kľúč sa rotuje mesačne a doručuje
sa v grante.

**LSE-10.** Implementácia MÔŽE na požiadanie podpisovať lease tokeny aj `ML-DSA-44`
(profil `pq-only-v1`). Toto nastavenie MAL BY byť dokumentované ako **compliance
požiadavka, nie bezpečnostné vylepšenie**.

---

## 4.5 Overenie na strane klienta

**LSE-11.** Klient MUSÍ overiť podpis tokenu pred jeho použitím.

**LSE-12.** Klient MUSÍ overiť, že `fp` zodpovedá jeho vlastnému fingerprintu.

**LSE-13.** Klient MUSÍ odmietnuť token, ktorého `sub` nezodpovedá licencii,
pod ktorou beží.

**LSE-14.** Klient NESMIE token perzistovať na disk. Výnimkou je artefakt
`.symlease` pri [borrow](07-floating-protokol.md#74-borrow--roaming), ktorý je
samostatný typ artefaktu s vlastnou platnosťou.

---

> [← `symlic/1`](03-symlic-1.md) · [Obsah](README.md) · [Seat Grant →](05-seat-grant.md)
