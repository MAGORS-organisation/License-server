# 1. Manažérske zhrnutie a kritické posúdenie

> Časť zadania **Symbolon — licenčný server na .NET 10**. Späť na [obsah](README.md) · [prehľad projektu](../README.md).

## 1.0 Najprv to nepríjemné: prečo to pravdepodobne nemáš stavať tak, ako si to zadal

Zadal si „open-source licenčný server". Skôr než sa dostaneme k ML-DSA a floating leases, tri veci, ktoré by ti mal povedať niekto, kto nie je platený za to, aby to postavil.

**(1) Trh je malý a štrukturálne klesá.** Licenčné servery pre on-prem/perpetual softvér sú produkt éry, ktorú SaaS systematicky nahrádza. Zvyšné jadro trhu — CAD, EDA, simulácie, priemyselný a laboratórny softvér — je konzervatívne, pomalé a kupuje od etablovaných dodávateľov (Revenera, Thales, Reprise). Sú to zákazníci, ktorí robia bezpečnostné audity dodávateľa a chcú referencie z vlastného odvetvia. Neznámy open-source projekt od slovenského konzultanta nie je pre nich v prvých dvoch rokoch nákupná možnosť. To nie je pesimizmus, to je predajný cyklus.

**(2) Monetizácia open-source infraštruktúry je najhoršia časť tvojej hypotézy.** Tvoji používatelia sú ISV firmy — ľudia, ktorí vedia hostovať Postgres a čítať dokumentáciu. Presne preto si vyberú teba namiesto SaaS. Empirický dôkaz priamo v tejto kategórii: zakladateľ **Keygenu** otvoril kód v roku 2023 pod Elastic License 2.0 a v auguste 2024 prešiel na **Fair Core License (FCL-1.0-ALv2)** — teda ešte reštriktívnejšiu, non-OSI licenciu s doložkou zakazujúcou obchádzanie licenčnej funkcionality. Človek, ktorý na tomto trhu žije, sa rozhodol, že OSI-licencia ho neuživí. **Keygate** (AGPL, Go) má ~47 hviezd. **keygen-relay** (MIT, Go) má ~34 hviezd. To je celá veľkosť open-source dopytu v tejto kategórii v roku 2026.

**(3) Oportunitné náklady sú reálne peniaze.** Realistický odhad k dôveryhodnej verzii 1.0 je **150–180 človekodní** (kapitola 13). Pri tvojej konzultantskej sadzbe je to šesťciferná investícia vlastného kapitálu do produktu s neistým výnosom, v čase, keď máš rozbehnutý SFP v Ultime, MATPEX a plán relokácie. Ak toto postavíš, niečo iné nepostavíš.

**Kognitívne skreslenia, ktoré tu vidím a mal by si ich pomenovať nahlas:**

| Skreslenie | Ako sa prejavuje v tomto zadaní | Test reality |
|---|---|---|
| **Zvádzanie remeslom** | Zaujímavá časť je kryptografia, PQC a .NET 10. To je ~15 % práce a 0 % dôvodu, prečo by ti niekto zaplatil. Nudná časť — admin UI, migrácie, dokumentácia, support, fakturácia, onboarding — je 85 % a rozhoduje o adopcii. | Napíš najprv dokumentáciu a onboarding flow. Ak ťa to nebaví, projekt zomrie na 60 %. |
| **PQC ako FOMO** | „Nikto to nemá" je pravda (žiadny licenčný produkt na trhu nepoužíva ML-DSA), ale nie je to nákupný spúšťač. Žiadny CIO nevymieňa FlexNet kvôli FIPS 204. | Nájdi jedného zákazníka, ktorý povie „kúpim to, lebo je to post-kvantové". Ak ho nenájdeš do 60 dní, PQC je diferenciátor v pitch decku, nie v rozpočte. |
| **„Open source = distribúcia"** | Open source je distribučný kanál len vtedy, ak doň investuješ DevRel. Inak je to verejný priečinok. | Naplánuj si 20 % kapacity na obsah a komunitu, alebo neočakávaj adopciu. |
| **Kotvenie na FlexNet parity** | Feature-matching 30-ročného produktu je nekonečný backlog. | Definuj non-goals skôr než goals (kap. 3.3). |
| **Pasca krásnej špecifikácie** | Tento dokument sám osebe vytvára záväzok. Špecifikácia je lacná, build nie. | Validuj pred stavaním (kap. 2.4). |

## 1.1 Čo z toho prežije kritiku

Napriek vyššie uvedenému existujú tri argumenty, ktoré obstoja:

1. **Medzera je reálna a úzka.** Neexistuje zrelý, OSI-licencovaný, komunitne spravovaný licenčný server s poriadnym floating režimom pre on-prem. Moderné API-first produkty (Keygen, Devolens ex-Cryptolens, LicenseSpring) sú cloud-first a on-prem floating riešia ako enterprise upsell alebo tenkú nadstavbu. Skutočný on-prem floating vlastnia 25–30-ročné C démony (`lmgrd` + vendor daemon), ktorých protokol, admin UX a observabilita zamrzli v 90. rokoch. To nie je marketingová medzera, to je technologický dlh celého segmentu.
2. **Toto je mimoriadne silný kompetenčný artefakt.** Fungujúci, otestovaný, PQC-ready licenčný server s vážne mysleným threat modelom na .NET 10 je pre PRAESTAR/MATPEX silnejší predajný nástroj než akákoľvek prezentácia. To ospravedlňuje projekt ako **marketingový capex**, nie ako produktovú stávku. Toto je podľa mňa jeho primárna hodnota.
3. **Amortizácia.** Ak ho použiješ na licencovanie vlastného alebo klientskeho softvéru, časť nákladov sa vráti v úspore za komerčný nástroj (QLM ~1 000 €/rok, Sentinel/FlexNet rádovo 10 000 €+ vstupne).

## 1.2 Odporúčanie: preformuluj cieľ

**Nestavaj „open-source licenčný server" ako produktovú stávku. Postav klin.**

> **V0.1 = `Symbolon.Relay` + `Symbolon.Client` SDK + otvorená špecifikácia formátu.**
> On-prem floating licenčný relay v jednom binári, s .NET SDK, hybridnými (klasicko-postkvantovými) podpismi a offline režimom. ~45–60 človekodní. Publikuj. Nechaj trh povedať, či má zmysel stavať control plane.

Dôvody, prečo práve toto:
- Je to jediná časť, kde je konkurencia demonštrovateľne slabá (keygen-relay: 34★, Go-only, 60 s lease TTL, žiadna HA story).
- Je to samostatne užitočné aj bez control plane — dá sa nasadiť pred existujúci backend.
- Je to dokončiteľné jedným človekom v definovanom čase, čo je pri solo projekte jediné kritérium, na ktorom naozaj záleží.
- Vytvára opciu, nie záväzok.

Zvyšok tohto dokumentu popisuje **cieľovú architektúru celého systému**, aby V0.1 nebolo slepou uličkou. Ale plán vydania (kap. 13) fázuje tak, že po V0.1 môžeš so cťou skončiť.

## 1.3 Kritériá zabitia projektu (napíš si ich, kým si nadšený)

Vyhodnotenie **90 dní po zverejnení V0.1**:

| Metrika | Prah pokračovania | Ak nie |
|---|---|---|
| GitHub stars | ≥ 250 | Distribučný kanál nefunguje → zastav marketingový naratív, nechaj to ako portfólio artefakt |
| Nezávislé nasadenia (issues/otázky od cudzích ľudí) | ≥ 5 | Nikto to nepoužíva → zastav |
| Design partneri (ISV ochotný testovať v prod.) | ≥ 2 | Nemáš dopyt → zastav pred stavbou control plane |
| Prvý platený rozhovor (support/integrácia/komerčná licencia) | ≥ 1 | Monetizačná hypotéza vyvrátená → drž ako OSS hobby, nie ako biznis |
| Tvoj vlastný čas strávený nad plán | ≤ 130 % odhadu | Odhad bol zlý → prehodnoť rozsah, nie termín |

Ak padnú **tri z piatich**, projekt sa zastavuje. Bez diskusie a bez „ešte jednej fičúry".

## 1.4 Zhrnutie technického rozhodnutia (pre netrpezlivých)

| Oblasť | Rozhodnutie | Kľúčový argument |
|---|---|---|
| Runtime | .NET 10 LTS, ASP.NET Core Minimal API | LTS do 11/2028; natívne PQC API; zdrojovo generovaná validácia a OpenAPI 3.1 |
| Formát licenčného súboru | **JWS General JSON Serialization**, viac podpisov, PEM obálka | RFC 7515 + **RFC 9964** (ML-DSA pre JOSE/COSE, Proposed Standard, máj 2026) → štandardizovaný, čitateľný, multi-signature natívne |
| Kryptografia | **Hybrid: ES256 (ECDSA P-256) + ML-DSA-65**, dva nezávislé podpisy | Crypto-agilita, degradovateľnosť na platformách bez PQC, vyhýba sa experimentálnemu `CompositeMLDsa` |
| Licenčný kľúč | Neprenášajúci dáta: 128-bit náhodný, Crockford Base32 + CRC | Odpojenie dĺžky kľúča od veľkosti podpisu — jediný spôsob, ako mať PQC a zároveň zapisovateľný kľúč |
| Účtovanie sedadiel | Materializované `seats` riadky v Postgres + `FOR UPDATE SKIP LOCKED` | O(1) checkout, žiadny Redis, žiadny distribuovaný konsenzus |
| HA namiesto triadu | **Delegated Seat Grant** — control plane podpíše relayu N sedadiel na čas T; granty sú disjunktné | Nepotrebuje kvórum. Toto je hlavná architektonická inovácia oproti FlexNet triad |
| DB | Postgres 17+ / EF Core 10 / Npgsql 10 (control plane); SQLite (relay) | Relay = jeden binár, nula závislostí |
| Native AOT | **Nie** pre control plane (EF Core AOT nie je produkčné), **áno** pre relay | Relay nepoužíva EF Core → AOT dáva jeden malý binár |
| Licencia projektu | Apache-2.0 (SDK/špec) + AGPL-3.0-only + CLA (server) | SDK sa linkuje do produktov ISV → musí byť permisívne. Server musí byť OSI — to je celý klin proti Keygenu |

---

[← 0. Ako čítať tento dokument](00-ako-citat.md) · [Obsah](README.md) · [2. Kontext, problém a ciele →](02-kontext-a-ciele.md)
