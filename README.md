# Symbolon — open-source licenčný server na .NET 10

**Kompletné zadanie projektu.** Tento repozitár zatiaľ neobsahuje kód — obsahuje zadanie,
ktoré rozhoduje o tom, či a v akom rozsahu sa kód vôbec bude písať.

| | |
|---|---|
| **Verzia dokumentu** | 1.0 |
| **Dátum** | 3. september 2026 |
| **Autor / vlastník** | Matej Langsfeld — PRAESTAR / MATPEX s.r.o. |
| **Stav** | Návrh na rozhodnutie (**nie schválené zadanie**) |
| **Cieľová platforma** | .NET 10 LTS (podpora do 14. 11. 2028), C# 14 |
| **Licencovanie** | **Rozhodnuté** podľa ADR-006 — viď [LICENSING.md](LICENSING.md) |

---

## Čo to je

Licenčný server pre ISV firmy, ktoré predávajú softvér nasadzovaný **u zákazníka** — desktop,
on-prem server, priemyselné zariadenie, air-gapped prostredie. Rieši to, čo dnešné cloud-first
produkty riešia ako doplnok: **on-prem floating (concurrent) licencie** s lease/heartbeat
protokolom, offline režimom a post-kvantovo pripravenými podpismi.

Tri nezávislé medzery na trhu sa prekrývajú presne v jednom bode:
**OSI-licencovaný, moderný, .NET-natívny, on-prem floating server s hybridnými podpismi.**
Žiadny produkt na trhu (Keygen, LicenseSpring, FlexNet, Reprise, Sentinel) nepoužíva
ML-DSA (FIPS 204) ani SLH-DSA (FIPS 205).

## Skôr než sa nadchneš

Kapitola [1.0](docs/01-manazerske-zhrnutie.md) je **zámerne nepríjemná** a treba ju prečítať prvú.
V skratke: trh je malý a štrukturálne klesá, monetizácia open-source infraštruktúry je najslabšia
časť hypotézy (zakladateľ Keygenu prešiel z OSI licencie na Fair Core), a realistický odhad
k dôveryhodnej verzii 1.0 je **150–180 človekodní**.

**Odporúčanie zadania: nestavať produktovú stávku, ale klin.**

> **V0.1 = `Symbolon.Relay` + `Symbolon.Client` SDK + otvorená špecifikácia formátu.**
> On-prem floating licenčný relay v jednom binári, s .NET SDK, hybridnými podpismi a offline
> režimom. **~45–60 človekodní.** Publikuj a nechaj trh povedať, či má zmysel stavať control plane.

Pred prvým riadkom kódu je v kapitole [2.4](docs/02-kontext-a-ciele.md) 7-dňová validácia
(5 rozhovorov s ISV firmami, 1 so správcom licencií, README-first landing page).
Kritériá zabitia projektu sú v [1.3](docs/01-manazerske-zhrnutie.md) — vyhodnotenie 90 dní
po zverejnení V0.1.

## Zhrnutie technického rozhodnutia

| Oblasť | Rozhodnutie | Kľúčový argument |
|---|---|---|
| Runtime | .NET 10 LTS, ASP.NET Core Minimal API | LTS do 11/2028; natívne PQC API v BCL; zdrojovo generovaná validácia a OpenAPI 3.1 |
| Formát licenčného súboru | **JWS General JSON Serialization**, viac podpisov, PEM obálka | RFC 7515 + RFC 9964 → štandardizovaný, čitateľný, multi-signature natívne |
| Kryptografia | **Hybrid: ES256 (ECDSA P-256) + ML-DSA-65**, dva nezávislé podpisy | Crypto-agilita, degradovateľnosť na platformách bez PQC, vyhýba sa experimentálnemu `CompositeMLDsa` |
| Licenčný kľúč | Neprenášajúci dáta: 128-bit náhodný, Crockford Base32 + CRC | Odpojenie dĺžky kľúča od veľkosti podpisu |
| Účtovanie sedadiel | Materializované `seats` riadky v Postgres + `FOR UPDATE SKIP LOCKED` | O(1) checkout, žiadny Redis, žiadny distribuovaný konsenzus |
| HA namiesto triadu | **Delegated Seat Grant** — control plane podpíše relayu N sedadiel na čas T | Nepotrebuje kvórum. Hlavná architektonická inovácia oproti FlexNet triad |
| DB | Postgres 17+ / EF Core 10 (control plane); SQLite (relay) | Relay = jeden binár, nula závislostí |
| Native AOT | **Nie** pre control plane, **áno** pre relay | EF Core AOT nie je produkčné; relay EF Core nepoužíva |
| Licencia projektu | Apache-2.0 (SDK/špec) + AGPL-3.0-only + CLA (server) | SDK sa linkuje do produktov ISV → musí byť permisívne. Server musí byť OSI — to je celý klin proti Keygenu |

## Obsah zadania

Dokument je napísaný ako reťaz štyroch rolí: **architekt** (4–7), **programátor** (8),
**bezpečák** (9), **tester** (10). Kapitoly 0–3 sú produktové, 11–15 prevádzkové a biznisové.

| # | Kapitola | Rola |
|---|---|---|
| 0 | [Ako čítať tento dokument](docs/00-ako-citat.md) | Produkt |
| 1 | [Manažérske zhrnutie a kritické posúdenie](docs/01-manazerske-zhrnutie.md) | Produkt |
| 2 | [Kontext, problém a ciele](docs/02-kontext-a-ciele.md) | Produkt |
| 3 | [Rozsah projektu](docs/03-rozsah.md) | Produkt |
| 4 | [Doménový model a licenčné modely](docs/04-domenovy-model.md) | Architekt |
| 5 | [Formát licencie — návrh variantov a rozhodnutie](docs/05-format-licencie.md) | Architekt |
| 6 | [Architektúra](docs/06-architektura.md) | Architekt |
| 7 | [Floating licencie — protokol](docs/07-floating-protokol.md) | Architekt |
| 8 | [Referenčná implementácia (.NET 10)](docs/08-referencna-implementacia.md) | Programátor |
| 9 | [Bezpečnosť](docs/09-bezpecnost.md) | Bezpečák |
| 10 | [Testovacia stratégia](docs/10-testovacia-strategia.md) | Tester |
| 11 | [Prevádzka](docs/11-prevadzka.md) | Prevádzka |
| 12 | [OSS governance a monetizácia](docs/12-oss-governance.md) | Biznis |
| 13 | [Roadmapa, odhad úsilia a riziká](docs/13-roadmapa-a-rizika.md) | Biznis |
| 14 | [Otvorené otázky — rozhodnutia, ktoré musíš urobiť ty](docs/14-otvorene-otazky.md) | Biznis |
| 15 | [Zdroje](docs/15-zdroje.md) | — |

Index kapitol je aj v [`docs/`](docs/README.md).

## Fázy

| Fáza | Obsah | Odhad |
|---|---|---|
| **V0.1 — Klin** | Formát licencie, krypto vrstva, `symbolon-relay`, `Symbolon.Client` SDK, CLI, floating checkout/renew/release/heartbeat, offline grant, dokumentácia | 45–60 čd |
| **V1.0 — Control plane** | Multi-tenant control plane, Admin API, politiky, entitlements, node-lock, revokácie, Delegated Seat Grants, borrow/roaming, audit ledger, OpenAPI 3.1, Helm chart | +90–120 čd |
| **V1.5 — Prevádzkovateľnosť** | Admin web konzola (Blazor), SSO/OIDC, webhooky, metriky, migračné nástroje | +60–80 čd |
| **V2.0 — Distribúcia** | Podpísaná distribúcia artefaktov (OCI), SBOM/CRA modul, transparency log | +60 čd |

## Čo tento projekt nie je

1. **Nie je anti-piracy nástroj.** Klientské vynucovanie je odstrašenie a viditeľnosť využitia, nie prevencia.
2. **Nie je obfuskátor ani packer.**
3. **Nie je fakturačný systém.** Integruje sa cez webhooky, nenahrádza.
4. **Nerobí parity s FlexNet.** Options file syntax, `lmgrd` protokol a `.lic` kompatibilita sú mimo rozsahu.

## Licencovanie

Rozdelené podľa [ADR-006](docs/06-architektura.md). Záväzná mapa je v **[LICENSING.md](LICENSING.md)**.

| Časť | Licencia |
|---|---|
| koreň repozitára (default) — server, control plane, relay, CLI | [`AGPL-3.0-only`](LICENSE) |
| `src/Symbolon.{Format,Crypto,Client,Protocol}` — SDK do produktov ISV | [`Apache-2.0`](LICENSES/Apache-2.0.txt) |
| [`spec/`](spec/) — formát, protokol, testovacie vektory | [`CC-BY-4.0`](spec/LICENSE) |
| `docs/` — zadanie (obsahuje interný obchodný materiál) | All rights reserved, viď [LICENSING.md](LICENSING.md) |

Serverová časť je AGPL + CLA zámerne: [CLA zatiaľ neexistuje](LICENSING.md#prispievanie-a-cla),
takže **externé príspevky do `src/` zatiaľ neprijímaj** — inak sa komerčná dual-licencia
zablokuje natrvalo.

## Ďalší krok

Zodpovedať otázky **Q1–Q7** v kapitole [14](docs/14-otvorene-otazky.md). Bez nich sa zadanie
nedá schváliť — najmä Q1 (validácia vs. rovno stavať) a Q4 (PRAESTAR vs. MATPEX ako držiteľ
autorských práv, blokuje CLA). Q3 (AGPL vs. Apache) je rozhodnutá v prospech ADR-006.

---

*Dokument pripravený ako podklad pre rozhodnutie. Nie je právnym ani bezpečnostným posudkom.*
