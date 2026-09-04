# 2. Kontext, problém a ciele

> Časť zadania **Symbolon — licenčný server na .NET 10**. Späť na [obsah](README.md) · [prehľad projektu](../README.md).

## 2.1 Problém

ISV (independent software vendor), ktorý predáva softvér nasadzovaný **u zákazníka** — desktop, on-prem server, priemyselné zariadenie, air-gapped prostredie — potrebuje:

- vydávať a overovať oprávnenia (entitlements) bez trvalého pripojenia na internet,
- vynucovať súbežnosť (koľko používateľov naraz), nie iba počet inštalácií,
- dať zákazníkovi auditovateľné dáta o využití (true-up, peak concurrency, denials),
- prežiť výpadok licenčného servera bez zastavenia prebiehajúcej práce,
- neposielať údaje o zamestnancoch zákazníka do cloudu tretej strany (GDPR, podnikové rady, dátová rezidencia v EÚ),
- a od 11. 12. 2027 mať pre produkty na trhu EÚ pripravený režim podľa **Cyber Resilience Act** (SBOM, hlásenie zraniteľností; ohlasovacie povinnosti od 11. 9. 2026).

Dnešné možnosti: buď drahý proprietárny démon z 90. rokov, alebo moderné cloud API, ktoré on-prem floating rieši ako doplnok, alebo si to napíšeš sám (a väčšina si to píše sama, zle).

## 2.2 Analýza konkurencie (stav 09/2026)

| Produkt | Licencia | Stack | Floating | On-prem | PQC | Poznámka |
|---|---|---|---|---|---|---|
| **Keygen** (keygen.sh) | FCL-1.0-ALv2 (Fair Source, **nie OSI**), konverzia na Apache-2.0 po 2 rokoch | Ruby on Rails, Postgres, Redis | Áno, prvotriedne (`floating`, `maxMachines/Processes/Users`, overage stratégie, heartbeat s resurrection) | CE zdarma, EE licencované | Nie (Ed25519 / ECDSA P-256 / RSA-2048) | Zrelý, ~1,5 k★, ~5,4 k commitov. **Referenčný dizajn, nie nepriateľ.** |
| **keygen-relay** | **MIT** | Go + SQLite | Lease TTL default 60 s, FIFO/LIFO/random, HMAC-SHA256 | Áno, offline-first | Nie | **~34★.** Najbližší konkurent klinu. Žiadna HA story. |
| **Keygate** | AGPL-3.0 + komerčná | Go + Postgres + S3 | Deklarované | Áno | Nie | ~47★, veľmi mladé, jednovendorové |
| **Devolens** (ex-Cryptolens) | Proprietárne | — | Áno | Nie (len offline license server add-on) | Nie | Silné .NET dedičstvo, €199–699/rok |
| **LicenseSpring** | Proprietárne | — | Áno; on-prem floating až v Enterprise | Áno (Enterprise, cena na dopyt) | Nie | $199 / $750 / custom |
| **10Duke Enterprise** | Proprietárne | — | Deklarované | Nie (single-tenant managed) | Nie | od $199/mes. |
| **FlexNet Publisher** | Proprietárne | C démony | Áno, referenčná implementácia | Áno | Nie | 2025 R2 = 11.19.9. Triad, borrow 168 h, options file. Vysoký lock-in a nenávisť zákazníkov |
| **Reprise RLM** | Proprietárne | C | Áno, roaming max 30 dní | Áno | Nie | Jednoduchší FlexNet |
| **Sentinel RMS/LDK** | Proprietárne | C | Áno, commuter | Áno | Nie | Vzdialený commuter sa **nedá** vrátiť predčasne |
| **Standard.Licensing** | MIT | .NET | **Nie** | n/a | Nie | ~668★. Len podpísané XML offline. Rieši ~15 % problému |

**Záver analýzy:** tri nezávislé medzery sa prekrývajú presne v jednom bode — **OSI-licencovaný, moderný, .NET-natívny, on-prem floating server s post-kvantovo pripravenými podpismi**. Žiadny produkt na trhu nespomína ML-DSA (FIPS 204) ani SLH-DSA (FIPS 205). Všetci podpisujú Ed25519 / ECDSA P-256 / RSA-2048.

**Prečo je PQC v tejto doméne technicky (nie marketingovo) relevantné:** perpetuálne licenčné súbory sú **dlhoveké podpísané artefakty** s platnosťou 5–20 rokov. Ak sa počas ich životnosti objaví kryptograficky relevantný kvantový počítač, útočník vie **falšovať nové licencie** pod starým verejným kľúčom, ktorý je zapečený v miliónoch nasadených inštalácií. Migrácia podpisového koreňa v už distribuovanom softvéri je rádovo drahšia než ho urobiť správne hneď. EÚ roadmapa (NIS Cooperation Group, 23. 6. 2025) hovorí: **začať prechod do konca 2026, kritická infraštruktúra do konca 2030.**

## 2.3 Ciele a metriky

**G1 — Korektnosť účtovania.** Počet súbežne aktívnych sedadiel nikdy nepresiahne limit (mimo explicitne nakonfigurovaného overage), ani pri konkurentnom zaťažení, reštarte servera či posune hodín.
*Metrika:* property-based a konkurenčné testy (CsCheck) preukážu 0 porušení na 10⁶ operáciách; záťažový test 500 súbežných checkoutov na jednu licenciu nevyprodukuje pretečenie.

**G2 — Prežitie výpadku.** Strata licenčného servera nezastaví bežiacu prácu.
*Metrika:* pri výpadku relayu klient pokračuje v grace režime po dobu `grace_ttl` (default 4 h, max 168 h); nové checkouty sú odmietnuté; po obnove sa stav zosúladí bez duplicity sedadiel.

**G3 — Offline / air-gapped.** Plná aktivácia a floating bez akéhokoľvek pripojenia relayu na internet.
*Metrika:* E2E scenár „air-gapped závod" prejde bez sieťovej cesty medzi relayom a control plane; výmena prebehne cez súbory (`.symreq` → `.symgrant`).

**G4 — Crypto-agilita.** Zmena podpisového algoritmu nevyžaduje zmenu formátu ani klienta staršej verzie.
*Metrika:* pridanie tretieho podpisu (napr. SLH-DSA) do existujúceho licenčného súboru nespôsobí zlyhanie validácie u klienta v1.

**G5 — Overiteľnosť.** Auditovateľné dáta na úrovni, ktorú vyžaduje enterprise obstarávanie.
*Metrika:* append-only ledger s serverovými časovými pečiatkami, exportovateľný CSV/JSON; peak concurrency počítaný z ledgeru, nie vzorkovaním.

**G6 — Vývojárska skúsenosť.** „Od `docker run` po prvý úspešný checkout do 10 minút."
*Metrika:* merané na 3 externých testeroch bez pomoci autora.

## 2.4 Validácia PRED stavbou (7 dní, nie 150)

Toto urob skôr, než napíšeš prvý riadok kódu:

1. **5 rozhovorov** s .NET ISV firmami, ktoré predávajú on-prem (CEE priemyselný/laboratórny/CAD softvér). Otázky: čím dnes licencujete, koľko to stojí, čo vás na tom hnevá, ktoré funkcie by museli byť, aby ste to vymenili.
2. **1 rozhovor** so správcom licencií u veľkého používateľa FlexNetu (univerzita, inžinierska firma) — na stranu dopytu po reportingu.
3. **Landing page + README-first** — napíš README a dokumentáciu produktu, ktorý neexistuje, a odmeraj, koľko ľudí sa prihlási na notifikáciu.
4. **Rozhodnutie:** ak z 5 rozhovorov aspoň 2 povedia „toto by som testoval", stavaj V0.1. Inak nie.

---

[← 1. Manažérske zhrnutie a kritické posúdenie](01-manazerske-zhrnutie.md) · [Obsah](README.md) · [3. Rozsah projektu →](03-rozsah.md)
