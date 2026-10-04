# Symbolon — otvorená špecifikácia

**Verzia:** `symlic/1` — draft
**Licencia:** [CC BY 4.0](LICENSE) (`CC-BY-4.0`) — odlišná od zvyšku repozitára, viď [LICENSING.md](../LICENSING.md)
**Stav:** návrh. Normatívne časti sú stabilné v zámere, nie vo formulácii — pred `1.0` sa môžu meniť.

> 🇬🇧 **English version:** The official English translation of this specification is available in [`spec/en/`](en/README.md).

Táto špecifikácia popisuje formáty artefaktov a sieťový protokol licenčného systému
Symbolon tak, aby ich vedel implementovať ktokoľvek, nezávisle od referenčnej
implementácie v .NET. Je zámerne oddelená od kódu aj od zadania.

---

## Obsah

| # | Dokument | Anglicky (English) | Obsah |
|---|---|---|---|
| 1 | [Artefakty](01-artefakty.md) | [Artifacts](en/01-artifacts.md) | Prehľad piatich artefaktov a prečo sú oddelené |
| 2 | [License Key](02-license-key.md) | [License Key](en/02-license-key.md) | Formát licenčného kľúča, abeceda, kontrolný súčet, uloženie |
| 3 | [`symlic/1` — License File](03-symlic-1.md) | [`symlic/1` — License File](en/03-symlic-1.md) | JWS General JSON, claims, **pravidlá validácie**, verziovanie |
| 4 | [Lease Token](04-lease-token.md) | [Lease Token](en/04-lease-token.md) | Kompaktný JWS pre držanie sedadla |
| 5 | [Seat Grant](05-seat-grant.md) | [Seat Grant](en/05-seat-grant.md) | Delegovaná kapacita pre relay |
| 6 | [Revocation List](06-revocation-list.md) | [Revocation List](en/06-revocation-list.md) | Revokácia licencií, strojov a podpisových kľúčov |
| 7 | [Floating protokol](07-floating-protokol.md) | [Floating Protocol](en/07-floating-protocol.md) | Životný cyklus sedadla, checkout/renew/release, borrow, rezervácie, air-gapped tok |
| 8 | [Fingerprint a node-locking](08-fingerprint.md) | [Fingerprint & Node-Locking](en/08-fingerprint.md) | Komponenty, matching stratégie, kontajnery a VM |

---

## Konvencie

### Kľúčové slová

Kľúčové slová **MUSÍ**, **NESMIE**, **VYŽADUJE SA**, **MAL BY**, **NEMAL BY**,
**ODPORÚČA SA**, **MÔŽE** a **VOLITEĽNÉ** sa v tomto dokumente vykladajú podľa
[RFC 2119](https://www.rfc-editor.org/rfc/rfc2119) a [RFC 8174](https://www.rfc-editor.org/rfc/rfc8174),
a to **iba** keď sú napísané veľkými písmenami.

### Identifikátory požiadaviek

Každá normatívna požiadavka má stabilný identifikátor v tvare `PREFIX-N`:

| Prefix | Oblasť |
|---|---|
| `KEY` | License Key |
| `LIC` | License File (`.symlic`) |
| `LSE` | Lease Token |
| `GNT` | Seat Grant (`.symgrant`) |
| `RVL` | Revocation List (`.symrl`) |
| `FLT` | Floating protokol |
| `FPR` | Fingerprint |

Identifikátory sú **stabilné naprieč verziami** — ak požiadavka zanikne, jej číslo sa
nerecykluje. Testovacie vektory a testy zhody sa na ne odvolávajú.

### Nenormatívny text

Bloky uvedené ako *Poznámka:* alebo `> Zdôvodnenie:` sú **nenormatívne**. Vysvetľujú,
prečo požiadavka existuje; neurčujú správanie.

---

## Zhoda (conformance)

Špecifikácia definuje tri roly. Implementácia MUSÍ deklarovať, ktoré roly napĺňa.

| Rola | Musí implementovať |
|---|---|
| **Vydavateľ** (issuer) | `KEY`, `LIC`, `GNT`, `RVL` — podpisovanie a vydávanie artefaktov |
| **Vydávajúci uzol** (relay) | `LSE`, `GNT` (overenie), `FLT` — vydávanie lease tokenov proti grantu |
| **Overovateľ** (verifier / SDK) | `LIC` (overenie), `LSE` (overenie), `RVL`, `FPR` — klientska strana |

**Overovateľ MUSÍ implementovať minimálne profil `classical-v1`** (ES256).
Profily sú definované v [03-symlic-1.md](03-symlic-1.md#35-kryptografické-profily).

---

## Verziovanie

Špecifikácia sa verzuje ako celok reťazcom `symlic/N`, kde `N` je celé číslo.

- **Pridanie poľa** je vždy povolené a NESMIE zvýšiť `N`.
- **Odobratie poľa** alebo **zmena sémantiky** existujúceho poľa MUSÍ zvýšiť `N`.
- Neznáme polia sa MUSIA ignorovať, pokiaľ nie sú uvedené v `crit`.

Podrobnosti pre licenčný súbor sú v [03-symlic-1.md](03-symlic-1.md#37-verziovanie-formátu).

---

## Mimo rozsahu

Táto špecifikácia **nedefinuje**:

- **Správu kľúčov** — hierarchiu, rotáciu, úložisko a konvenciu `kid` popisuje
  kapitola 9.3 zadania. Špecifikácia z nej preberá iba to, čo je nutné na interpretáciu
  poľa `kid` a formátu JWKS.
- **Administratívne API** (`/admin/v1`) — je to implementačný detail vydavateľa,
  nie interoperabilný povrch.
- **Doménový model a databázovú schému** — viď kapitoly 4 a 6.5 zadania.
- **Klientske vynucovanie licencie.** Symbolon nie je anti-piracy nástroj; klientská
  kontrola je odstrašenie a viditeľnosť využitia, nie prevencia.

---

## Testovacie vektory

Adresár [`vectors/`](../vectors/) obsahuje referenčné normatívne artefakty pre požiadavky,
ktoré sa dajú overiť staticky — vrátane FIPS 204 KAT vektorov pre ML-DSA-65 a párov
*(vstup, očakávaný verdikt)* pre pravidlá validácie
[`LIC-21` až `LIC-34`](03-symlic-1.md#34-validácia-normatívne).
Referenčná implementácia ich overuje v testovacej sade `VectorConformanceTests.cs`.

Prioritné vektory:

| Vektor | Súbor | Overuje |
|---|---|---|
| platný hybrid, oba podpisy | `lic-21-valid-hybrid.json` | `LIC-21`, `LIC-22` |
| odstránený ML-DSA podpis, `requiredAlgs` ho stále žiada | `lic-23-downgrade-attack.json` | `LIC-23` — **downgrade útok** |
| neznámy `alg` v `requiredAlgs`, `exp − iat` 2 roky | `lic-25-unknown-alg.json` | `LIC-25` (`auto` → odmietnuť) |
| `symlic.v` nezhodné s hlavičkou `symlic` | `lic-15-version-mismatch.json` | `LIC-15` |
| `iat` nižší než naposledy videný | `lic-30-clock-rollback.json` | `LIC-30` — rollback hodín |
| podpis platný, `kid` revokovaný | `lic-31-revoked-kid.json` | `LIC-31` |

---

## Známe medzery

Poctivý zoznam stavu otvorených otázok špecifikácie:

1. ~~**Jazyk.**~~ **Vyriešené.** Oficiálny anglický preklad otvorenej špecifikácie je dostupný v [`spec/en/`](en/README.md).
2. ~~**Testovacie vektory.**~~ **Vyriešené.** Kompletná sada 6 normatívnych testovacích vektorov je implementovaná v [`vectors/`](../vectors/) a overovaná testovacou sadou `VectorConformanceTests`.
3. ~~**`.symlease`**~~ **Vyriešené.** Štruktúra claimov artefaktu `.symlease`, schéma v PEM armore a mechanizmus proof-of-possession sú normatívne špecifikované v [07-floating-protokol.md §7.4.1](07-floating-protokol.md#741-štruktúra-artefaktu-symlease).
4. ~~**Profil `symlic+cose`**~~ **Vyriešené.** Celočíselné CBOR/COSE mapovanie claimov podľa RFC 8392 a RFC 9964 je normatívne špecifikované v [03-symlic-1.md §3.8.1](03-symlic-1.md#381-mapovanie-claimov-do-cbor-celočíselných-kľúčov).
5. ~~**Formát fronty (`queueTicket`)**~~ **Vyriešené.** Formát odpovede 202, stavový model a endpointy pre čakanie na sedadlo sú normatívne špecifikované v [07-floating-protokol.md §7.6.2](07-floating-protokol.md#762-formát-fronty-queue-ticket).
6. ~~**Registrácia relayu a mTLS identita**~~ **Vyriešené.** Registračný endpoint `POST /relay/v1/register`, priradenie `relayId` a verifikácia odtlačku klientskeho certifikátu `mtlsThumbprint` sú normatívne špecifikované v [07-floating-protokol.md §7.6](07-floating-protokol.md#76-endpointy).

> **Stav špecifikácie:** Všetky pôvodné medzery boli uzavreté. Špecifikácia `symlic/1` je úplná, sebestačná a plne pripravená na nezávislú implementáciu tretími stranami.

---

## Zdroj

Normatívny obsah je prepis kapitol **5** (formát licencie) a **7** (floating protokol)
zadania, doplnený o kapitolu **9.4**, na ktorú sa kapitola 5 pri pravidlách validácie
výslovne odvoláva. Zadanie zostáva v [`docs/`](../docs/) a je *All rights reserved* —
argumentácia, zamietnuté varianty a obchodný kontext sa do `spec/` zámerne neprepisovali.

Pri rozpore medzi `spec/` a `docs/` platí **`spec/`**.
