# Zoznam Zmien (Changelog)

Všetky podstatné zmeny v projekte **Symbolon Enterprise Floating License Server** sú dokumentované v tomto súbore.

Formát vychádza zo špecifikácie [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)
a projekt striktne dodržiava [Sémantické Verziovanie (Semantic Versioning 2.0.0)](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

### Pridané (Added)
- **Multi-Region Geo-Replication (Active-Active CRDT)**:
  - **Kauzálne Vektorové Hodiny (`VectorClock`)**: Deterministické rozlišovanie kauzálnych a súbežných zmien naprieč geografickými regiónmi (`eu-central-1`, `us-east-1`, `ap-southeast-1`).
  - **Stavový PN-Counter CRDT (`PnCounter`)**: Komutatívne, asociatívne a idempotentné zlučovanie globálnych kapacít a alokovaných sedadiel bez potreby centrálneho distribuovaného zámku.
  - **Disjoint Seat Allocation & LWW-OR-Set (`GeoReplicationEngine`)**: Autonómne bezkolízne prideľovanie sedadiel v každom regióne aj počas úplného sieťového rozpadu (network partition) s automatickým uzdravením (partition healing).
  - **Kryptografická Medziklastrová Autentifikácia (`ReplicationSecurity`)**: Overovanie replikačných správ pomocou HMAC-SHA256 s ochranou proti replay útokom cez časové okno (anti-replay window).
  - **HTTP Replikácia API (`ReplicationEndpoints`)**: Endpointy `POST /v1/replication/sync`, `GET /v1/replication/status` a `POST /v1/replication/peers` pre výmenu deliet.
  - **CLI Príkaz `symbolon cluster`**: `symbolon cluster status` pre zobrazenie topológie, vektorových hodín a CRDT kapacít; `symbolon cluster sync` pre manuálny on-demand delta sync.
  - **Retro Web TUI Integrácia**: Nová vizualizačná karta Active-Active Geo-Replikácie v zobrazení `view-mesh` zobrazujúca vektorový čas a stav jednotlivých regiónov v reálnom čase.
- **eBPF Kernel Socket Enforcement (Linux)**:
  - **Linux BPF Program (`ebpf/symbolon_sock_filter.bpf.c`)**: Nízkoúrovňový BPF CO-RE C program pripájaný k `cgroup/connect4` a `cgroup/connect6` pre striktnú kontrolu socketových spojení priamo na úrovni Linux jadra.
  - **BPF Mapy & Ring Buffer Udalostí (`license_map`, `events_ringbuf`)**: Kernel hash mapa pre overovanie aktívnych leasingov a ring buffer pre asynchrónny zber bezpečnostných incidentov.
  - **Okamžité Blokovanie Neoprávnených Spojení (`-EPERM`)**: Deterministické zablokovanie sieťovej prevádzky aplikácií a procesov bez platnej licencie alebo po okamžitej revokácii.
  - **Jadrový Engine a Emulátor (`EbpfEnforcementEngine`)**: Správa životného cyklu cgroups, portových filtrov a deterministická simulácia pre cross-platform testovanie (Linux, Windows, macOS).
  - **HTTP Endpoints (`EbpfEndpoints`)**: Endpointy `GET /v1/system/ebpf/status`, `GET /v1/system/ebpf/violations`, `POST /v1/system/ebpf/attach` a `detach`.
  - **CLI Nástroje `symbolon ebpf`**: Príkazy `symbolon ebpf status`, `symbolon ebpf attach` a `symbolon ebpf violations`.
  - **Retro TUI Web Dashboard**: Nová monitorovacia karta v `view-mesh` zobrazujúca režim ovládača, chránené cgroups, aktívne BPF položky a zablokované spojenia v reálnom čase.

## [1.0.0] - 2026-09-22

Prvé oficiálne vydanie produkčnej platformy Symbolon — podnikový open-source licenčný server s podporou vysoko-dostupných plávajúcich (floating) licencií, post-kvantovej kryptografie (PQC), on-premise relay uzlov, air-gap sietí a Zero-Trust klastrov.

### Pridané (Added)

#### Kryptografické Jadro & Špecifikácia Formátov (`Symbolon.Crypto`, `Symbolon.Format`)
- **Hybridné Podpisovanie (PQC-Ready)**: Duálne nezávislé podpisovanie kombinujúce klasickú eliptickú kryptografiu **ECDSA P-256 (ES256)** podľa NIST FIPS 186-5 a post-kvantovú schému **ML-DSA-65 (FIPS 204)** chrániacu pred hrozbami kvantových počítačov.
- **Formát Licencií `symlic/1`**: Štruktúrovaný formát licenčných dokumentov založený na JWS General JSON Serialization (RFC 7515) a JWKS (RFC 9964) zabalený v čitateľnej ASCII PEM obálke.
- **Crockford Base32 Licenčné Kľúče**: Generovanie a validácia kľúčov v tvare `SYM-XXXX-XXXX-XXXX-XXXX-XXXX` s integrovaným kontrolným súčtom **CRC-32C (Castagnoli)**.
- **Správa Kľúčov (`SymbolonKeyRing`)**: Bezpečný, vláknovo bezpečný správca podpisových kľúčov s podporou okamžitej revokácie a overovania integrity.
- **WebAssembly (WASM) Offline Validátor**: Ľahké overovacie jadro (`WasmLicenseValidator`) kompilovateľné do WASM pre offline overovanie licencií v prehliadačoch a Node.js bez kontaktu so serverom.
- **OCI Cryptographic License Bundles**: Balenie licencií a JWKS kľúčov do štandardizovaného formátu OCI Image Manifest v1 (`application/vnd.oci.image.manifest.v1+json`) pre air-gapped Kubernetes klastre a privátne kontajnerové registre (`OciLicenseBundle`).
- **Hardware Enclave Attestation (R6)**: Podpora overovania hardvérových citácií TPM 2.0 PCR (Platform Configuration Register) a Confidential Computing Enclaves (Intel SGX / AMD SEV-SNP) chránená challenge-response mechanizmom proti replay útokom (`TpmQuoteVerifier`, `TpmQuoteGenerator`).

#### Distribuovaný Konsenzus & Doménová Logika (`Symbolon.Domain`, `Symbolon.Data`, `Symbolon.Protocol`)
- **Materializované Sedadlá v O(1)**: Deterministické prideľovanie a uvoľňovanie plávajúcich sedadiel pomocou `FOR UPDATE SKIP LOCKED` v PostgreSQL 17 a SQLite bez potreby distribuovaných zámkov (Redis/Raft).
- **Kryptograficky Reťazený Auditný Denník**: Nemenný audit ledger (`IAuditLedger`, `EfAuditLedger`) zreťazený cez SHA-256 (`SHA256(prev_hash + data)`).
- **Menovaní Používatelia & Rezervácie**: Podpora menovaných používateľov a skupín podľa vzoru FlexNet Options (`INCLUDE`, `EXCLUDE`, `GROUP`, `RESERVE`).
- **Metered Kvóty**: Sledovanie a odčítavanie spotreby kreditových jednotiek (entitlement quotas) v reálnom čase.
- **Prioritný Rad Požiadaviek**: FIFO rad s voliteľnou prioritizáciou pri vyčerpaní sedadiel a automatickým checkoutom po uvoľnení kapacity.
- **gRPC / Protobuf Streaming Heartbeat**: Obojsmerný streamovací kanál (`symbolon_lease.proto`, `LeaseStream.cs`) znižujúci réžiu siete o 85 % oproti bežnému HTTP/1.1 REST protokolu.
- **Decentralizovaný Relay Mesh Koordinátor**: P2P synchronizácia delegovaných kapacít medzi autonómnymi Relay uzlami pomocou **Lamportových logických hodín** a disjunktných intervalov sedadiel (`RelayMeshCoordinator`).
- **Air-Gap Delegovaný Portál**: Výmena offline licenčných súborov (`.symreq` → `.symgrant`) cez USB prenos s povinným overením hash-chain uzla `usageDigest`.
- **Hardvérový Fingerprint**: Deterministický zber a kanonický hash hardvérových komponentov (CPU, MAC, SMBIOS, OS).

#### Viacjazyčné Klientske SDK (`sdk/`)
- **.NET 10 LTS SDK (`Symbolon.Client`)**: Plnohodnotný asynchrónny klient s automatickým vláknom pre heartbeat obnovu, adaptívnym jitterom ±10 %, automatickým retry s exponenciálnym backoffom a elegantným RAII uvoľnením (`IAsyncDisposable`).
- **Python SDK (`sdk/python/symbolon`)**: Python knižnica s kontextovým manažérom (`with client.acquire_seat(...)`), automatickým vláknom obnovy a overovaním JWK podpisov.
- **Rust SDK (`sdk/rust/symbolon-rs`)**: Pamäťovo bezpečný klient bez dynamických alokácií na kritickej ceste.
- **C/C++ SDK (`sdk/c_cpp/libsymbolon`)**: Natívna C knižnica pre integráciu do CAD aplikácií, herných enginov a desktopových programov.
- **WebAssembly JavaScript SDK (`sdk/wasm/symbolon-validator.js`)**: Modul pre webové a hybridné aplikácie.

#### Webové Rozhranie Control Plane & Retro TUI (`Symbolon.ControlPlane`)
- **Jednookenné Pracovné Prostredie**: Celá správa servera prebieha v jedinom okne bez nežiaducich vyskakovacích okien.
- **Retro FoxPro / DOS TUI Vzhľad**: Bridlicové pozadie, kaskádové prekrývajúce sa okná, sýte pravouhlé tiene (`box-shadow: 10px 10px 0 #000`), žlté akcelerátory a azúrové zvýraznenie.
- **100% Klávesnicové Ovládanie**: Kompletná navigácia cez šípky `↑/↓/←/→`, `Enter`, `Escape`, `Tab`, priame písmenové skratky (hotkeys `V`, `L`, `K`, `R`, `M`, `A`, `G`, `S`, `T`, `O`) a funkčné klávesy `F1`–`F10`.
- **Živá Info Lišta Systémových Zdrojov**: Priebežný monitoring v reálnom čase na spodku okna zobrazujúci využitie **CPU %**, **RAM MB**, **DISK MB**, **INTERNET KB/s**, **Server IP**, **Prihláseného používateľa** a **Serverový čas**.
- **Merkle Tree Transparency Log**: Deterministický Merkle strom (RFC 6962) s matematickým overovaním inkluzívnych dôkazov (`/v1/transparency/inclusion` a `/v1/transparency/verify`).
- **Podnikový Alerting & Anomálie**: Detekcia vyčerpania kapacity ($\ge 90\%$), denial spikes a bezpečnostných incidentov s notifikáciami cez webhooks (`IAlertService`).
- **Outbound Webhook Engine**: Asynchrónne doručovanie notifikácií s HMAC-SHA256 podpisom v hlavičke `X-Symbolon-Signature`.

#### Príkazový Riadok CLI (`Symbolon.Cli`)
- **Interaktívny TUI Sprievodca**: Sprievodca inštaláciou a konfiguráciou (`symbolon setup`) cez Spectre.Console.
- **Kryptografické Príkazy**: `symbolon keys generate`, `symbolon keys export-jwks`.
- **Správa Licencií**: `symbolon license keygen`, `symbolon license issue`, `symbolon license inspect`.
- **Migračné Nástroje**: `symbolon import`, `symbolon export` pre prechod z FlexNet Publisher a Keygen.sh.
- **Súlad CRA & SBOM**: Generovanie CycloneDX v1.6 SBOM (`symbolon sbom`) a overovanie kontrolných súčtov binárok (`symbolon verify-artifact`).
- **Cloud-Native OCI & Mesh**: Balenie a verifikácia OCI balíčkov (`symbolon oci pack`, `symbolon oci verify`), zobrazenie konsenzu klastra (`symbolon mesh status`) a overenie TPM 2.0 citácií (`symbolon attestation verify`).
- **Diagnostika**: Kontrola prostredia, kryptografie a pripojenia (`symbolon doctor`).

#### Legislatívny a Bezpečnostný Súlad
- **EU Cyber Resilience Act (CRA - Nariadenie 2024/2847)**: Endpointy `/v1/compliance/cra` a strojovo čitateľný súlad.
- **CycloneDX v1.6 SBOM**: Endpoint `/v1/compliance/sbom` s kompletným zoznamom softvérových komponentov.

---

### Zmenené (Changed)

- Architektúra webového administračného rozhrania bola transformovaná na jednotné pracovné okno (Single Desktop Window Environment) s FoxPro vizuálom.
- Koordinátor distribuovaného konsenzu `RelayMeshCoordinator` bol presunutý do `Symbolon.Protocol` pre spoločné využitie v CLI, Relay aj Control Plane.
- Predvolené správanie autentifikácie API bolo zmenené na striktné bezpečné zlyhanie (fail-closed).
- Globálny rate limiter bol zmenený na partíciovaný podľa IP adresy klienta.

---

### Opravené (Fixed)

- **Súbeh pri prideľovaní sedadiel**: Vyriešený concurrency race condition pri paralelnom checkoute licencií odstránením nekonzistentných medzistavov.
- **Deadlock vo vlákne obnovy**: Opravená synchronizácia pozadového časovača v .NET a Python SDK pri obnove expirovaných lease tokenov.
- **Parsovanie ISO 8601 trvaní**: Opravená konverzia hodnôt časového posunu (clock skew tolerance) v `PolicyClaim`.
- **Uvoľňovanie prostriedkov**: Zabezpečené korektné uzatváranie streamovacích WebSocket a gRPC spojení pri zlyhaní siete.

---

### Bezpečnosť (Security)

V rámci nezávislého bezpečnostného auditu (Senior Cyber Security Auditor) boli identifikované a kompletne odstránené nasledujúce zraniteľnosti (SEC-01 až SEC-06):

- **SEC-01 (Insecure Default: Dev SuperAdmin Bypass)**: Odstránený nekontrolovaný bypass autentifikácie v `ApiKeyAuthenticationHandler.cs`. Server je teraz v predvolenej inštalácii striktne uzamknutý (fail-closed).
- **SEC-02 (32-bit Truncated Hash Collision)**: V `PublicEndpoints.cs` a `OfflineEndpoints.cs` bola zavedená striktná kontrola celého 256-bitového `KeyHash` pomocou `CryptographicOperations.FixedTimeEquals`, čím sa eliminovalo riziko kolíznych útokov na skrátený 32-bitový index.
- **SEC-03 (Server-Side Request Forgery - SSRF)**: Zavedený prísny validátor webhookových adries `WebhookSecurityValidator`, ktorý blokuje link-local adresy (AWS IMDS `169.254.0.0/16`), loopback (`127.0.0.0/8`), multicast a privátne siete RFC 1918 v produkčnom režime.
- **SEC-04 (Broken Object Level Authorization - BOLA)**: Zabezpečená striktná izolácia nájomcov (multi-tenancy boundary enforcement) v `AdminEndpoints.cs` a `ApiKeyEndpoints.cs`. Používatelia bez roly `admin:super` nemôžu čítať ani modifikovať dáta iných nájomcov.
- **SEC-05 (Denial of Service cez globálny Rate Limiter)**: Prechod z globálneho zdieľaného rate limitera na particionované limitery podľa IP adresy pripojenia klienta a identity volajúceho.
- **SEC-06 (Chýbajúce HTTP Security Headers)**: Zavedený bezpečnostný middleware vstrekujúci prísne hlavičky `Content-Security-Policy`, `X-Frame-Options: DENY`, `X-Content-Type-Options: nosniff`, `Referrer-Policy`, `Permissions-Policy` a `Strict-Transport-Security` (HSTS).

---

[1.0.0]: https://github.com/MAGORS-organisation/License-server/releases/tag/v1.0.0
