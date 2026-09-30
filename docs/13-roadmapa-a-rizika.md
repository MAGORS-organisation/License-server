# 13. Roadmapa, odhad úsilia a riziká

> Časť zadania **Symbolon — licenčný server na .NET 10**. Späť na [obsah](README.md) · [prehľad projektu](../README.md).

## 13.1 Odhad (jeden skúsený vývojár, čisté človekodni)

| Balík | ČD |
|---|---|
| Špecifikácia formátu + testovacie vektory | 5 |
| `Symbolon.Crypto` (hybrid, key ring, JWKS, fallback) | 9 |
| `Symbolon.Format` (JWS, PEM, verziovanie, verifikátor) | 7 |
| `Symbolon.Client` SDK (checkout, heartbeat, grace, fingerprint, offline cache) | 12 |
| `Symbolon.Relay` (SQLite, lease engine, AOT, sync) | 14 |
| CLI (`keys`, `license`, `grant`, `doctor`) | 6 |
| Testy V0.1 (unit + property + integračné + E2E) | 10 |
| Dokumentácia, README, quickstart, web | 7 |
| **V0.1 spolu** | **~70** *(rozsah 55–85 podľa toho, koľko času zožerie AOT a PQC platformová matica)* |
| Control plane: doména, EF Core, migrácie, Admin API | 30 |
| Politiky, entitlements, node-lock, revokácie | 14 |
| Delegated Seat Grants + relay sync + air-gapped | 16 |
| Borrow/roaming, fronta, rezervácie | 12 |
| Audit ledger + reporting API | 10 |
| Observabilita, health, Helm, deploy | 8 |
| Testy V1.0 | 20 |
| Dokumentácia V1.0 | 10 |
| **V1.0 spolu (kumulatívne)** | **~190** |
| **A/B Testovanie — Vytvorenie a experimentálny engine** (bucketing, canary rollout, telemetria) | 12 |
| **Reálne testovanie A/B testovaním** (10k virtuálnych klientov, Chi-Square validácia, chaos/drift harness) | 8 |
| **Post-Quantum Era** (FIPS 203 ML-KEM, FIPS 205 SLH-DSA, `pqc-strict` CNSA 2.0 profil, HNDL ochrana) | 14 |
| **PQC Scanner & HSM PKCS#11 integrácia** (quantum vulnerability scanner, Cloud KMS / HSM v3.2) | 10 |
| **V1.1+ Enterprise & PQC Era spolu (kumulatívne)** | **~234** |

> **Počítaj s ~234 ČD k plne vybavenej enterprise verzii pripravenej na post-kvantovú éru.** Fázovanie do postupných ucelených míľnikov zaručuje, že každá etapa prináša plne funkčný a samostatne nasaditeľný produkt s nulovým rizikom „uviaznutia v polovici“.

## 13.2 Míľniky

| Míľnik | Kritérium hotovosti |
|---|---|
| **M0 — Validácia** (7 dní) | 5 rozhovorov, rozhodnutie go/no-go |
| **M1 — Spike** (5 ČD) | ML-DSA podpis+overenie na Linux/Windows/macOS(fallback); AOT relay hello-world; `SKIP LOCKED` benchmark. **Ak M1 odhalí blokujúci problém s PQC platformami, prehodnoť ADR-003 pred zvyškom.** |
| **M2 — Formát zmrazený** | `spec/symlic-1.md` + testovacie vektory, verejné |
| **M3 — V0.1 relay** | 20 E2E scenárov zelených, quickstart do 10 min |
| **M4 — 90-dňové vyhodnotenie** | Kritériá z 1.3 |
| **M5 — V1.0 Core Enterprise** | Control plane, air-gap seat grants, borrowing, node-lock, migračný nástroj z Keygenu/FlexNetu, 2 referencie |
| **M6 — A/B Testovanie & Experimentácia** | Experimentálny engine, deterministický bucketing, canary circuit breaker, reálne A/B záťažové testovanie s 10k klientmi a štatistickým vyhodnotením |
| **M7 — Post-Quantum Era (Full PQC Suite)** | FIPS 203 ML-KEM, FIPS 205 SLH-DSA, `pqc-strict` profil (CNSA 2.0 / NIS 2), HNDL ochrana, PQC vulnerability scanner v CLI a Web TUI, PKCS#11 v3.2 HSM podpora |

## 13.3 Register rizík

| # | Riziko | P | D | Zmiernenie |
|---|---|---|---|---|
| R1 | **Nikto to nepoužije** | Vysoká | Vysoký | M0 validácia; kill criteria; hodnota ako portfólio artefakt aj pri nule adopcie |
| R2 | Rozsah sa rozšíri na FlexNet parity | Vysoká | Vysoký | Non-goals; rozpočet ČD na balík; „nie" ako default |
| R3 | PQC platformová matica (macOS, staré Linuxy) zožerie viac času než plán | Stredná | Stredný | M1 spike ako prvý; fallback od začiatku; ADR-003 je revidovateľné |
| R4 | `[Experimental]` ML-DSA API sa v .NET 11 zmení | Stredná | Stredný | Izolované za `ISignatureProvider`; KAT vektory zachytia zmenu správania |
| R5 | AGPL odradí enterprise | Stredná | Stredný | SDK Apache-2.0; pripravená komerčná výnimka a FAQ |
| R6 | Chyba v účtovaní sedadiel u zákazníka | Nízka | **Kritický** | Invarianty v schéme; property/konkurenčné testy; mutation score ≥ 80 % |
| R7 | Kompromitácia podpisového kľúča | Nízka | **Kritický** | Hierarchia, offline root, revokácia `kid`, krátke TTL |
| R8 | Vyhorenie / projekt zomrie na 60 % | **Vysoká** | Vysoký | Fázovanie tak, aby V0.1 bola samostatne hodnotná; časový strop; kill criteria |
| R9 | Právne riziko značky „Symbolon" | Nízka | Nízky | Rešerš EUIPO + domény pred prvým commitom (30 min) |
| R10 | **Skreslenie A/B testov a regresia výkonu pri experimentoch** | Stredná | Stredný | Deterministický bezstavový hashing; canary circuit breaker (<100ms rollback); Chi-Square validácia pred vyhodnotením |
| R11 | **Nárast veľkosti kľúčov/tokenov a handshake latency v Post-Kvantovej ére** | Stredná | Stredný | Hybridný režim ako predvolený; optimalizované AVX-512/ARM Neon assembly implementácie; kompresia hlavičiek; FALCON pre constrained zariadenia |

---

## 13.4 A/B Testovanie — Vytvorenie, Architektúra a Reálne Testovanie A/B Testovaním

### 13.4.1 Ciele a architektonický model experimentovania
V podnikovom licenčnom prostredí je zmena licenčných parametrov, časov prenájmu (lease TTL) alebo sprístupnenie nových funkcií vysoko riziková operácia. A/B testovací engine umožňuje prevádzkovateľom a ISV dodávateľom nasadzovať zmeny postupne na definované kohorty klientov bez výpadku a s exaktným štatistickým vyhodnotením dopadu.

```
                           ┌──────────────────────────────────────────────┐
                           │            Control Plane Engine              │
                           │  - Experiment Registry & Traffic Splitters   │
                           │  - Statistical Engine (Z-Score, p-value)     │
                           └──────────────────────┬───────────────────────┘
                                                  │
                    ┌─────────────────────────────┴─────────────────────────────┐
                    ▼                                                           ▼
         Variant A (Control / 50%)                                   Variant B (Treatment / 50%)
    - TTL: 15s, Jitter: ±10%                                    - TTL: 60s, Adaptive Backoff
    - Options: Hard Quota (`max`)                               - Options: Priority Queue (`reserve`)
    - Feature: Standard Suite                                   - Feature: AI / GPU Acceleration Enabled
                    │                                                           │
                    └─────────────────────────────┬─────────────────────────────┘
                                                  ▼
                               ┌─────────────────────────────────────┐
                               │     Deterministic Client Router     │
                               │  Hash(licenseKey + machineId + salt)│
                               │  Bucket 0..49 ➜ A | 50..99 ➜ B      │
                               │  Sticky Session Invariant (Zero-Drift│
                               └─────────────────────────────────────┘
```

### 13.4.2 Deterministický Hashing a Rozdeľovanie do Bucketov (Bucketing)
1. **Bezstavovosť (Stateless Consistency)**: Priradenie klienta do variantu nesmie vyžadovať dodatočný zápis do databázy pri každom checkoute.
2. **Algoritmus**:
   $$\text{Bucket} = \text{Murmur3}\big(\text{LicenseKey} \mathbin{\Vert} \text{MachineId} \mathbin{\Vert} \text{ExperimentSalt}\big) \pmod{100}$$
3. **Sticky Session Invariant**: Pokiaľ klient raz vstúpi do experimentu, počas celej doby trvania leasingu (checkout ➜ heartbeat ➜ renew ➜ release) je garantované, že zostáva v identickom variante. Nedochádza k žiadnemu prepínaniu (flapping/drift).
4. **Segmentácia a Cieľové Skupiny (Targeting)**: Možnosť obmedziť experiment na špecifické verzie SDK, konkrétnych zákazníkov (tenants), operačné systémy alebo IP rozsahy.

### 13.4.3 Podporované typy experimentov
1. **Protokolové a sieťové experimenty**:
   - Porovnanie výkonu rôznych dĺžok lease TTL (napr. 15s vs. 60s) na databázové zaťaženie a latenciu pripojenia.
   - Testovanie nových stratégií zotavenia po výpadku siete (grace period vs. exponential backoff).
2. **Licenčné politiky a Options Rules (FLT-23..25)**:
   - Testovanie dopadu striktných zákazov (`deny`) a skupinových kvót (`max`) oproti mäkkým prioritným radom (`priority-queue`).
   - Sledovanie miery odmietnutia prístupu (denial rate) v oboch kohortách.
3. **Packaging, Entitlements & Cenové modely**:
   - Sprístupnenie prémiových modulov (napr. GPU akcelerácia, pokročilý export) vybraným 20 % inštalácií a sledovanie vyťaženia a konverzií.

### 13.4.4 Reálne testovanie A/B testovaním (Validation & Real-World Experimentation Framework)
A/B testovací engine v Symbolone nie je len teoretický návrh — jeho súčasťou je **reálny validačný a testovací framework**, ktorý testuje samotné A/B experimentovanie pri reálnej záťaži:
- **Automatizovaný testovací harness (`tests/Symbolon.ControlPlane.Tests/ExperimentationTests.cs`)**:
  - Simulácia **10 000 paralelných virtuálnych klientov** generujúcich checkouts a heartbeats.
  - **Chi-Square ($\chi^2$) Goodness-of-Fit Test**: Štatistické overenie, že rozdelenie klientov medzi varianty A (50 %) a B (50 %) nevykazuje skreslenie a spĺňa $p > 0.05$.
  - **Zero-Drift Invariant Test**: Overenie, že ani jeden z 10 000 klientov nezmení priradený variant počas opakovaných obnovení lease tokenov.
  - **Chaos & Dynamická rekonfigurácia**: Zmena pomeru z 50/50 na 80/20 za behu servera bez výpadku alebo prerušenia bežiacich leasingov.
  - **Canary Circuit Breaker**: Automatické zrušenie experimentu a okamžitý fallback na bezpečný Variant A (<100 ms), ak chybovosť variantu B prekročí nastavený prah (napr. 1 % chybových stavov HTTP 5xx).
- **Multi-language integrácia (Python SDK / C# Client)**:
  - Klient transparentne prijíma experimentálne hlavičky `X-Symbolon-Experiment: exp_id=vB` a reportuje telemetrické dáta bez nutnosti zásahu do aplikačného kódu.

### 13.4.5 Štatistické vyhodnotenie a Web Dashboard
- Výpočet **Z-Score**, **p-hodnoty** a 95% konfidenčného intervalu v reálnom čase priamo v databáze / in-memory akumulátore.
- Vizualizácia v Retro FoxPro Web TUI:
  - Porovnávacie grafy vyťaženia, priemernej latencie checkoutu, počtu odmietnutí (denials) a chybovosti oboch variantov.
  - Tlačidlá okamžitého schválenia víťazného variantu (**Promote to 100%**) alebo núdzového zastavenia (**Rollback**).

---

## 13.5 Post-Quantum Era — Príprava Celého Riešenia na Post-Kvantovú Éru

### 13.5.1 Prechod z hybridného režimu na čisté PQC
Symbolon bol od počiatku navrhnutý s ohľadom na dlhovekosť licencií (perpetual licencie s platnosťou 10–20 rokov). Zatiaľ čo doterajšie verzie implementovali hybridný profil `hybrid-v1` (kombinácia NIST P-256 ECDSA + ML-DSA-65), príprava celého riešenia na **post-kvantovú éru (Post-Quantum Era)** zahŕňa kompletnú sadu štandardov FIPS publikovaných v rokoch 2024–2026.

```
┌─────────────────────────────────────────────────────────────────────────────────────────┐
│                          SYMBOLON POST-QUANTUM CRYPTO SUITE                             │
├──────────────────────────────┬──────────────────────────────┬───────────────────────────┤
│    Digitálne Podpisy (DS)    │  Key Encapsulation (KEM)     │  Stavové / Hash Podpisy   │
│         FIPS 204             │         FIPS 203             │         FIPS 205          │
│        (ML-DSA-65)           │     (ML-KEM-768/1024)        │         (SLH-DSA)         │
├──────────────────────────────┼──────────────────────────────┼───────────────────────────┤
│ • Podpisy licencií (.symlic) │ • Hybrid TLS 1.3 / mTLS      │ • Dlhodobý Root of Trust  │
│ • Offline granty (.symgrant) │ • Šifrované licenčné payloady│ • Záložný algoritmus pri  │
│ • Lease tokeny (.symlease)   │ • Ochrana voči útokom HNDL   │   potenciálnom prelomení  │
│ • Disjunktné poverenia       │ • Kľúčová výmena Relay/Server│   mriežkovej kryptografie │
└──────────────────────────────┴──────────────────────────────┴───────────────────────────┘
```

### 13.5.2 Kľúčové PQC piliere a komponenty

#### 1. FIPS 203: ML-KEM (Module-Lattice-Based Key-Encapsulation Mechanism)
- **Ochrana pred Harvest-Now-Decrypt-Later (HNDL)**: Útočníci v súčasnosti ukladajú zašifrovanú sieťovú prevádzku s cieľom prelomiť ju po príchode kvantového počítača. Symbolon nasadzuje ML-KEM pre všetky šifrované prenosy.
- **Varianty**:
  - `ML-KEM-768`: Predvolená voľba (bezpečnostná úroveň ekvivalentná AES-192, optimálny pomer rýchlosti a veľkosti šifrového textu 1088 bajtov).
  - `ML-KEM-1024`: Maximálna bezpečnostná úroveň (ekvivalentná AES-256) pre armádne a vládne inštalácie.
- **Hybridný TLS 1.3 / mTLS**: Kľúčová výmena `X25519MLKEM768` pre komunikáciu medzi Client SDK, on-premise Relay servermi a centrálnym Control Plane.

#### 2. FIPS 204: ML-DSA (Module-Lattice-Based Digital Signature Algorithm)
- Primárny podpisový algoritmus pre všetky Symbolon artefakty (`.symlic`, `.symgrant`, `.symlease`, `.symrl`).
- Podpora pre **čisté JWS General JSON formáty bez klasickej zložky** (`alg: "ML-DSA-65"` podľa RFC 9964).
- Podpora pre HW akceleráciu s vektorovými inštrukciami AVX-512 na procesoroch x86_64 a ARM Neon na ARM64.

#### 3. FIPS 205: SLH-DSA (Stateless Hash-Based Digital Signature Algorithm / SPHINCS+)
- **Matematická poistka (Lattice Independence)**: ML-DSA aj ML-KEM sú založené na probléme učenia s chybami nad modulárnymi mriežkami (Module-LWE). Ak by v budúcnosti došlo k teoretickému matematickému prielomu v riešení mriežkových problémov, Symbolon má pripravenú okamžitú alternatívu: SLH-DSA.
- SLH-DSA stavia výlučne na bezpečnosti kolízií a jednosmernosti hashovacej funkcie (SHA-256 / SHAKE-256). Používa sa predovšetkým pre koreňové autority (Offline Root CA) a revokačné zoznamy s dlhou životnosťou.

#### 4. Kryptografický profil `pqc-strict` (Zero Classical Cryptography)
- Symbolon definuje dva kryptografické profily:
  1. `hybrid-v1` (predvolený): Koexistencia klasických algoritmov (ES256) a post-kvantových (ML-DSA-65) pre hladkú spätnú kompatibilitu so staršími operačnými systémami.
  2. `pqc-strict`: **Striktný režim**. Úplné vyradenie klasických asymetrických algoritmov (žiadne RSA, žiadne ECDSA P-256, žiadny klasický Diffie-Hellman). Prijímané a vystavované sú výhradne PQC podpisy.
- Spĺňa požiadavky **CNSA 2.0 (Commercial National Security Algorithm Suite 2.0)** nariadené vládou USA a smernice EÚ **NIS 2 / NIS Cooperation Group PQC Roadmap** (prechod kritickej infraštruktúry do roku 2030).

#### 5. Crypto-Agility Framework & Bezvýpadková rotácia
- Architektúra `Symbolon.Crypto` je postavená na rozhraní `ISignatureProvider` a `IKeyEncapsulationProvider`.
- Umožňuje bezvýpadkovú migráciu kľúčov: server môže publikovať JWKS obsahujúci hybridné aj čisto post-kvantové kľúče súčasne, pričom verifikátor v klientskom SDK automaticky vyberá najsilnejšiu dostupnú kryptografickú sadu.

#### 6. Vstavaný Quantum Vulnerability & Readiness Scanner (`symbolon pqc scan`)
- CLI a Web nástroj určený pre bezpečnostných audítorov a správcov licencií:
  ```bash
  symbolon pqc scan --server http://cp.symbolon.internal --out pqc-report.json
  ```
- **Funkcionalita skenera**:
  - Inšpekcia všetkých aktívnych licencií, tokenov a kľúčov v databáze.
  - Detekcia zraniteľných algoritmov (RSA < 3072, ECC P-256 bez PQC zložky, klasický TLS handshake).
  - Výpočet **PQC Readiness Indexu (0 – 100 %)**.
  - Automatické generovanie zoznamu artefaktov, ktoré vyžadujú preregistráciu alebo opätovné podpísanie post-kvantovými kľúčmi.

#### 7. Integrácia s hardvérovými modulmi (HSM & PKCS#11 v3.2)
- Podpora pre moderné HSM moduly podporujúce štandard PKCS#11 v3.2 a natívne PQC algoritmy (Utimaco, Thales Luna, AWS CloudHSM, Azure Dedicated HSM).
- Bezpečné ukladanie súkromných kľúčov ML-DSA a SLH-DSA v tamper-resistant hardvéri s certifikáciou FIPS 140-3 Level 3/4.

---

[← 12. OSS governance a monetizácia](12-oss-governance.md) · [Obsah](README.md) · [14. Otvorené otázky — rozhodnutia, ktoré musíš urobiť ty →](14-otvorene-otazky.md)
