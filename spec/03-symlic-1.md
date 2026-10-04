# 3. `symlic/1` — License File

> [← License Key](02-license-key.md) · [Obsah](README.md) · [Lease Token →](04-lease-token.md)

License File (`.symlic`) je **dlhoveký podpísaný artefakt**, ktorý nesie entitlements,
limity a voliteľné viazanie na stroj. Je to jediná časť systému, ktorá sa po nasadení
u zákazníkov **nedá zmeniť**.

---

## 3.1 Obálka

**LIC-1.** Súbor MUSÍ byť PEM armor v štýle [RFC 7468](https://www.rfc-editor.org/rfc/rfc7468)
s návestím `SYMBOLON LICENSE`:

```
-----BEGIN SYMBOLON LICENSE-----
eyJwYXlsb2FkIjoiZXlKcGMzTWlPaUp6ZVc0dGRHVnpkQ0lzSW5OMVlpSTZJbXhwWXlY...
-----END SYMBOLON LICENSE-----
```

**LIC-2.** Telo obálky MUSÍ byť **štandardný Base64** (nie base64url), zalomený na
64 znakov na riadok.

**LIC-3.** Dekódované telo MUSÍ byť JWS dokument v **General JSON Serialization**
(RFC 7515 §7.2).

**LIC-4.** Overovateľ MUSÍ prijať aj holý JWS JSON dokument bez PEM obálky.
Obálka je transportná konvencia, nie súčasť podpisovaného obsahu.

**LIC-5.** Odporúčaná prípona súboru je `.symlic`. Overovateľ NESMIE robiť rozhodnutia
o platnosti na základe prípony ani názvu súboru.

---

## 3.2 Štruktúra JWS

```jsonc
{
  "payload": "<base64url(claims JSON)>",
  "signatures": [
    {
      "protected": "<base64url({\"alg\":\"ES256\",\"kid\":\"prd-acme-2026-09-ec\",\"typ\":\"symlic+jws\",\"crit\":[\"symlic\"],\"symlic\":\"1\"})>",
      "signature": "<base64url(64 B)>"
    },
    {
      "protected": "<base64url({\"alg\":\"ML-DSA-65\",\"kid\":\"prd-acme-2026-09-pq\",\"typ\":\"symlic+jws\",\"crit\":[\"symlic\"],\"symlic\":\"1\"})>",
      "signature": "<base64url(3309 B)>"
    }
  ]
}
```

**LIC-6.** Pole `signatures` MUSÍ obsahovať aspoň jednu položku.

**LIC-7.** Každá položka MUSÍ mať hlavičku `protected` obsahujúcu minimálne `alg`,
`kid`, `typ` a `symlic`.

**LIC-8.** `typ` MUSÍ byť `symlic+jws`.

**LIC-9.** `crit` MUSÍ obsahovať `"symlic"`. Hlavičkový parameter `symlic` MUSÍ byť
reťazcová hodnota rovná major verzii formátu (`"1"`).

> **Zdôvodnenie (nenormatívne).** `crit` zabezpečí, že implementácia JWS, ktorá
> Symbolon nepozná, dokument **odmietne** namiesto toho, aby ho ticho prijala bez
> kontroly verzie (RFC 7515 §4.1.11).

**LIC-10.** Hlavička `unprotected` sa NESMIE používať. Všetky hlavičkové parametre
MUSIA byť v `protected`.

**LIC-11.** Vydavateľ NESMIE do jedného dokumentu vložiť dva podpisy s rovnakým `alg`.

---

## 3.3 Claims (payload)

```jsonc
{
  "iss": "https://licenses.acme.example",
  "sub": "lic_01JQ8ZK4N9V2X6M0",
  "aud": "acme-cad",
  "jti": "lf_01JQ8ZK5T3P7Q1R4",
  "iat": 1788480000,
  "nbf": 1788480000,
  "exp": 1791072000,
  "symlic": {
    "v": 1,
    "profile": "hybrid-v1",
    "requiredAlgs": ["ES256", "ML-DSA-65"],
    "license": {
      "key": "SYM-4K7QT-…",
      "model": "floating",
      "state": "active",
      "issuedAt": "2026-09-03T00:00:00Z",
      "expiresAt": null,
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
    "binding": {
      "fingerprint": "sha256:9f2c…",
      "matching": "match-most",
      "components": { "machineId": "…", "cpu": "…", "board": "…" }
    },
    "policy": {
      "lease": { "ttl": "PT10M", "graceTtl": "PT4H" },
      "clockSkewTolerance": "PT5M",
      "revocation": { "url": "https://…/v1/revocations/latest", "maxAge": "P7D" }
    }
  }
}
```

### 3.3.1 Registrované claims

| Claim | Povinnosť | Význam |
|---|---|---|
| `iss` | MUSÍ | URI vydavateľa |
| `sub` | MUSÍ | ID licencie (ULID s prefixom `lic_`) |
| `aud` | MUSÍ | `code` produktu |
| `jti` | MUSÍ | ID **tohto konkrétneho súboru** (ULID s prefixom `lf_`), slúži ako nonce |
| `iat` | MUSÍ | čas vydania súboru |
| `nbf` | MAL BY | ak chýba, predpokladá sa `iat` |
| `exp` | MUSÍ | **TTL súboru**, nie licencie — viď `LIC-14` |

**LIC-12.** `jti` MUSÍ byť unikátny naprieč všetkými súbormi vydanými pre danú licenciu.

**LIC-13.** `aud` MUSÍ zodpovedať `code` produktu, pre ktorý je licencia vydaná.
Overovateľ MUSÍ odmietnuť súbor, ktorého `aud` nezodpovedá produktu, v ktorom je
zabudovaný.

**LIC-14.** `exp` claim JWS označuje **platnosť súboru**, nie platnosť licencie.
Platnosť licencie je v `symlic.license.expiresAt`. Tieto dve hodnoty NESMÚ byť
zamieňané.

> **Zdôvodnenie (nenormatívne).** Súbor je *perishable snapshot*. Jeho krátke TTL je
> jediný funkčný mechanizmus revokácie v offline režime: perpetuálna licencia
> (`expiresAt: null`) sa doručuje ako súbor s 30-dňovým `exp`, ktorý sa periodicky
> obnovuje. Keď zákazník prestane platiť, vydavateľ jednoducho prestane obnovovať.

### 3.3.2 Objekt `symlic`

| Pole | Povinnosť | Význam |
|---|---|---|
| `v` | MUSÍ | major verzia formátu, celé číslo |
| `profile` | MUSÍ | kryptografický profil, viď 3.5 |
| `requiredAlgs` | MUSÍ | politika verifikácie chránená podpisom, viď 3.4 |
| `license` | MUSÍ | stav a metadáta licencie |
| `limits` | MUSÍ | kvantitatívne limity |
| `entitlements` | MUSÍ | zoznam oprávnení; MÔŽE byť prázdny |
| `binding` | VOLITEĽNÉ | viazanie na stroj (node-locked / offline) |
| `policy` | MAL BY | politika pre klienta bez spojenia so serverom |

**LIC-15.** `symlic.v` MUSÍ zodpovedať hodnote hlavičkového parametra `symlic`
v každom `protected`. Pri nezhode MUSÍ overovateľ dokument odmietnuť.

**LIC-16.** `license.model` MUSÍ byť jedna z hodnôt: `perpetual`, `subscription`,
`trial`, `floating`, `named-user`, `metered`.

**LIC-17.** `license.state` MUSÍ byť jedna z hodnôt: `active`, `suspended`, `expired`,
`revoked`. Overovateľ MUSÍ odmietnuť súbor so stavom iným než `active`.

**LIC-18.** `license.expiresAt` s hodnotou `null` znamená **perpetual**. Overovateľ
NESMIE takúto licenciu považovať za expirovanú bez ohľadu na `maintenanceUntil`.

**LIC-19.** `limits.seatUnit` MUSÍ byť jedna z hodnôt: `machine`, `process`, `user`,
`core`. Pole je povinné a **nemá default**.

> **Zdôvodnenie (nenormatívne).** Granularita sedadla je cenový model, nie implementačný
> detail. Retrofit znamená prerobiť zmluvy — preto sa nesmie dať „zabudnúť".

**LIC-20.** Entitlement MUSÍ mať `code`. Entitlement typu kvóta MUSÍ mať navyše
`value` (číslo) a MÔŽE mať `period` (ISO-8601 duration). Neznámy `code` sa MUSÍ
ignorovať, nie odmietnuť.

---

## 3.4 Validácia (normatívne)

Toto je najdôležitejšia časť špecifikácie. Odchýlka tu je bezpečnostná chyba, nie
nekompatibilita.

### 3.4.1 Overenie podpisov

**LIC-21.** Každý podpis sa MUSÍ overiť **nezávisle** nad vstupom

```
ASCII(BASE64URL(protected) || '.' || BASE64URL(payload))
```

podľa RFC 7515 §5.2. Podpisy sa **NESMÚ** kombinovať, agregovať ani XOR-ovať.

**LIC-22.** Overovateľ MUSÍ overiť **všetky** algoritmy uvedené v
`symlic.requiredAlgs`, ktoré sú v jeho zozname podporovaných algoritmov.

**LIC-23.** Ak overovateľ algoritmus z `requiredAlgs` **podporuje**, ale zodpovedajúci
podpis v dokumente **chýba alebo neplatí**, MUSÍ dokument odmietnuť. **Bez výnimiek.**

**LIC-24.** Podpis s `alg`, ktorý nie je v `requiredAlgs`, MÔŽE overovateľ ignorovať.
Jeho neplatnosť NESMIE sama osebe viesť k odmietnutiu.

### 3.4.2 Nepodporovaný algoritmus a striktnosť

**LIC-25.** Ak `requiredAlgs` obsahuje algoritmus, ktorý overovateľ **nepozná vôbec**,
správanie určuje konfigurovaná striktnosť:

| Striktnosť | Správanie |
|---|---|
| `strict` | MUSÍ odmietnuť vždy |
| `auto` (**default**) | MUSÍ odmietnuť, ak `exp − iat > 1 rok`; inak MÔŽE akceptovať a MUSÍ zalogovať `degraded-verification` |
| `lenient` | MÔŽE akceptovať a MUSÍ zalogovať `degraded-verification` |

**LIC-26.** Striktnosť `lenient` je určená **výlučne pre migračné okná**. Implementácia
NEMALA BY ju ponúkať ako trvalé nastavenie.

**LIC-27.** Overovateľ MUSÍ hlásiť do telemetrie počet degradovaných overení.

> **Zdôvodnenie (nenormatívne).** Bez tohto merania nikto nezistí, že celá flotila roky
> beží iba na ES256. `LIC-27` je to, čo robí z hybridného podpisu skutočnú ochranu
> namiesto papierovej.

**LIC-28.** Klientske SDK MUSÍ obsahovať managed **verify-only** implementáciu ML-DSA
ako fallback pre platformy bez natívnej podpory, aby bol prípad `LIC-25` v praxi
zriedkavý.

> *Poznámka:* `requiredAlgs` je **vnútri podpísaného payloadu**, takže útočník ho nevie
> zmenšiť bez zneplatnenia všetkých podpisov. Spolu s `LIC-23` je to obrana proti
> **downgrade / signature-stripping útoku** — jedinej reálnej slabine nekompozitného
> hybridu. Zodpovedá kapitole 9.4 zadania.

### 3.4.3 Časová a stavová validácia

**LIC-29.** Overovateľ MUSÍ odmietnuť súbor, pre ktorý platí `now < nbf − skew`
alebo `now > exp + skew`, kde `skew` je `symlic.policy.clockSkewTolerance`
(default `PT5M`, ak chýba).

**LIC-30.** Overovateľ MUSÍ perzistovať **najvyšší doteraz videný `iat`** pre danú
licenciu a MUSÍ odmietnuť súbor, ktorého `iat` je menší než uložená hodnota mínus
`clockSkewTolerance`.

> **Zdôvodnenie (nenormatívne).** Toto je obrana proti dvom útokom naraz: posunutiu
> systémových hodín dozadu a replay-u starého, ešte platne podpísaného súboru
> s väčším rozsahom oprávnení.

**LIC-31.** Overovateľ MUSÍ odmietnuť súbor podpísaný `kid`, ktorý je uvedený
v platnom [revokačnom zozname](06-revocation-list.md), aj keď je podpis kryptograficky
platný.

**LIC-32.** Ak `symlic.policy.revocation` existuje, overovateľ MAL BY načítať revokačný
zoznam a NEMAL BY dôverovať zoznamu staršiemu než `revocation.maxAge`.

**LIC-33.** Ak `binding` existuje, overovateľ MUSÍ overiť fingerprint podľa
`binding.matching` a pravidiel v [08-fingerprint.md](08-fingerprint.md).

### 3.4.4 Poradie

**LIC-34.** Validácia MUSÍ prebehnúť v tomto poradí a pri prvom zlyhaní sa MUSÍ
ukončiť:

1. rozbalenie obálky a parsovanie JWS (`LIC-1`–`LIC-5`),
2. kontrola hlavičiek a `crit` (`LIC-7`–`LIC-11`, `LIC-15`),
3. overenie podpisov (`LIC-21`–`LIC-28`),
4. časová validácia (`LIC-29`, `LIC-30`),
5. revokácia (`LIC-31`, `LIC-32`),
6. stav a publikum (`LIC-13`, `LIC-17`),
7. viazanie na stroj (`LIC-33`).

> **Zdôvodnenie (nenormatívne).** Podpis sa overuje **pred** interpretáciou claimov.
> Opačné poradie znamená rozhodovať na základe neoverených dát — to je pôvod väčšiny
> zraniteľností v domácich implementáciách JWT.

---

## 3.5 Kryptografické profily

**LIC-35.** `symlic.profile` MUSÍ byť jedna z hodnôt:

| Profil | `requiredAlgs` | Použitie |
|---|---|---|
| `classical-v1` | `["ES256"]` | migrácia a platformy bez PQC |
| `hybrid-v1` | `["ES256", "ML-DSA-65"]` | **default** pre dlhoveké artefakty |
| `pq-only-v1` | `["ML-DSA-65"]` | formálna compliance požiadavka |

**LIC-36.** `requiredAlgs` MUSÍ zodpovedať zvolenému profilu. Pri nezhode MUSÍ
overovateľ dokument odmietnuť.

**LIC-37.** Vydavateľ MUSÍ pre licencie s `exp − iat > 1 rok` použiť profil
obsahujúci post-kvantový algoritmus.

> **Zdôvodnenie (nenormatívne).** Perpetuálne licenčné súbory sú podpísané artefakty
> s platnosťou 5–20 rokov. Ak sa počas ich životnosti objaví kryptograficky relevantný
> kvantový počítač, útočník vie falšovať **nové** licencie pod starým verejným kľúčom
> zapečeným v už nasadených inštaláciách. Migrácia podpisového koreňa v distribuovanom
> softvéri je rádovo drahšia než urobiť to správne hneď.

**LIC-38.** Overovateľ MUSÍ podporovať `ES256`. Podpora `ML-DSA-65` sa ODPORÚČA.

**LIC-39.** Podpis MUSÍ byť generovaný nezávislými kľúčmi pre klasickú a post-kvantovú
polovicu. Kompozitné schémy (jeden kľúč, jeden podpis pokrývajúci oba algoritmy) sa
v `symlic/1` NESMÚ používať.

> **Zdôvodnenie (nenormatívne).** Kompozitný podpis je atomický — overovateľ bez PQC
> podpory neoverí ani klasickú polovicu. Dva nezávislé podpisy umožňujú postupnú
> migráciu (klient v1 overí ES256, klient v2 vyžaduje oboje, klient v3 môže vyžadovať
> len ML-DSA) bez zmeny formátu, a dovoľujú použiť rôzne HSM pre každú polovicu.

---

## 3.6 Kľúče a `kid`

**LIC-40.** `kid` MUSÍ dodržiavať konvenciu `{role}-{tenant}-{yyyy-MM}-{alg}`,
napr. `prd-acme-2026-09-pq`.

**LIC-41.** Verejné kľúče MUSIA byť publikované ako JWKS na
`GET /v1/.well-known/symbolon-keys`.

**LIC-42.** Post-kvantové kľúče v JWKS MUSIA používať `kty: AKP` podľa RFC 9964.

> *Poznámka:* hierarchiu kľúčov, periódy platnosti a pravidlá rotácie táto špecifikácia
> nedefinuje — viď kapitolu 9.3 zadania. Pre interoperabilitu stačí `LIC-40`–`LIC-42`.

---

## 3.7 Verziovanie formátu

**LIC-43.** Pridanie poľa do `symlic` je vždy povolené a NESMIE zvýšiť `symlic.v`.

**LIC-44.** Odobratie poľa alebo zmena jeho sémantiky MUSÍ zvýšiť `symlic.v`
a MUSÍ zmeniť `typ` (`symlic2+jws`).

**LIC-45.** Klient MAL BY deklarovať podporované verzie hlavičkou
`X-Symbolon-Accept: symlic/1`. Server MUSÍ vydať najvyššiu verziu, ktorú klient
deklaruje.

---

## 3.8 Voliteľný profil `symlic+cose`

**LIC-46.** Implementácia MÔŽE podporovať alternatívnu serializáciu
`COSE_Sign` ([RFC 9052](https://www.rfc-editor.org/rfc/rfc9052)) s `typ: symlic+cose`
pre embedded ciele s obmedzenou pamäťou.

**LIC-47.** Profil `symlic+cose` MUSÍ používať **rovnakú množinu claimov** ako
`symlic+jws`. Konverzia medzi profilmi MUSÍ byť mechanická a bezstratová.

**LIC-48.** COSE algoritmy pre ML-DSA MUSIA byť podľa RFC 9964 (`-48` / `-49` / `-50`)
a pre ECDSA podľa RFC 9053 (`-7` pre ES256).

### 3.8.1 Mapovanie claimov do CBOR celočíselných kľúčov

Pre minimalizáciu binárnej veľkosti licencie na mikrokontroléroch a embedded zariadeniach
sa textové reťazce mapujú na štandardné CWT ([RFC 8392](https://www.rfc-editor.org/rfc/rfc8392))
a proprietárne Symbolon celočíselné kľúče:

| Úroveň | JSON reťazec | CBOR kľúč | Typ hodnoty | Štandard |
|---|---|---|---|---|
| Root CWT | `iss` | `1` | text string | RFC 8392 |
| Root CWT | `sub` | `2` | text string | RFC 8392 |
| Root CWT | `aud` | `3` | text string | RFC 8392 |
| Root CWT | `exp` | `4` | unsigned integer (NumericDate) | RFC 8392 |
| Root CWT | `nbf` | `5` | unsigned integer (NumericDate) | RFC 8392 |
| Root CWT | `iat` | `6` | unsigned integer (NumericDate) | RFC 8392 |
| Root CWT | `jti` | `7` | byte string / text string | RFC 8392 |
| Root CWT | `symlic` | `-65700` | map | Symbolon |
| `symlic` | `v` | `1` | unsigned integer | Symbolon |
| `symlic` | `profile` | `2` | unsigned int (`1`: classical, `2`: hybrid, `3`: pqc) | Symbolon |
| `symlic` | `requiredAlgs` | `3` | array of int (`-7`: ES256, `-49`: ML-DSA-65) | RFC 9964 / 9053 |
| `symlic` | `license` | `4` | map (`1`: key, `2`: model, `3`: state, `4`: issuedAt, `5`: customer) | Symbolon |
| `symlic` | `limits` | `5` | map (`1`: maxSeats, `2`: seatUnit, `3`: overageStrategy, `4`: maxRelays) | Symbolon |
| `symlic` | `entitlements` | `6` | array of maps (`1`: code, `2`: value, `3`: period) | Symbolon |
| `symlic` | `binding` | `7` | map (`1`: fingerprint, `2`: matching, `3`: components) | Symbolon |
| `symlic` | `policy` | `8` | map (`1`: lease, `2`: clockSkewTolerance, `3`: revocation) | Symbolon |

---

> [← License Key](02-license-key.md) · [Obsah](README.md) · [Lease Token →](04-lease-token.md)
