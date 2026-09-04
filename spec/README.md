# Symbolon — otvorená špecifikácia

**Verzia:** `symlic/1` — draft
**Licencia:** [CC BY 4.0](LICENSE) (`CC-BY-4.0`) — odlišná od zvyšku repozitára, viď [LICENSING.md](../LICENSING.md)
**Stav:** návrh. Normatívne časti sú stabilné v zámere, nie vo formulácii — pred `1.0` sa môžu meniť.

Táto špecifikácia popisuje formáty artefaktov a sieťový protokol licenčného systému
Symbolon tak, aby ich vedel implementovať ktokoľvek, nezávisle od referenčnej
implementácie v .NET. Je zámerne oddelená od kódu aj od zadania.

---

## Obsah

| # | Dokument | Obsah |
|---|---|---|
| 1 | [Artefakty](01-artefakty.md) | Prehľad piatich artefaktov a prečo sú oddelené |
| 2 | [License Key](02-license-key.md) | Formát licenčného kľúča, abeceda, kontrolný súčet, uloženie |
| 3 | [`symlic/1` — License File](03-symlic-1.md) | JWS General JSON, claims, **pravidlá validácie**, verziovanie |
| 4 | [Lease Token](04-lease-token.md) | Kompaktný JWS pre držanie sedadla |
| 5 | [Seat Grant](05-seat-grant.md) | Delegovaná kapacita pre relay |
| 6 | [Revocation List](06-revocation-list.md) | Revokácia licencií, strojov a podpisových kľúčov |
| 7 | [Floating protokol](07-floating-protokol.md) | Životný cyklus sedadla, checkout/renew/release, borrow, rezervácie, air-gapped tok |
| 8 | [Fingerprint a node-locking](08-fingerprint.md) | Komponenty, matching stratégie, kontajnery a VM |

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

Adresár `vectors/` (zatiaľ neexistuje) bude obsahovať referenčné artefakty pre každú
požiadavku, ktorá sa dá overiť staticky — vrátane FIPS 204 KAT vektorov pre ML-DSA-65
a párov *(vstup, očakávaný verdikt)* pre pravidlá validácie
[`LIC-21` až `LIC-34`](03-symlic-1.md#34-validácia-normatívne).
Bez nich nie je špecifikácia overiteľná a implementácie sa rozídu.

Prioritné vektory (bez nich sa implementácie rozídu na bezpečnostne relevantnom mieste):

| Vektor | Overuje |
|---|---|
| platný hybrid, oba podpisy | `LIC-21`, `LIC-22` |
| odstránený ML-DSA podpis, `requiredAlgs` ho stále žiada | `LIC-23` — **downgrade útok** |
| neznámy `alg` v `requiredAlgs`, `exp − iat` 2 roky | `LIC-25` (`auto` → odmietnuť) |
| `symlic.v` nezhodné s hlavičkou `symlic` | `LIC-15` |
| `iat` nižší než naposledy videný | `LIC-30` — rollback hodín |
| podpis platný, `kid` revokovaný | `LIC-31` |

---

## Známe medzery

Poctivý zoznam toho, čo v tomto drafte chýba:

1. **Jazyk.** Špecifikácia je v slovenčine. Otvorený formát, ktorý má implementovať
   ktokoľvek, potrebuje anglickú verziu — inak je CC BY 4.0 gesto bez účinku.
   Preklad je podmienka zverejnenia, nie „nice to have".
2. **Testovacie vektory neexistujú.** Viď vyššie.
3. **`.symlease`** (borrow artefakt, [07](07-floating-protokol.md#74-borrow--roaming))
   nemá vlastnú štruktúru claimov — zadanie ho popisuje len ako „samostatne overiteľný
   artefakt s hybridným podpisom". Treba došpecifikovať.
4. **Profil `symlic+cose`** (CBOR pre embedded ciele) je v zadaní deklarovaný ako
   voliteľný, ale nie je zmapovaný claim po claime.
5. **Formát fronty** (`queueTicket`) nie je špecifikovaný.
6. **Registrácia relayu** (`POST /relay/v1/register`) a formát mTLS identity nie sú
   pokryté — sú na hranici medzi protokolom a nasadením.

---

## Zdroj

Normatívny obsah je prepis kapitol **5** (formát licencie) a **7** (floating protokol)
zadania, doplnený o kapitolu **9.4**, na ktorú sa kapitola 5 pri pravidlách validácie
výslovne odvoláva. Zadanie zostáva v [`docs/`](../docs/) a je *All rights reserved* —
argumentácia, zamietnuté varianty a obchodný kontext sa do `spec/` zámerne neprepisovali.

Pri rozpore medzi `spec/` a `docs/` platí **`spec/`**.
