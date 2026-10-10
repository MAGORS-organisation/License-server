# Achilles Enterprise Floating License Server — Komplexná Používateľská a Administrátorská Príručka

> **Verzia:** 1.0 (Achilles Enterprise Release)  
> **Jazyk:** Slovenský (Slovak)  
> **Cieľová skupina:** Systémoví administrátori, DevOps inžinieri, bezpečnostní audítori a softvéroví vývojári (ISV).

---

## Obsah Príručky

1. [Úvod a Celková Architektúra Systému](#1-úvod-a-celková-architektúra-systému)
2. [Plávajúce Licencie a Lízingový Mechanizmus (Floating Engine)](#2-plávajúce-licencie-a-lízingový-mechanizmus-floating-engine)
3. [Licenčný Rad a Prioritná Alokácia (Queue FLT-31)](#3-licenčný-rad-a-prioritná-alokácia-queue-flt-31)
4. [Post-Kvantová Kryptografia (PQC M7) a Správa Kľúčov](#4-post-kvantová-kryptografia-pqc-m7-a-správa-kľúčov)
5. [On-Premise Relay, Air-Gap a Poverenia (.symreq / .symgrant)](#5-on-premise-relay-air-gap-a-poverenia-symreq--symgrant)
6. [Hardvérový Odtlačok (Fingerprint) a Node-Locking](#6-hardvérový-odtlačok-fingerprint-a-node-locking)
7. [eBPF Kernel Socket Enforcement (Ochrana v Jadre Linuxu)](#7-ebpf-kernel-socket-enforcement-ochrana-v-jadre-linuxu)
8. [Multi-Region Geo-Replikácia a Active-Active CRDT](#8-multi-region-geo-replikácia-a-active-active-crdt)
9. [Podnikové Identity (SCIM 2.0) a Single Sign-On (SSO)](#9-podnikové-identity-scim-20-a-single-sign-on-sso)
10. [Správa Kľúčov (Cloud KMS, Hardware HSM a Shamir Secret Sharing)](#10-správa-kľúčov-cloud-kms-hardware-hsm-a-shamir-secret-sharing)
11. [Granulárne Moduly (Entitlements) a Balíkové Suity](#11-granulárne-moduly-entitlements-a-balíkové-suity)
12. [Zásady a Pravidlá Politík (Options Engine FLT-24)](#12-zásady-a-pravidlá-politík-options-engine-flt-24)
13. [Tokenové a Pay-As-You-Go Licencovanie](#13-tokenové-a-pay-as-you-go-licencovanie)
14. [Offline Roaming a Zapožičanie Licencií (Borrowing)](#14-offline-roaming-a-zapožičanie-licencií-borrowing)
15. [A/B Testovanie a Experimentačný Engine (AB-1 .. AB-15)](#15-ab-testovanie-a-experimentačný-engine-ab-1--ab-15)
16. [Webhooky, Notifikácie a Životný Cyklus Licencií](#16-webhooky-notifikácie-a-životný-cyklus-licencií)
17. [Zero-Config Discovery a Server Failover Pool](#17-zero-config-discovery-a-server-failover-pool)
18. [Migrácia z FlexNet Publisher (FLEXlm) a Keygen.sh](#18-migrácia-z-flexnet-publisher-flexlm-a-keygensh)
19. [Klientske SDK a Integrácie (6 Jazykov)](#19-klientske-sdk-a-integrácie-6-jazykov)
20. [Kompletný Prehľad CLI Príkazov (achilles)](#20-kompletný-prehľad-cli-príkazov-achilles)
21. [Konfigurácia Servera, Docker Compose a Kubernetes Helm](#21-konfigurácia-servera-docker-compose-a-kubernetes-helm)
22. [Dizajnové Témy a Klávesové Ovládanie (F1–F12)](#22-dizajnové-témy-a-klávesové-ovládanie-f1f12)

---

## 1. Úvod a Celková Architektúra Systému

### 1.1 Účel a Poslanie
**Achilles** je podnikový licenčný server určený pre nezávislých dodávateľov softvéru (ISV), ktorí distribuujú desktopové, serverové, priemyselné alebo HPC aplikácie vyžadujúce striktné riadenie súbehu sedadiel (floating licensing), vysokú dostupnosť a post-kvantovú bezpečnosť.

### 1.2 Porovnanie s Tradičnými Systémami
| Vlastnosť | FlexNet Publisher (FLEXlm) | Tradičné Cloud Licencory | Achilles Enterprise |
|---|---|---|---|
| **Prideľovanie sedadiel** | Súborové zámky / lmgrd daemony | Redis locky / distribuovaný konsenzus | Deterministické $O(1)$ `FOR UPDATE SKIP LOCKED` |
| **Kryptografia** | Symetrické šifry / CRC / RSA 1024 | ECDSA P-256 | Hybridné NIST P-256 + ML-DSA-65 (FIPS 204 PQC) |
| **Air-Gap podpora** | Hardvérové USB dongle kľúče | Iba obmedzený offline token | Podpísané poverenia `.symreq` / `.symgrant` |
| **Ochrana jadra** | Žiadna (len userspace DLL hooky) | Žiadna | eBPF socket enforcement priamo v Linux jadre |
| **Multi-region** | Manuálne triady s výpadkami | Latentné cloudové replikácie | Active-Active PN-Counter CRDT bez zámkov |

---

## 2. Plávajúce Licencie a Lízingový Mechanizmus (Floating Engine)

### 2.1 Model Materializovaných Sedadiel
Na rozdiel od počítadiel v pamäti má každá licencia v databáze PostgreSQL 17 / SQLite materializované riadky pre každé jednotlivé sedadlo. Transakcia checkoutu prebieha v konštantnom čase:
```sql
SELECT id, seat_number 
FROM seats 
WHERE license_id = @licenseId AND status = 'available'
LIMIT 1
FOR UPDATE SKIP LOCKED;
```
Týmto spôsobom je **vylúčený race-condition a prečerpanie (over-allocation)** aj pri súbežnom nápore desiatok tisíc požiadaviek za sekundu.

### 2.2 Heartbeat Životný Cyklus a Jitter
- **Checkout:** Klient zavolá `POST /v1/leases`. Získa podpísaný JWS token s platnosťou $T = 300\text{ s}$.
- **Renewal (Heartbeat):** Klientske SDK spúšťa periodický task v intervale $T / 2$ s náhodným rozptylom $\pm 10\%$ (adaptívny jitter chrániaci pred thundering herd).
- **Expirácia:** Ak klient prestane komunikovať (havária, pád OS), sedadlo po vypršaní TTL automaticky expiruje a vráti sa do fondu voľných sedadiel.
- **Uvoľnenie (Release):** Pri riadnom ukončení aplikácie zavolá SDK `DELETE /v1/leases/{id}`, čo sedadlo okamžite uvoľní.

---

## 3. Licenčný Rad a Prioritná Alokácia (Queue FLT-31)

V prípade, že sú všetky sedadlá vyčerpané, klient neobdrží tvrdú chybu, ale je zaradený do prioritnej čakacej fronty (`POST /v1/queue/join`).
- **VIP váhovanie:** Klienti majú priradené váhy (napr. výpočtový cluster váha 100, bežný inžinier váha 50, testovací skript váha 10).
- **Auto-Reaping:** `QueueReaperBackgroundService` každých 30 sekúnd vyčistí lístky, ktorých držitelia prestali posielať polling požiadavky.
- **Okamžitá notifikácia:** Hneď ako sa sedadlo uvoľní, systém pridelí sedadlo lístku na čele radu s vyhradeným časom na prevzatie.

---

## 4. Post-Kvantová Kryptografia (PQC M7) a Správa Kľúčov

### 4.1 Hybridné Podpisovanie
Dokument licencie (`.symlic`) a lízingový token (`.symlease`) obsahujú dvojitý nezávislý digitálny podpis:
1. **ES256 (NIST P-256):** Overiteľný ľubovoľným existujúcim klientskym zariadením a W3C WebCrypto API.
2. **ML-DSA-65 (FIPS 204):** Mriežkový post-kvantový podpis odolný voči kvantovým počítačom.

### 4.2 Režimy Kryptografického Profilu
- `hybrid-v1` (predvolené): Koexistencia ES256 a ML-DSA-65.
- `pqc-strict`: Výlučne čisté post-kvantové algoritmy ML-DSA-65 bez akejkoľvek klasickej eliptickej krivky (určené pre CNSA 2.0 a armádne normy).

---

## 5. On-Premise Relay, Air-Gap a Poverenia (.symreq / .symgrant)

Pre prostredia bez prístupu na internet (jadrové elektrárne, podzemné bane, výrobné haly) funguje protokol delegovanej kapacity:
1. Lokálny Relay uzol vygeneruje `.symreq` so zoznamom požadovaných sedadiel.
2. Správca prenesie `.symreq` na USB kľúči do webového portálu Achilles.
3. ControlPlane vystaví podpísané poverenie `.symgrant` s disjunktným rozsahom `[SeatFrom..SeatTo]`.
4. Relay uzol importuje `.symgrant` a obsluhuje lokálnu sieť LAN 100% autonómne.

---

## 6. Hardvérový Odtlačok (Fingerprint) a Node-Locking

Zber hardvérového fingerprintu prebieha deterministicky cez 6 nezávislých vrstiev:
- `machineId` (Systémové GUID OS)
- `board` (Sériové číslo základnej dosky)
- `cpu` (Procesorové CPUID a revízia)
- `disk` (Fyzický NVMe/SSD radič)
- `mac` (Fyzická sieťová karta)
- `host` (Solený SHA-256 hash názvu stanice)

Matching stratégie podporujú toleranciu výmeny komponentov (`match-most` vyžaduje aspoň 51% zhodu). Pre kritické uzly sa využíva kryptografická verifikácia cez **TPM 2.0 PCR Quote**.

---

## 7. eBPF Kernel Socket Enforcement (Ochrana v Jadre Linuxu)

Pomocou moderného Linux BPF subsystému pripája Achilles BPF CO-RE programy na socketové háky `cgroup/connect4` a `cgroup/connect6`.
Každé odchádzajúce sieťové spojenie chráneného procesu je v jadre overené voči BPF mape `license_map`. Ak proces nemá platný lízing, jadro spojenie zhodí v čase pod 200 nanosekúnd.

---

## 8. Multi-Region Geo-Replikácia a Active-Active CRDT

Globálne inštalácie využívajú bezkolízny dátový typ **PN-Counter CRDT (Conflict-Free Replicated Data Type)** a kauzálne vektorové hodiny (Vector Clocks).
Každý región (EU, US, AP) prideľuje sedadlá lokálne bez distribuovaného zámku. Pri sieťových výpadkoch nastáva automatické zliatie (partition healing) bez straty integrity.

---

## 9. Podnikové Identity (SCIM 2.0) a Single Sign-On (SSO)

- **SCIM 2.0:** Štandardné koncové body `/scim/v2/Users` a `/scim/v2/Groups` pre synchronizáciu s Okta, Microsoft Entra ID a PingFederate.
- **Okamžitý Deprovisioning:** Deaktivácia zamestnanca v IdP okamžite uvoľní všetky jeho plávajúce sedadlá a zruší čakacie lístky.
- **SAML 2.0 a OIDC PKCE:** Zabezpečené prihlasovanie administrátorov a operátorov s mapovaním rolí (`admin:super`, `admin:tenant`, `auditor`).

---

## 10. Správa Kľúčov (Cloud KMS, Hardware HSM a Shamir Secret Sharing)

- **3-Úrovňová Hierarchia:** Tier 1 (Offline Root), Tier 2 (KMS/HSM Intermediate Authority), Tier 3 (Efemérny kľúč uzla).
- **Shamir's Secret Sharing:** Rozdelenie master kľúča na $n$ častí s prahom $k$ nad konečným poľom $GF(2^8)$. Pre obnovu stačí $k$ ľubovoľných častí.

---

## 11. Granulárne Moduly (Entitlements) a Balíkové Suity

Podpora nezávislých kvót pre add-ony (napr. CAD Designer má 50 sedadiel, ale FEA Solver len 5). Klientske SDK podporujú RAII vzor:
```csharp
await using var feature = await lease.UseFeatureAsync("FEA_SOLVER");
// Výpočet... po opustení bloku sa modul automaticky uvoľní!
```

---

## 12. Zásady a Pravidlá Politík (Options Engine FLT-24)

Pravidlá sa vyhodnocujú v prísnom poradí:
1. `deny` — Okamžité zamietnutie (napr. hosťovské podsiete CIDR `192.168.100.0/24`).
2. `max` — Maximálny limit sedadiel pre skupinu.
3. `reserve` — Garantované vyhradené sedadlá pre kľúčové tímy.
4. `priority` — Prioritné poradie v rade.

---

## 13. Tokenové a Pay-As-You-Go Licencovanie

Pre výpočtovo náročné jednorazové úlohy (simulácie, AI inferencie) slúži tokenový motor s dvojfázovým potvrdením (2PC):
1. `reserve` (zablokovanie kreditov v peňaženke)
2. `heartbeat` (predlžovanie rezervácie počas behu)
3. `commit` (zaúčtovanie skutočnej spotreby) alebo `rollback` (vrátenie pri zlyhaní výpočtu).

---

## 14. Offline Roaming a Zapožičanie Licencií (Borrowing)

Zapožičanie plávajúceho sedadla na 1 až 30 dní vygeneruje podpísaný súbor `.symlease`.
Predčasné vrátenie sedadla je chránené kryptografickou výzvou (Challenge-Response nonce) s podpisom efemérneho privátneho kľúča (Proof-of-Possession).

---

## 15. A/B Testovanie a Experimentačný Engine (AB-1 .. AB-15)

Deterministický bezstavový Murmur3 hash priraďuje klientov do variantov bez nutnosti ukladania stavu v databáze.
Systém obsahuje živé štatistické vyhodnocovanie (Z-score, p-hodnota, $\chi^2$) a automatický circuit breaker, ktorý do 100 ms odpojí neúspešný experimentálny variant.

---

## 16. Webhooky, Notifikácie a Životný Cyklus Licencií

Bezpečné HTTP webhooky s HMAC-SHA256 podpisom a formátovaním pre Slack a Microsoft Teams. Vstavaná Dead-Letter Queue (DLQ) umožňuje opakované odoslanie neúspešných správ.

---

## 17. Zero-Config Discovery a Server Failover Pool

Automatické vyhľadávanie licenčných serverov cez UDP port 7584 (`0x1D90`).
Klientske SDK automaticky prepínajú na záložné inštancie pri výpadkoch siete a rešpektujú premenné prostredia `ACHILLES_LICENSE_SERVER` a `ACHILLES_SERVERS`.

---

## 18. Migrácia z FlexNet Publisher (FLEXlm) a Keygen.sh

Plnohodnotný migračný transpiler dekóduje súbory `license.dat`, transpiluje súbory `options.opt`, analyzuje logy `lmgrd.log` a importuje dáta z cloudovej platformy Keygen.sh.

---

## 19. Klientske SDK a Integrácie (6 Jazykov)

Podporované sú oficiálne knižnice s automatickým obnovovaním sedadiel, RAII vzorom a offline validáciou:
- **C# (.NET 10 LTS):** NuGet balíček `Achilles.Client`
- **Python (3.10+):** `pip install achilles-client`
- **Rust (2021):** Crate `achilles-client` (Tokio async)
- **C99 / C++17:** Knižnica `achilles.h` / `libachilles`
- **Go:** Modul `github.com/achilles/client`
- **WebAssembly / TypeScript:** NPM balíček `@achilles/validator` (100% offline W3C WebCrypto validácia)

---

## 20. Kompletný Prehľad CLI Príkazov (achilles)

| Príkaz | Popis |
|---|---|
| `achilles setup` | Interaktívny inštalačný sprievodca. |
| `achilles key gen` | Vygenerovanie páru podpisových kľúčov. |
| `achilles lic issue` | Vydanie licenčného súboru `.symlic`. |
| `achilles lic inspect` | Inšpekcia a verifikácia licencie. |
| `achilles grant request` | Vytvorenie Air-Gap žiadosti `.symreq`. |
| `achilles grant issue` | Schválenie žiadosti a vydanie poverenia `.symgrant`. |
| `achilles pqc scan` | Audit kvantovej zraniteľnosti a PQC Readiness Index. |
| `achilles ebpf status` | Diagnostika BPF filtrov na Linuxe. |
| `achilles doctor` | Diagnostika prostredia a dostupnosti služieb. |
| `achilles sbom` | Export CycloneDX v1.6 SBOM pre Cyber Resilience Act. |
| `achilles verify-artifact` | Kontrola integrity a podpisu binárky. |

---

## 21. Konfigurácia Servera, Docker Compose a Kubernetes Helm

### Docker Compose Spustenie
```bash
docker compose up -d --build
```
Spustí PostgreSQL 17, Achilles ControlPlane (`:8080`), Achilles Relay (`:8081`), Prometheus (`:9090`) a Grafana (`:3000`).

### Kubernetes Helm Nasadenie
```bash
helm install achilles deploy/helm/achilles/ -n licensing --create-namespace
```

---

## 22. Dizajnové Témy a Klávesové Ovládanie (F1–F12)

### Štyri Grafické Témy
1. **💼 Modern Enterprise:** Čisté profesionálne rozhranie pre cloudové platformy.
2. **💾 DOS (FoxPro 2.6 TUI):** Autentické textové rozhranie DOS s kaskádovými oknami a tieňmi.
3. **⚡ Cyberpunk 2077 HUD:** Neónový Night City štýl so sci-fi kybernetickými akcentmi.
4. **🍎 Apple iOS Glassmorphism:** Human Interface s frosted glass efektom a automatickým trojrežimovým systémom (Systémový Auto / Denný Light / Nočný Dark).

### Rýchle Klávesové Skratky
- `F1` — Otvorenie tejto Príručky a Nápovedy.
- `F2` — Prehľad (Overview).
- `F3` — Licencie.
- `F4` — Kľúče & PQC.
- `F5` — Relay uzly.
- `F6` — Auditný denník.
- `F7` — Air-Gap portál.
- `F8` — Webhooky.
- `F9` — Súlad CRA & Diagnostika.
- `F10 / Alt` — Aktivácia hlavného menu.
- `V` — Vystavenie novej licencie.
- `O / C` — Konfigurácia API kľúča.
- `/` — Zameranie vyhľadávania v príručke.
- `Esc` — Návrat / Zatvorenie okna.
