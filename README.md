# Achilles — Open-Source Cloud & On-Premise Licenčný Server

[![Platform](https://img.shields.io/badge/.NET-10.0%20LTS-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Language](https://img.shields.io/badge/C%23-14.0-239120?logo=csharp)](https://learn.microsoft.com/dotnet/csharp/)
[![Tests](https://img.shields.io/badge/tests-656%20passed%20(+53%20Python%2C%20+19%20Wasm%2C%20+13%20Java%2C%20+11%20Go%2C%20+6%20Rust)-brightgreen)](#výsledky-testovania)
[![License: AGPL-3.0](https://img.shields.io/badge/license-AGPL--3.0-blue.svg)](LICENSE)
[![License: Apache-2.0](https://img.shields.io/badge/client%20SDK-Apache--2.0-blue.svg)](LICENSES/Apache-2.0.txt)
[![CRA Compliant](https://img.shields.io/badge/CRA%20Compliance-EU%202024%2F2847-success)](SECURITY.md)
[![SBOM: CycloneDX v1.6](https://img.shields.io/badge/SBOM-CycloneDX%20v1.6-blue)](spec/README.md)
[![Crypto](https://img.shields.io/badge/cryptography-ES256%20%2B%20ML--DSA--65%20(PQC)-orange)](#kryptografia)

**Achilles** (pôvodne *Symbolon*) je moderný podnikový licenčný server navrhnutý pre nezávislých dodávateľov softvéru (ISV), ktorí predávajú softvér nasadzovaný v cloude, on-premise, na desktope, v priemyselných zariadeniach alebo v striktne izolovaných (air-gapped) prostrediach.

Rieši to, čo dnešné cloud-first platformy ponúkajú iba ako obmedzený doplnok: **vysokovýkonné plávajúce (concurrent / floating) licencie**, deterministické účtovanie sedadiel bez distribuovaných zámkov, lokálne on-premise relay uzly s delegovanou kapacitou, offline validáciu a **post-kvantovú kryptografiu** (FIPS 204).

👉 **[Zoznam Zmien (CHANGELOG.md)](CHANGELOG.md)**  
👉 **[Ucelený Integration Quickstart Guide (C#, Python, Rust, C/C++)](docs/quickstart-guide.md)**  
👉 **[Návod na Migráciu z FlexNet Publisher (FLEXlm)](docs/migracia-z-flexnetu.md)**  

---

## Kľúčové Vlastnosti a Inovácie

1. **Plávajúce (Floating) Sedadlá v O(1)**
   - Využíva vzor **materializovaných sedadiel** v PostgreSQL 17 / SQLite s `FOR UPDATE SKIP LOCKED`.
   - Garantuje presne 0 prečerpaných licencií (over-allocation) bez potreby Redis klastra alebo distribuovaného konsenzu (Raft/Paxos).
2. **Hybridná Post-Kvantová Kryptografia (PQC-Ready)**
   - Dvojité nezávislé podpisovanie: **ES256 (NIST P-256)** pre okamžitú kompatibilitu + **ML-DSA-65 (FIPS 204)** pre ochranu pred kvantovými útokmi („harvest now, decrypt later“).
   - Formát [`symlic/1`](spec/03-symlic-1.md) založený na JWS General JSON Serialization (RFC 7515) a JWKS (RFC 9964) zabalený v čitateľnej PEM obálke.
3. **Delegovaný Seat Grant pre On-Premise Relay**
   - Namiesto nestabilných FlexNet triadov používa **Delegated Seat Grant** (`.symgrant`).
   - Control plane podpíše lokálnemu relay serveru blok sedadiel na stanovený čas `T`. Relay funguje autonómne aj pri výpadku pripojenia do cloudu.
4. **Resilience & Idempotencia**
   - Heartbeat lease protokol s adaptívnym ±10% jitterom, zabraňujúcim synchronizovaným búrkam požiadaviek (thundering herd).
   - Ochrana proti posunu hodín (clock skew), monotonic lease sequence guard a okno vzkriesenia (`resurrectionWindow`).
5. **Kryptograficky Reťazený Audit Ledger**
   - Každá udalosť (checkout, renew, release, borrow, revoke) je zapísaná do nemenného auditného denníka zreťazeného cez `SHA-256(prev_hash + data)`.
6. **Produkčná Observabilita**
   - Vstavaný Prometheus `/metrics` exporter, Kubernetes liveness/readiness sondy a predpripravený Grafana dashboard.
7. **Bezpečnosť, mTLS & Správa Podpisových Kľúčov**
   - Striktná autentifikácia Relay uzlov pomocou API kľúčov a mTLS certifikátov zabraňujúca impersonácii.
   - Vstavaný ASP.NET Core Rate Limiting (Sliding Window & Fixed Window) chrániaci API pred zneužitím.
   - Bezvýpadková rotácia kryptografických kľúčov (`/admin/v1/keys/rotate`), okamžitá revokácia a dynamický JWKS filter garantujúci vylúčenie kompromitovaných kľúčov.
8. **Autentický Retro FoxPro / DOS TUI Web Dashboard (Čisto Klávesnicové Ovládanie)**
   - Vstavané webové rozhranie na `http://localhost:8080/` s vernou estetikou DOS/FoxPro (bridlicové pozadie, kaskádové okná, sýte čierne pravouhlé tiene `box-shadow: 10px 10px 0 #000`, žlté akcelerátory a azúrový výber).
   - **100% ovládateľné z klávesnice**: šípky `↑/↓/←/→`, `Enter`, `Esc`, priame písmenové skratky (hotkeys) a funkčné klávesy `F1`–`F10` bez nutnosti siahnuť na myš.
9. **Cyber Resilience Act (CRA) & SBOM CycloneDX v1.6**
   - Strojovo čitateľný súlad s európskym nariadením CRA na endpointoch `/v1/compliance/cra` a export SBOM `/v1/compliance/sbom`.
   - CLI príkazy `symbolon sbom` a `symbolon verify-artifact` pre overenie integrity a kontrolných súčtov binárok.
10. **Air-Gap Delegácia Kapacity, `.symreq` Požiadavky & `.symgrant` Granty (GNT-1..10, FLT-32..35)**
    - Plnohodnotná podpora pre striktne izolované priemyselné závody a bezsieťové prostredia bez internetového pripojenia.
    - Kryptografický protokol výmeny `.symreq` → `.symgrant` cez USB alebo Web UI s **disjunktnou alokáciou sedadiel** (`[SeatFrom..SeatTo]`), garanciou nulového prekrývania kapacít a auditným Merkle hash uzlom `usageDigest`.
    - Interaktívna mapa alokácie sedadiel vo Web Dashboarde, správa aktívnych poverení a CLI príkazy `symbolon grant request/issue/inspect/import`.
    - Offline node-lock aktivácie viazané na hardvérový fingerprint stanice.
11. **Viacjazyčné Klientske SDK (C#, Python, Go, Java, Rust, C/C++, WebAssembly/TS)**
    - Oficiálne klientske knižnice pod licenciou **Apache-2.0** v priečinku `sdk/` s automatickým vláknom/workerom pre heartbeat, adaptívnym jitterom ±10%, offline grace periódou a deterministickým A/B routingom.
    - **Hardware Enclave Attestation**: TPM 2.0 PCR quote generovanie a kryptografická verifikácia v C#, Pythone aj Ruste pre bezpečné air-gapped klientske prostredia.
12. **Hardware Enclave Attestation (R6)**
    - Podpora overovania kryptografických citácií TPM 2.0 PCR a Confidential Computing Enclaves (Intel SGX, AMD SEV-SNP) chránená challenge-response mechanizmom proti replay útokom.
13. **Cloud-Native Zero-Trust Mesh & OCI Bundles**
    - P2P Relay Mesh koordinátor s **Lamportovými logickými hodinami** a disjunktnými partíciami sedadiel zabraňujúci split-brain overage.
    - Štandardizované balíčky OCI Image Manifest v1 pre air-gapped Kubernetes repozitáre (`symbolon oci pack/verify`).
14. **Jednookenné Desktop Prostredie & Živá Telemetria**
    - Kompletná správa servera v jedinom okne s priebežným monitoringom systémových zdrojov (CPU, RAM, DISK, NET, IP, Používateľ) v reálnom čase.
15. **Multi-Region Geo-Replication (Active-Active CRDT)**
    - Kauzálne vektorové hodiny (Vector Clocks), stavový PN-Counter CRDT a autonómne disjoint prideľovanie sedadiel bez centrálneho distribuovaného zámku.
    - Okamžité bezkolízne lokálne checkouts v EU, US aj AP regiónoch s automatickým zliatím po obnovení spojenia (partition healing) a HMAC-SHA256 autentifikáciou.
    - Kompletná CLI správa (`symbolon cluster status/sync`) a živá vizualizácia v Retro TUI dashboarde.
16. **eBPF Kernel Socket Enforcement (Linux Boundary)**
    - Nízkoúrovňová ochrana na úrovni jadra Linuxu pomocou BPF CO-RE programov pripájaných na cgroup socket hooks (`cgroup/connect4`, `cgroup/connect6`).
    - Jadrová kontrola BPF máp (`license_map`) a okamžité zablokovanie sieťových spojení (`-EPERM` / `BPF_DROP`) procesov bez platného licenčného leasingu s ring-buffer auditom.
    - Diagnostické CLI príkazy (`symbolon ebpf status/attach/violations`) a živý monitoring v Retro TUI dashboarde.
17. **Cross-Platform Release Matrix & Multi-Arch Distribúcia**
    - Plnohodnotná podpora a samostatné single-file balíčky pre 6 cieľových platforiem: `linux-x64`, `linux-arm64`, `win-x64`, `win-arm64`, `osx-x64`, `osx-arm64`.
    - Plne automatizovaný GitHub Actions release pipeline (`.github/workflows/release.yml`) s generovaním `SHA256SUMS.txt`, CycloneDX v1.6 SBOM a GitHub Releases.
18. **License Borrowing & Roaming (Offline Výpožičky pre Poľné Zariadenia)**
    - Možnosť dlhodobého zapožičania sedadla (1–30 dní) pre offline stanice, terénne notebooky a inžinierske aplikácie bez sieťového pripojenia.
    - Automatické pozastavenie periodických heartbeatov v .NET a Python SDK; ukončenie behu aplikácie zachováva offline licenciu aktívnu.
    - CLI podpora (`symbolon license borrow`, `symbolon license return`) a správa výpožičiek v Retro TUI s možnosťou manuálneho predčasného uvoľnenia.
19. **Anti-Fraud & Impossible Travel Velocity / VM Cloning Detection**
    - Pokročilá detekcia podvodov v reálnom čase pomocou Haversine vzorca ($v = \Delta d / \Delta t$) odhaľujúca fyzikálne nemožné geografické skoky ($> 900 \text{ km/h}$) na vzdialenosti $> 100 \text{ km}$.
    - Detekcia klonovania virtuálnych strojov (VM snapshot replay) pri simultánnom pripojení identického hardvérového fingerprintu (SMBIOS UUID, MAC) z rôznych verejných IP podsietí.
    - Automatické spúšťanie bezpečnostných výstrah cez `IAlertService`, zápis do kryptografického audit ledgeru a živý monitorovací radar v Retro TUI Web Dashboarde.
20. **OpenTelemetry Distribuované Trasovanie (W3C TraceContext)**
    - Natívna podpora pre `System.Diagnostics.ActivitySource` naprieč celým stackom (`symbolon.checkout`, `symbolon.renew`, `symbolon.borrow`, `symbolon.fraud_check`, `symbolon.keys.split`, `symbolon.ebpf`).
    - Automatická propagácia hlavičiek `traceparent` a `tracestate` cez .NET Client SDK, Python SDK, Rust SDK, ControlPlane a Relay.
    - Živý kruhový diagnostický buffer (`SymbolonTraceBuffer`) a vizualizácia stôp v reálnom čase na Web TUI (`/admin/v1/traces/recent`).
21. **Shamir's Secret Sharing ($k$-of-$n$ Prahová Obnova Kľúčov pri Havárii)**
    - Informačno-teoreticky bezpečné delenie master podpisových kľúčov (ES256, ML-DSA-65) nad konečným poľom Galois Field $GF(2^8)$ s AES polynómom `0x11B`.
    - Rekonštrukcia kľúča z ľubovoľných $k$ z $n$ podielov pomocou Lagrangeovej interpolácie s kryptografickým SHA-256 MAC overením integrity.
    - CLI príkazy `symbolon keys split` a `symbolon keys combine` s podporou Base64Url tokenov aj štandardného PEM formátu.
22. **Enterprise SCIM 2.0 Identity & Group Synchronization Bridge (RFC 7643, RFC 7644)**
    - Automatická obojsmerná synchronizácia identít a skupín s podnikovými IdP (Microsoft Entra ID / Azure AD, Okta, PingFederate, Google Workspace) cez štandardné rozhranie `/scim/v2`.
    - **Zero-Trust Automatické Deprovisioning & Okamžitá Revokácia Sedadiel**: Okamžité uvoľnenie všetkých plávajúcich licencií (`LeaseEngine.ReleaseAsync`), zrušenie čakajúcich frontových lístkov a odobratie menných alokácií pri deaktivácii používateľa v IdP.
    - Retro FoxPro Web TUI integrácia s monitorovaním synchronizovaných používateľov a manuálnym núdzovým riadením.
23. **SAML 2.0 & OIDC Single Sign-On (SSO) s Enterprise RBAC Mapovaním Rolí**
    - Natívna integrácia s podnikovými Identity Provider-mi (Okta, Microsoft Entra ID, Keycloak, PingFederate).
    - OIDC Core 1.0 Authorization Code Flow s PKCE (RFC 7636) a bezstavovou HMAC-SHA256 ochranou relácie chrániacou pred CSRF útokmi.
    - SAML 2.0 Web Browser SSO profil (SP-initiated aj IdP-initiated) s XXE-safe XML validáciou (`DtdProcessing.Prohibit`) a automatickým exportom SP metadát (`/auth/sso/saml/metadata`).
    - Automatický RBAC claim mapper transformujúci externé IdP skupiny a roly na interné úrovne oprávnení (`admin:super`, `admin:tenant`, `auditor`).
    - Duálny režim autentifikácie v `ApiKeyAuthenticationHandler`: paralelné spracovanie strojových kľúčov (`X-Api-Key` / `Bearer sym_adm_...`) a zabezpečených HTTP-Only SSO cookie relácií (`symbolon_session` / `sym_sso_...`) pre Web TUI.
24. **Cloud-Native Kubernetes Operator & Enterprise Helm Chart (`Symbolon.Operator`)**
    - Custom Resource Definitions (CRDs): `SymbolonCluster` (správa klastra, replík, PQC režimu, PostgreSQL storage, Ingress a ServiceMonitor) a `SymbolonLicense` (deklaratívne nasadzovanie a sledovanie stavu licencií priamo cez `kubectl`).
    - Automatizované reconcilery (`ClusterReconciler`, `LicenseReconciler`) pre generovanie a synchronizáciu Deploymentov, PodDisruptionBudget (PDB), NetworkPolicy, ServiceMonitor pre Prometheus Operator a Kubernetes Secrets.
    - Podpora produkčného Helm Chartu s `pdb.yaml`, `networkpolicy.yaml` a `servicemonitor.yaml`.
    - Integrované CLI príkazy `symbolon k8s crd`, `symbolon k8s export-license` a `symbolon k8s generate-cluster` pre GitOps a CI/CD pipelines.
25. **WebAssembly & In-Browser Offline License Validator SDK (`@symbolon/validator`)**
    - Zero-dependency klientsky validátor bežiaci 100% offline vo webových prehliadačoch, Node.js, Electron, Tauri, React, Vue a Angular aplikáciách.
    - Kryptografické overovanie digitálnych podpisov **NIST P-256 (ES256)** cez štandardné rozhranie **W3C WebCrypto API** (`crypto.subtle.verify`) bez nutnosti kontaktu s licenčným serverom.
    - **Web Node-Locking**: Generovanie stabilného hardvérového odtlačku prehliadača (`generateBrowserFingerprint`) kombinujúceho 2D Canvas rendering, WebGL informácie, parametre displeja a systémové prostredie.
    - Interaktívny in-browser validačný portál integrovaný priamo v Retro FoxPro Web TUI (`view-wasm`) aj samostatný jedno-súborový offline portál (`sdk/wasm/index.html`).
26. **Enterprise Cloud KMS / Hardware HSM Integrácia & 3-Úrovňová Hierarchia Kľúčov (§9.3)**
    - **Cloud KMS & Hardware HSM**: Podpora pre **Azure Key Vault**, **AWS KMS**, **Hardware HSM (PKCS#11)** a **Encrypted Envelope Store**.
    - **Envelope Encryption**: Ochrana privátnych kľúčov šifrovanou obálkou **AES-256-GCM** s kľúčovou deriváciou **PBKDF2 (100 000 iterácií SHA-256)** a PEM armor serializáciou (`-----BEGIN ENCRYPTED SYMBOLON KEY-----`).
    - **3-Úrovňová Hierarchia Kľúčov (Root ➜ Product ➜ Lease)**:
      - *Tier 1 (Root Master Anchor)*: Offline/Cold kľúč s dlhodobou platnosťou (10 rokov).
      - *Tier 2 (Intermediate Product Authority)*: Zabezpečuje vydávanie licencií pre konkrétne produktové línie (2 roky).
      - *Tier 3 (Ephemeral Lease Key)*: Efemerálny kľúč pre klastrové uzly a sedadlá (30 dní).
    - **Production Key Safety Guard (§9.3 Pravidlo 4)**: Fail-fast overenie pri štarte servera – odmietnutie štartu v produkčnom prostredí (`ASPNETCORE_ENVIRONMENT=Production`) pri detekcii nezašifrovaných privátnych kľúčov v bežných súboroch či premenných.
    - **CLI & Web TUI**: Príkazy `symbolon kms status`, `symbolon keys envelope`, `symbolon keys hierarchy` a interaktívna vizualizácia reťazca dôvery v Retro FoxPro Web TUI.
27. **Token & Credit-Based Metered Licensing Engine (Pay-As-You-Go, Token Wallets & Consumption Ledger)**
    - **Podnikové Účtovanie Spotreby**: Tokenový a kreditový licenčný model pre náročné CAD, FEA simulácie, AI inferenciu a HPC výpočty.
    - **Multi-Tenant Kreditové Peňaženky (`TokenWallet`)**: Podpora kreditových fondov s konfigurovateľným povoleným prečerpaním (`OverdraftLimit`) a výstrahami pri nízkom zostatku (`ThresholdLowAlert`).
    - **Flexibilný Sadzobník Jednotiek a Času (`TokenRate`)**: Sadzba za minútu behu (`RatePerMinute`) alebo za výpočtovú jednotku/úlohu (`RatePerUnit`).
    - **Dvojfázové Rezervovanie Kreditov (2PC)**: Rezervácia pred začatím výpočtu (`reserve`), priebežný heartbeat spotreby s predlžovaním TTL (`heartbeat`), finálne zúčtovanie (`commit`) alebo okamžité uvoľnenie alokácie pri zlyhaní či zrušení úlohy (`rollback`).
    - **Nemenný Auditný Ledger (`TokenLedgerEntry`)**: Záznam každého pohybu kreditov (credit, reserve, consume, release, refund) s podporou idempotencie.
    - **CLI & Web / Retro FoxPro TUI**: Príkazy `symbolon tokens wallets|create|credit|rates|set-rate|balance`, nové navigačné menu a dedikované zobrazenie `view-tokens` v riadiacom paneli.
28. **Enterprise Migration Engine & Transpiler (FlexNet/FLEXlm, Options.opt, lmgrd Log Analytics & Keygen Importer)**
    - **Automatizovaný Prechod z FlexNet Publisher**: Plnohodnotná podpora dekódovania `license.dat` / `*.lic` vrátane `SERVER`, `DAEMON`/`VENDOR`, `FEATURE`, `INCREMENT`, `PACKAGE` a `UPGRADE` s normalizáciou dátumov a prepočtom neobmedzených (`uncounted`) aj diskrétnych sedadiel.
    - **Transpilácia `options.opt` do JSON Pravidiel**: Deterministická konverzia `GROUP`, `HOST_GROUP`, `RESERVE`, `MAX`, `INCLUDE`/`EXCLUDE`, `BORROW_LOWWATER` a časových limitov priamo do Symbolon `Policy` pravidiel.
    - **lmgrd Log Analytics & Right-Sizing KPI Engine**: Hĺbková analýza záznamov `lmgrd.log` (`OUT`, `IN`, `DENIED`), výpočet reálnej krivky súbehu sedadiel, identifikácia špičiek a inteligentný odhad optimálnej kapacity s bezpečnostným kreditovým prečerpaním zabraňujúci zbytočnému preplácaniu licencií.
    - **Keygen.sh Importer**: Bezproblémový import používateľov, politík a licencií priamo z cloudových JSON exportov platformy Keygen.sh.
    - **CLI & Web / Retro FoxPro TUI**: Príkazy `symbolon migrate flexnet|options|log|keygen`, nové navigačné menu a dedikované zobrazenie `view-migrate` v riadiacom paneli s interaktívnymi šablónami.
29. **Enterprise Webhook & Event Notification Engine (HMAC-SHA256, Slack/Teams Integration, Dead-Letter Queue & Automated License Expiration Lifecycle)**
    - **Kryptografické HMAC-SHA256 Podpisy & Anti-Replay Ochrana**: Hlavička `X-Symbolon-Signature: t={ts},v1={hex}` s konštantno-časovým overovaním (`FixedTimeEquals`) a toleranciou časového posunu proti replay útokom.
    - **Natívne Formátovače pre Slack & Microsoft Teams**: Automatické prispôsobenie payloadov pre štandardný JSON, Slack Incoming Webhooks (farebné bloky podľa závažnosti udalosti) a Microsoft Teams Connector karty (MessageCards).
    - **Asynchrónna Bounded Fronta & Background Worker**: Bounded Channel `IWebhookQueue` zaručujúci nulovú latenciu licenčných operácií a spoľahlivé spracovanie na pozadí s evidenciou dĺžky trvania `DurationMs`.
    - **Dead-Letter Queue (DLQ) & Okamžitý Replay**: Automatické označenie zlyhaných doručení po vyčerpaní pokusov a dedikovaný endpoint `POST /admin/v1/webhooks/deliveries/{id}/replay` s filtrovaním podľa stavu.
    - **Engine pre Životný Cyklus Licencií (`LicenseLifecycleEngine`)**: Automatické monitorovanie a vyhodnocovanie expirácie licencií (`ExpiringSoon`, `SoftGrace`, `HardGrace`, `Expired`, `Perpetual`) s generovaním systémových alertov (`license.expiring_soon`, `license.grace_entered`, `license.expired`).
    - **Udalosťami Riadené Notifikácie (Event Hooking)**: Automatické odosielanie notifikácií pri zamietnutí sedadiel (`lease.denied`), detekcii bezpečnostných anomálií a klonovania (`fraud.detected`) a nízkom stave kreditových peňaženiek (`token.threshold_low`).
    - **CLI & Web Dashboard**: Kompletná sada príkazov `symbolon webhooks list|create|delete|test|deliveries|replay|lifecycle`, živý KPI panel doručení, filter pre DLQ a manuálna kontrola expirácií.
30. **Dynamic Entitlements & Granular Feature Flagging Engine (Tiered Modules, Package Suites & RAII Context Managers)**
    - **Nezávislé kvóty sedadiel modulov**: Nezávislé concurrency limity pre funkčné moduly a add-ony oddelené od kapacity hlavnej licencie podľa vzoru FlexNet/RLM/Sentinel RMS.
    - **Balíkové suity (Package Suites)**: Automatická dekompozícia suít na dcérske moduly pri checkoutoch s deterministickou kontrolou verzií (`*`, `2026.*`, intervaly `>= 2025.0 and <= 2027.0`).
    - **Dynamická alokácia za behu**: Získavanie a uvoľňovanie funkcií za behu bez nutnosti re-checkoutu hlavného sedadla aplikácie (`POST /v1/leases/{id}/features/acquire`, `POST /v1/leases/{id}/features/release`, `GET /v1/leases/{id}/features`).
    - **RAII Správa vo Viacjazyčných SDK**: Plná integrácia do C# (`await using var feat = await lease.UseFeatureAsync("FEA_SOLVER")`), Pythonu (`with lease.use_feature("FEA_SOLVER"):`), Rustu (`let _feat = lease.acquire_feature("FEA_SOLVER", ...)` s `Drop`) a C/C++ (`ScopedFeatureLease`) s deterministickým uvoľnením zdrojov.
    - **Live Concurrency Teplomery & FoxPro TUI**: Grafické gauge merače vyťaženia modulov, katalóg balíkov a suít vo webovom paneli aj v retro FoxPro TUI (klávesová skratka `U`).
31. **Zero-Config Server Discovery, Resilient Multi-Server Failover Pool & Enterprise Environment Variable Resolution**
    - **Zero-Config UDP Broadcast / Multicast Discovery (Port 7584 / `0x1D90`)**: Automatické vyhľadávanie aktívnych uzlov ControlPlane a Relay na lokálnej podsieti bez nutnosti akejkoľvek manuálnej konfigurácie adries serverov.
    - **Odolný `ServerFailoverPool`**: Priebežné sledovanie zdravia serverov s automatickým prepnutím na záložné inštancie pri výpadkoch siete, 502/503/504 stavoch a exponenciálnym cooldownom.
    - **Enterprise Premenné Prostredia (`SYMBOLON_LICENSE_SERVER`, `SYMBOLON_SERVERS`)**: Podpora FlexNet notácie `port@host`, `@host`, štandardných URL adries a zoznamov oddelených čiarkami či bodkočiarkami.
    - **Parita vo Viacjazyčných SDK**: Automatická detekcia a konfigurácia v C#, Python, Rust a C/C++ SDK.
    - **CLI Nástroje**: Príkazy `symbolon discover` a `symbolon servers resolve` pre vizualizáciu nájdených uzlov a diagnostiku parsovania.
32. **Enterprise Options File Engine & Deklaratívne Pravidlá Politík (FLT-23, FLT-24, FLT-25, §7.5)**
    - **FLT-24 Deterministické Vyhodnocovanie**: Striktné poradie `deny` ➜ `max` ➜ `reserve` ➜ `priority`. Prvé vyhovujúce `deny` pravidlo okamžite zastaví vyhodnocovanie s chybou 403 Forbidden (`RuleDenied`).
    - **FLT-23 Rezervácie a Izolácia Kapacity Sedadiel**: Deklaratívne pravidlá `reserve` materializujú vyhradené sedadlá s atribútom `ReservedFor` priamo v PostgreSQL / SQLite. Bežné checkouts bez kvalifikácie nemôžu čerpať z vyhradenej kapacity.
    - **FLT-25 Aktualizácia za Behu s Auditom**: Zmena pravidiel cez `PUT /admin/v1/licenses/{id}/rules` vyvoláva auditnú udalosť `policy.rules_updated`, dynamicky synchronizuje rezervácie a zachováva aktívne leasingy až do vypršania TTL.
    - **Wildcard & CIDR Matching**: Presné priraďovanie skupín používateľov (`eng-*`), hostiteľov (`srv-*`) a IP podsietí (IPv4/IPv6 CIDR notácia `10.0.0.0/8`, `192.168.1.0/24`).
    - **Multi-Interface Parita**: REST API endpointy, lokálny engine v on-premise Relay serveri, CLI príkazy `symbolon policy rules get|set|test`, obojsmerný YAML editor a interaktívny simulátor vo Web Dashboarde aj v Retro FoxPro TUI.
33. **Enterprise Offline Roaming, Self-Verifiable `.symlease` Artifact & Cryptographic Proof-of-Possession Early Return (FLT-17 – FLT-22, §7.4)**
    - **Offline Roaming & Zapožičanie Sedadla**: Možnosť zapožičať plávajúce sedadlo na offline použitie na definovaný počet dní (FLT-17).
    - **Samostatne Verifikovateľný `.symlease` Artefakt**: JWS General JSON formát s PEM obálkou (`-----BEGIN SYMBOLON LEASE-----`), hybridným podpisom (ES256 + ML-DSA-65) a deterministickou verifikáciou offline klientom.
    - **Cryptographic Proof-of-Possession Early Return (FLT-21)**: Predčasné vrátenie sedadla cez challenge-response mechanizmus — server vydá jednorazovú výzvu (nonce s 5-minútovou TTL) a klient ju podpíše efemérnym privátnym kľúčom vytvoreným pri zapožičaní.
    - **Ochrana Proti Neautorizovanému Uvoľneniu (FLT-22)**: Bežné uvoľnenie (`DELETE /v1/leases/{id}`) bez dôkazu o držbe zapožičaného sedadla zlyháva s chybou `403 Forbidden` (`ProofOfPossessionRequired`).
34. **Advanced Multi-Component Hardware Fingerprinting, Fuzzy Node-Lock Matching & Anti-Virtualization/Container Isolation (FPR-1 – FPR-19, LIC-33, §7.6, §8)**
    - **Multi-Component Hardvérový Odtlačok (FPR-1 – FPR-4)**: Deterministický zber 6 štandardných kľúčov (`machineId`, `board`, `cpu`, `disk`, `mac`, `host`) s vynechaním placeholderov a GDPR-kompatibilnou salted SHA-256 pseudonymizáciou hostname.
    - **Fuzzy Node-Lock Matching Engine (FPR-5 – FPR-9)**: Podpora stratégií `match-any`, `match-two`, `match-most` (predvolená, >50%) a `match-all` s automatickou degradáciou na `match-all` pri menej ako 2 spoločných komponentoch (FPR-8).
    - **Anti-Virtualizácia & Kontajnerová Izolácia (FPR-10 – FPR-14)**: Detekcia Docker, Containerd, Kubernetes a hypervízorových prostredí, persistentný volume UUID manažment a odporúčanie prechodu na floating lízing.
    - **Životný Cyklus Aktivácie & Správa Uzlov (FPR-15 – FPR-17)**: Registrácia uzlov cez `POST /v1/activations`, kontrola unikátnosti podľa politiky, overenie zhody cez `POST /v1/activations/verify-match` a okamžité uvoľnenie slotu pri deaktivácii.
35. **Air-Gapped Capacity Transfer, Signed `.symreq` Request & Delegated Seat Grant Issuance Protocol (GNT-1 – GNT-10, FLT-32 – FLT-35, §5, §7.7)**
    - **Delegované Poverenia `.symgrant` & Žiadosti `.symreq`**: JWS General JSON artefakty s PEM obálkou a hybridným podpisom (ES256 + ML-DSA-65) pre izolované priemyselné závody.
    - **Disjunctive Grant Allocator (GNT-3, GNT-4, GNT-5)**: Deterministické prideľovanie disjunktných rozsahov sedadiel `[SeatFrom..SeatTo]` s matematickou zárukou nulového prekrývania kapacít.
    - **Monotónna Ochrana & Merkle Digest (GNT-7, GNT-9, FLT-33)**: Prísna kontrola sekvenčných čísiel, automatické nahradenie predchádzajúcich grantov (`supersedes`) a overenie auditného Merkle hash reťazca pri USB prenose.
    - **Offline Edge Relay Enforcement (GNT-10)**: Striktné ohraničenie lokálnych leasingov do delegovaného intervalu a registrácia efemérneho podpisového kľúča.
36. **A/B Testovanie a Experimentačný Engine (Vytvorenie, Deterministický Bucketing & Reálne Záťažové Testovanie A/B)**
    - **Deterministický Bezstavový Hashing**: Rozdeľovanie klientov do experimentálnych bucketov (0–99%) pomocou $\text{Murmur3}(\text{LicenseKey} \mathbin{\Vert} \text{MachineId} \mathbin{\Vert} \text{Salt}) \pmod{100}$ bez nutnosti databázového stavu.
    - **Sticky Session Invariant**: Záruka nulového posunu (zero-drift) — klient zostáva v identickom variante počas celého životného cyklu leasingu (checkout ➜ heartbeat ➜ renew ➜ release).
    - **Reálne Testovanie A/B Testovaním**: Automatizovaný validačný harness s **10 000 paralelnými virtuálnymi klientmi**, Chi-Square ($\chi^2$) testom dobrej zhody ($p > 0.05$), overením chaos failoveru a canary circuit breakerom (<100 ms automatický rollback pri chybovosti).
    - **Štatistické Vyhodnotenie v Reálnom Čase**: Výpočet Z-score, 95% konfidenčného intervalu a p-hodnoty v databázovom akumulátore s riadením vo Web TUI.
37. **Post-Quantum Era — Komplexná Pripravenosť na Post-Kvantovú Éru (FIPS 203, FIPS 204, FIPS 205 & Profil `pqc-strict`)**
    - **Plná NIST PQC Suite**: Integrácia **FIPS 203 (ML-KEM-768/1024)** pre hybridný TLS 1.3 / mTLS kľúčový handshake a ochranu voči útokom typu *Harvest Now, Decrypt Later (HNDL)*.
    - **Stateless Hash Signatures (FIPS 205 / SLH-DSA)**: Matematicky nezávislý záložný podpisový algoritmus eliminujúci riziko prípadného prelomenia mriežkovej kryptografie (LWE).
    - **Režim `pqc-strict`**: Režim bez klasickej kryptografie (nulové RSA / ECDSA) spĺňajúci štandardy **CNSA 2.0** a európske smernice **NIS 2 / NIS Cooperation Group Roadmap 2030**.
    - **Quantum Vulnerability & Migration Scanner (`symbolon pqc scan`)**: Integrovaný skener v CLI a Web Dashboarde s výpočtom PQC Readiness Indexu (0–100%) a migračným plánom.

---

## Architektúra Riešenia

Celé riešenie je postavené na **.NET 10 LTS** v súlade s normatívnou [špecifikáciou](spec/README.md) a rozdelené do modulárnych projektov:

```
Achilles.slnx
├── src/
│   ├── Achilles.Crypto         (Apache-2.0)  - ES256, ML-DSA-65 (FIPS 204), KeyRing, JWK/JWKS (RFC 9964)
│   ├── Achilles.Format         (Apache-2.0)  - symlic/1 JWS, Crockford Base32 + CRC-32C, PEM Armor, LIC-34
│   ├── Achilles.Protocol       (Apache-2.0)  - symlease+jwt, SHA-256 fingerprint kanonizácia, DTO kontrakty
│   ├── Achilles.Domain         (AGPL-3.0)    - LeaseEngine, ISeatStore, IAuditLedger, stavové automaty, idempotencia
│   ├── Achilles.Relay          (AGPL-3.0)    - Samostatný on-premise relay server (SQLite WAL, Minimal API)
│   ├── Achilles.Client         (Apache-2.0)  - Klientske ISV SDK, IAsyncDisposable SeatLease, automatický heartbeat
│   ├── Achilles.Cli            (AGPL-3.0)    - CLI nástroj pre správu kľúčov, vydávanie licencií, SBOM a diagnostiku
│   ├── Achilles.Data           (AGPL-3.0)    - EF Core 10, Npgsql 10, multi-tenancy, materializované sedadlá, ledger
│   ├── Achilles.Operator       (AGPL-3.0)    - Kubernetes Operator, CRDs, reconcilery, manifest generátory
│   └── Achilles.ControlPlane   (AGPL-3.0)    - Centrálny server (Public, Admin, Relay, Compliance, /metrics, Web TUI)
├── deploy/
│   ├── config/                               - Vzorové konfiguračné súbory a mapovania z FlexNetu (.opt -> policy.json)
│   ├── docker/                               - Multi-stage Dockerfile pre ControlPlane a Relay
│   ├── docker-compose.yml                    - Kompletný stack: Postgres 17, ControlPlane, Relay, Prometheus, Grafana
│   ├── prometheus/                           - Prometheus konfigurácia zberu metrík
│   ├── grafana/                              - Provisioning a predkonfigurované dashboardy
│   ├── helm/achilles/                        - Kubernetes Helm Chart (Deployment, Service, Ingress, PDB, NetworkPolicy, ServiceMonitor)
│   └── systemd/                              - Tvrdený Linux systemd unit pre on-premise Relay
├── sdk/
│   ├── wasm/                                 - WebAssembly & WebCrypto JS/TS SDK (@achilles/validator, 100% offline)
│   ├── python/achilles/                      - Python SDK (pip installable, context manager, daemon heartbeat)
│   ├── rust/achilles/                        - Rust crate (Tokio async, RAII Drop pattern)
│   ├── go/achilles/                          - Go SDK (Heartbeat goroutine, context cancel)
│   └── c/                                    - C99 / C++17 knižnica (ScopedLease RAII)
└── tests/
    ├── Achilles.Crypto.Tests                 - Testy kryptografických primitív a hybridných podpisov
    ├── Achilles.Format.Tests                 - Validácia formátu symlic/1, Crockford Base32 a PEM obálky
    ├── Achilles.Protocol.Tests               - Serializácia DTO a kanonizácia hardvérového fingerprintu
    ├── Achilles.Domain.Tests                 - Testy LeaseEngine, TTL expirácie a idempotencie
    ├── Achilles.Relay.Tests                  - Integračné testy on-premise Relay servera
    ├── Achilles.Client.Tests                 - Testy ISV SDK, automatického obnovovania a jitteru
    ├── Achilles.Cli.Tests                    - Testy príkazového riadka
    ├── Achilles.Data.Tests                   - Testy PostgreSQL / SQLite úložiska sedadiel a auditného reťazca
    ├── Achilles.Operator.Tests               - Testy Kubernetes Operatora, reconcilerov a manifestov
    └── Achilles.ControlPlane.Tests           - Komplexné testy API, metrík, CRA reportov a CycloneDX SBOM
```

---

## Požiadavky na Systém (Hardvér a Softvér)

Server Symbolon je optimalizovaný pre maximálnu priepustnosť, extrémnu odolnosť a nízku latenciu vďaka natívnemu AOT kódu a deterministickému O(1) prideľovaniu sedadiel. Nasledujúce požiadavky definujú minimálne a odporúčané parametre pre jednotlivé profily nasadenia.

---

### 1. Hardvérové Požiadavky (Hardware Specifications)

| Profil Nasadenia / Komponent | Minimálne Parametre (Min) | Odporúčané Parametre (Production Rec) | Poznámka k Výkonu a Kapacite |
|---|---|---|---|
| **On-Premise Edge Relay** | 1 vCPU, 512 MB RAM, 1 GB disk | 2 vCPU, 1–2 GB RAM, 5 GB NVMe | Zvláda tisíce lokálnych heartbeatov/s pri spotrebe pamäte < 80 MB. Autonómny SQLite WAL engine. |
| **Air-Gapped Standalone Relay** | 1 vCPU, 512 MB RAM, 2 GB disk | 2 vCPU, 1 GB RAM, 5 GB SSD | Pre striktne izolované priemyselné zóny a bezsieťové klastre. Vyžaduje USB port pre transfer `.symreq`/`.symgrant`. |
| **Control Plane — Standard Tier**<br>*(do 10 000 súbežných sedadiel)* | 2 vCPU, 2 GB RAM, 10 GB SSD | 4 vCPU, 4–8 GB RAM, 25 GB NVMe | Vhodné pre stredné ISV inštalácie, podnikové privátne cloudy a lokálne klastre. |
| **Control Plane — Enterprise Tier**<br>*(> 50 000 súbežných sedadiel)* | 4 vCPU, 8 GB RAM, 50 GB NVMe | 8+ vCPU, 16–32 GB RAM, 100+ GB NVMe (RAID-10) | Pre masívne súbehy, multi-region aktívno-aktívne klastre, vysokofrekvenčný auditný ledger a True-Up reporting. |
| **Dedikovaný PostgreSQL Server**<br>*(pre Enterprise Control Plane)* | 2 vCPU, 4 GB RAM, 20 GB SSD | 8 vCPU, 16–32 GB RAM, 100+ GB NVMe (> 5 000 IOPS) | Zabezpečuje sub-milisekundové zamykanie `FOR UPDATE SKIP LOCKED` a materializované sedadlá. |
| **Klientske Stanice & ISV Aplikácie** | 64-bit architektúra (x64 / ARM64) | 1 vCPU, < 10 MB voľnej RAM | Klientske SDK (.NET, Python, Rust, C/C++) má zanedbateľný footprint (< 5 MB RAM, < 0.1% CPU). |

#### Sieťová Infraštruktúra, Latencia a Priepustnosť
- **Dátový tok heartbeatu**: Prenos jedného lease renewal paketu predstavuje iba cca **350 bajtov**. Napríklad 10 000 aktívnych klientov pri 60-sekundovom obnovovacom intervale generuje sieťový tok iba cca **58 kB/s**.
- **Latencia k databáze**: Pre Control Plane sa odporúča pripojenie k PostgreSQL s odozvou **< 5 ms** (lokálna sieť alebo dedikovaný cloud VPC peering).
- **Priepustnosť rozhrania**: Pre bežný Control Plane a Relay postačuje **1 Gbps Ethernet**; pre Enterprise klastre s tisíckami požiadaviek za sekundu sa odporúča **10 Gbps Ethernet**.
- **Lokálny Broadcast / Multicast**: Podpora protokolu UDP pre port `7584` (`0x1D90`) na lokálnom segmente siete pre fungovanie Zero-Config Discovery mechanizmu.

---

### 2. Softvérové Požiadavky (Software Specifications)

#### Podporované Operačné Systémy (OS Matrix)
- **Linux (odporúčané pre produkčné nasadenie)**:
  - Ubuntu 22.04 LTS / 24.04 LTS (x64, ARM64)
  - Debian 12 (Bookworm) a novší (x64, ARM64)
  - Red Hat Enterprise Linux (RHEL) 9+ / Rocky Linux 9+ / AlmaLinux 9+
  - Alpine Linux 3.19+ (podpora pre `musl libc` aj `glibc` kontajnerové obrazy)
- **Windows**:
  - Windows Server 2019 / 2022 / 2025 (64-bit)
  - Windows 10 / 11 (64-bit x64 a ARM64)
- **macOS**:
  - macOS 13+ (Ventura, Sonoma, Sequoia — architektúry Apple Silicon M1/M2/M3/M4 aj Intel x64)
- **Kontajnerizácia & Orchestrácia**:
  - Docker Engine 24+ / Podman 4+
  - Docker Compose v2.20+
  - Kubernetes 1.28+ (podporovaný natívny Helm Chart a Symbolon Kubernetes Operator)

#### Behové Prostredie (Runtime) & Knižnice
- **.NET Runtime**:
  - **.NET 10.0 LTS** Runtime / ASP.NET Core Runtime (pre beh zo zdrojových kódov alebo framework-dependent nasadenie).
  - *Self-Contained & Docker*: Oficiálne Docker kontajnery a samostatné single-file binárky (`symbolon`, `symbolon-relay`) obsahujú pribalený AOT runtime – **nevyžadujú predinštalovaný .NET v hostiteľskom systéme**.
- **C-Runtime & Systémové Knižnice**:
  - Linux: `glibc 2.31+` alebo `musl libc 1.2.4+`, `libssl 3.0+`.
  - Windows: Universal C Runtime (CRT) – štandardná súčasť moderných Windows systémov.

#### Databázové Úložiská
- **PostgreSQL 16 alebo 17** (odporúčané **PostgreSQL 17**):
  - Vyžadované pre produkčný Symbolon Control Plane.
  - Využíva natívnu schému s materializovanými sedadlami, indexované stĺpce `ReservedFor` a `UserId`, a transakčný O(1) mechanizmus `FOR UPDATE SKIP LOCKED`.
- **SQLite 3.42+**:
  - Vstavané automaticky priamo v binárke Relay servera.
  - Využíva režim Write-Ahead Logging (`PRAGMA journal_mode=WAL;`), indexy na `reserved_for` a garantuje bezpečný súbeh procesov pri výpadkoch siete a reštartoch.

#### Sieťové Porty a Firewall Pravidlá
| Port / Protokol | Smer Komunikácie | Služba | Popis a Účel |
|---|---|---|---|
| `8080/TCP` | Inbound | Control Plane | REST API, Web TUI, SCIM 2.0, CRA/SBOM compliance, Prometheus metriky. |
| `8081/TCP` | Inbound | On-Premise Relay | Lokálne klientske API, heartbeat, edge správa sedadiel a fronty. |
| `7584/UDP` (`0x1D90`) | Inbound / Outbound | Discovery Responder | Zero-config automatické vyhľadávanie licenčných serverov na lokálnej sieti. |
| `5432/TCP` | Outbound (CP ➜ DB) | PostgreSQL | Databázové spojenie pre Control Plane (chránené TLS). |
| `9090/TCP` & `3000/TCP` | Inbound | Observabilita | Prometheus zber metrík a Grafana dashboard (voliteľné pre monitoring). |

---

### 3. Požiadavky Špecifických Modulov a Fázy 20

- **Enterprise Options File Engine & Policy Rules (Fáza 20 - FLT-23, FLT-24, FLT-25, §7.5)**:
  - *Dátová vrstva*: Podpora pre stĺpce `ReservedFor` (vyhradená kapacita) a `UserId` (aktívny nájomca) v `SeatEntity` (PostgreSQL) a `sqlite_seat_store` (SQLite).
  - *Pamäťový procesor*: In-memory cache pravidiel pre okamžité vyhodnocovanie CIDR podsietí a wildcard masiek používateľov/hostiteľov s odozvou pod 1 milisekundu.
  - *Konfigurácia*: Prístup k čítaniu/zápisu konfiguračných súborov politík (`options.yaml` na Relay, auditný ledger na Control Plane).
- **Air-Gapped & Offline Izolované Prostredia**:
  - *Nulová závislosť na internete*: Server ani klientske SDK nevyžadujú DNS preklad, verejné NTP servery ani kontakt s externými licencormi.
  - *Výmena Offline Tokenov*: Fyzické vymeniteľné médium (USB flash disk), optické médium alebo lokálny zabezpečený SFTP kanál pre prenos požiadaviek `.symreq` a grantov `.symgrant`.
  - *In-Browser Validátor*: Prehliadač s podporou W3C WebCrypto API (Chrome 37+, Firefox 34+, Safari 11+, Edge 79+) – funguje 100% offline z lokálneho disku.
- **Hardware Enclave Attestation (R6)**:
  - Fyzický čip **TPM 2.0** (Trusted Platform Module) alebo vTPM vo virtualizovaných prostrediach.
  - Na Linuxe vyžadovaný balíček `tpm2-tools` a prístup k zariadeniu `/dev/tpmrm0`.
  - Podpora pre dôveryhodné exekučné prostredia Intel SGX / AMD SEV-SNP.
- **eBPF Kernel Socket Enforcement**:
  - Linuxové jadro verzie **5.15 alebo novšej** (odporúčané 6.5+).
  - Kompilácia jadra s podporou BPF CO-RE: `CONFIG_DEBUG_INFO_BTF=y`, `CONFIG_BPF=y`, `CONFIG_BPF_SYSCALL=y`, `CONFIG_NET_CLS_ACT=y`.
  - Používateľské oprávnenie `CAP_BPF` / `CAP_NET_ADMIN` alebo `root` pre pripojenie socket filtrov.
- **Cloud KMS & Hardware HSM**:
  - Sieťová konektivita k endpointom Azure Key Vault, AWS KMS alebo podpora štandardného rozhrania **PKCS#11** (knižnice vendor HSM ako Thales Luna, Utimaco, YubiHSM).

---

### 4. Požiadavky pre Vývoj a Kompiláciu (Build & Dev)

Ak plánujete kompilovať riešenie zo zdrojových kódov, prispievať do repozitára alebo vyvíjať vlastné integrácie:
- **.NET SDK 10.0** (podpora jazyka C# 14.0, AOT source generátorov).
- **Git 2.30+** (správa verzií, Git LFS voliteľné).
- **Python 3.10+** (pre vývoj a beh testov v `sdk/python/symbolon`).
- **Node.js 18+** (pre offline WebAssembly a WebCrypto testy v `sdk/wasm`).
- **Rust 1.75+** (edícia 2021 s Cargo, pre zostavenie Rust SDK v `sdk/rust`).
- **C/C++ kompilátor**: GCC 9+, Clang 10+ alebo MSVC 2019+ s podporou štandardov C99 a C++17 (pre klientske C/C++ SDK v `sdk/c_cpp`).

---

## Rýchly Štart (Quickstart)

### 1. Zostavenie riešenia a spustenie testov

```bash
# Naklonovanie repozitára
git clone https://github.com/MAGORS-organisation/License-server.git
cd License-server

# Zostavenie celého solution
dotnet build Achilles.slnx

# Spustenie testov naprieč všetkými 10 .NET projektmi a klientskymi SDK (656 testov)
dotnet test Achilles.slnx
python -m unittest discover sdk/python/achilles/tests
node --test sdk/wasm/tests/validator.test.js

# Diagnostika prostredia, PQC pripravenosti a sond serverov (Doctor)
dotnet run --project src/Achilles.Cli/Achilles.Cli.csproj -- doctor
```

### 2. Spustenie celého prostredia cez Docker Compose

Spustí naraz **PostgreSQL 17**, **ControlPlane** (s Web TUI), **Relay**, **Prometheus** a **Grafanu**:

```bash
docker compose up -d
```

Dostupné služby:
- **Achilles Web UI & ControlPlane API:** `http://localhost:8080`
  - Konzola pre správu: `http://localhost:8080/`
  - CycloneDX v1.6 SBOM: `http://localhost:8080/v1/compliance/sbom`
  - Cyber Resilience Act (CRA) report: `http://localhost:8080/v1/compliance/cra`
  - OpenAPI 3.1 dokumentácia: `http://localhost:8080/openapi/v1.json`
  - Health check: `http://localhost:8080/health/ready`
  - Prometheus metriky: `http://localhost:8080/metrics`
- **Achilles On-Premise Relay:** `http://localhost:8081`
- **Prometheus UI:** `http://localhost:9090`
- **Grafana Dashboard:** `http://localhost:3000` (prihlásenie: `admin` / `achilles_grafana_secure_admin_2026`)

---

## Ukážka Klientskej Integrácie (4 Jazyky)

Podrobný návod krok za krokom nájdete v **[Integration Quickstart Guide](docs/quickstart-guide.md)**.

### C# (.NET 10)
```csharp
using Achilles.Client;

var options = new AchillesClientOptions {
    ServerUri = new Uri("http://localhost:8080"),
    LicenseKey = "ACH-9ABC-DEF2-3456-7890"
};
using var client = new AchillesClient(options);

await using var lease = await client.AcquireSeatAsync(["cad-core", "rendering"]);
if (lease.Acquired) {
    Console.WriteLine($"Sedadlo #{lease.SeatNo} pridelené! Heartbeat beží na pozadí.");
    // Beh vašej aplikácie...
}
// Sedadlo sa automaticky uvoľní pri opustení bloku.
```

### Python (3.10+)
```python
from achilles import AchillesClient

client = AchillesClient("http://localhost:8080", product_code="cad-pro")
with client.acquire_seat("ACH-9ABC-DEF2-3456-7890", features=["cad-core"]) as lease:
    print(f"Sedadlo #{lease.seat_number} alokované. Aplikácia beží...")
# Automatické uvoľnenie po opustení bloku with (dostupný aj spätný import from symbolon import ...)
```

### Rust (2021 Edition)
```rust
use achilles_client::AchillesClient;

let client = AchillesClient::new("http://localhost:8080", "cad-pro");
let lease = client.acquire_seat("ACH-9ABC-DEF2-3456-7890")?;
println!("Sedadlo #{} alokované!", lease.seat_number());
// Pri opustení scope sa vďaka RAII Drop sedadlo okamžite vráti do fondu.
```

### C99 & C++17
```cpp
#include "achilles.h"

achilles_client_t* client = nullptr;
achilles_client_create("http://localhost:8080", "cad-pro", &client);

achilles_lease_t* raw_lease = nullptr;
if (achilles_acquire_seat(client, "ACH-9ABC-DEF2-3456-7890", &raw_lease) == ACHILLES_OK) {
    achilles::ScopedLease lease(raw_lease); // C++ RAII wrapper
    // ... výkonný kód aplikácie ...
}
achilles_client_destroy(client);
```

---

## Ako Používať `Achilles.Cli`

```bash
# 1. Spustenie interaktívneho inštalačného sprievodcu (TUI Wizard)
dotnet run --project src/Achilles.Cli -- setup

# 2. Vygenerovanie nového páru podpisových kľúčov (ES256 alebo hybrid ML-DSA-65)
dotnet run --project src/Achilles.Cli -- key gen -a es256 -o ./my-keys

# 3. Vydanie licenčného súboru .symlic
dotnet run --project src/Achilles.Cli -- lic issue \
  --product "cad-pro" \
  --customer "Acme Corporation" \
  --seats 10 \
  --type floating \
  --key ./my-keys/private.jwk \
  --out license.symlic

# 4. Export SBOM v štandarde CycloneDX v1.6
dotnet run --project src/Achilles.Cli -- sbom --out sbom.json

# 5. Overenie integrity binárneho artefaktu
dotnet run --project src/Achilles.Cli -- verify-artifact --file app.dll --expected-hash <sha256>

# 6. Diagnostika prostredia (Doctor)
dotnet run --project src/Achilles.Cli -- doctor --file license.symlic --key ./my-keys/public.jwk
```

---

## Prehľad API Endpointov

### Verejné Klientske API (`/v1`)
- `POST /v1/leases` — získanie plávajúceho sedadla (checkout) a vydanie podpísaného JWS lease tokenu.
- `POST /v1/leases/{id}/renew` — predĺženie platnosti sedadla (heartbeat) s kontrolou seq monotónnosti.
- `DELETE /v1/leases/{id}` — okamžité uvoľnenie sedadla.
- `POST /v1/leases/{id}/features/acquire` — dynamická alokácia funkčného modulu / add-onu za behu s kontrolou kvóty.
- `POST /v1/leases/{id}/features/release` — explicitné uvoľnenie funkčného modulu späť do fondu sedadiel.
- `GET /v1/leases/{id}/features` — zoznam aktívne držaných modulov pre daný lease.
- `POST /v1/leases/{id}/borrow` — vypožičanie sedadla pre offline roaming na $N$ dní.
- `POST /v1/activations` — node-lock aktivácia viazaná na hardvérový fingerprint stanice.
- `DELETE /v1/activations/{id}` — uvoľnenie node-lock aktivácie.
- `GET /v1/licenses/{key}/file` — stiahnutie `.symlic` licenčného súboru v PEM formáte.
- `GET /v1/revocations/latest` — publikovanie aktuálneho revokačného zoznamu (`.symrl`).
- `GET /v1/.well-known/symbolon-keys` — export verejných podpisových kľúčov vo formáte JWKS (RFC 9964).

### CRA & Compliance API (`/v1/compliance`)
- `GET /v1/compliance/cra` — strojovo čitateľný Cyber Resilience Act (CRA) compliance status, SLA a zraniteľnosti.
- `GET /v1/compliance/sbom` — export oficiálneho CycloneDX v1.6 SBOM vo formáte JSON.

### Administrátorské API (`/admin/v1`)
- `POST /admin/v1/tenants` — registrácia ISV tenanta.
- `POST /admin/v1/products` — vytvorenie produktu a definícia entitlements.
- `POST /admin/v1/policies` — definícia licenčných šablón (TTL, grace periódy, borrow limity).
- `POST /admin/v1/licenses` — vydanie licencie s materializáciou $N$ sedadiel a generovaním Crockford Base32 kľúča.
- `POST /admin/v1/licenses/{id}/revoke` — okamžitá revokácia licencie.
- `GET /admin/v1/entitlements/features` — zoznam definícií funkcií a modulov.
- `POST /admin/v1/entitlements/features` — registrácia nového funkčného modulu.
- `GET /admin/v1/entitlements/suites` — zoznam balíkových suít modulov.
- `POST /admin/v1/entitlements/suites` — vytvorenie balíkovej suity modulov.
- `POST /admin/v1/entitlements/licenses/{id}` — priradenie modulu a kvóty k licencii.
- `GET /admin/v1/entitlements/usage` — live telemetria vyťaženia modulov a súbehu sedadiel.
- `GET /admin/v1/keys` — inventár podpisových kľúčov (aktívne, deprecované a revokované).
- `POST /admin/v1/keys/rotate` — bezvýpadková rotácia nového podpisového kľúča (ES256 / hybrid).
- `POST /admin/v1/keys/{kid}/revoke` — okamžitá revokácia kompromitovaného kľúča a jeho vyradenie z JWKS.
- `GET /admin/v1/reports/concurrency` — analytika vyťaženia floating licencií a počtu odmietnutí.
- `GET /admin/v1/audit` — prehľadávanie kryptograficky reťazeného auditného ledgeru.
- `GET /admin/v1/licenses/{id}/rules` — získanie deklaratívnych politických pravidiel licencie (FLT-24).
- `PUT /admin/v1/licenses/{id}/rules` — aktualizácia pravidiel, synchronizácia rezervácií sedadiel (`reserve`) a audit `policy.rules_updated` (FLT-23, FLT-25).
- `POST /admin/v1/licenses/{id}/rules/simulate` — interaktívna simulácia vyhodnotenia pravidiel bez zmeny stavu licencie.

### Synchronizačné API pre Relay (`/relay/v1`)
- `POST /relay/v1/register` — registrácia on-premise relay uzla a vystavenie bezpečného API kľúča.
- `POST /relay/v1/grants:request` — delegovanie kapacity sedadiel (`SeatGrant`) lokálnemu serveru.
- `POST /relay/v1/usage` — dávkový príjem auditných a telemetrických záznamov z relayov.

### Offline & Air-Gap API (`/v1/offline`)
- `POST /v1/offline/grants` — spracovanie offline `.symreq` požiadavky, validácia `usageDigest` v ledgeri a vystavenie `.symgrant`.
- `POST /v1/offline/activations` — generovanie offline node-lock `.symlic` súboru viazaného na HW fingerprint.

### Observabilita & Zdravie
- `GET /metrics` — Prometheus formát metrík (v0.0.4).
- `GET /health` — základná odozva služby.
- `GET /health/live` — Kubernetes liveness probe (beh procesu).
- `GET /health/ready` — Kubernetes readiness probe (overenie spojenia s databázou).

---

## Výsledky Testovania

Všetkých 10 testovacích projektov má 100% úspešnosť testov bez zlyhania (656 testov v .NET 10):

```text
Passed!  - Failed: 0, Passed:  57, Skipped: 0, Total:  57 - Achilles.Crypto.Tests.dll
Passed!  - Failed: 0, Passed:  61, Skipped: 0, Total:  61 - Achilles.Format.Tests.dll
Passed!  - Failed: 0, Passed:  24, Skipped: 0, Total:  24 - Achilles.Protocol.Tests.dll
Passed!  - Failed: 0, Passed: 127, Skipped: 0, Total: 127 - Achilles.Domain.Tests.dll
Passed!  - Failed: 0, Passed:  16, Skipped: 0, Total:  16 - Achilles.Relay.Tests.dll
Passed!  - Failed: 0, Passed:  39, Skipped: 0, Total:  39 - Achilles.Client.Tests.dll
Passed!  - Failed: 0, Passed:  66, Skipped: 0, Total:  66 - Achilles.Cli.Tests.dll
Passed!  - Failed: 0, Passed:  24, Skipped: 0, Total:  24 - Achilles.Data.Tests.dll
Passed!  - Failed: 0, Passed:   8, Skipped: 0, Total:   8 - Achilles.Operator.Tests.dll
Passed!  - Failed: 0, Passed: 234, Skipped: 0, Total: 234 - Achilles.ControlPlane.Tests.dll

Viacjazyčné SDK & WebAssembly testovacie sady:
Passed!  - Failed: 0, Passed:  53, Skipped: 0, Total:  53 - Python SDK (unittest)
Passed!  - Failed: 0, Passed:  19, Skipped: 0, Total:  19 - WebAssembly / WebCrypto SDK (node:test)
Passed!  - Failed: 0, Passed:  13, Skipped: 0, Total:  13 - Java SDK (JUnit 5)
Passed!  - Failed: 0, Passed:  11, Skipped: 0, Total:  11 - Go SDK (testing)
Passed!  - Failed: 0, Passed:  12, Skipped: 0, Total:  12 - Rust SDK (cargo test suite)

Celkovo: 764 úspešných automatizovaných testov (656 .NET + 53 Python + 19 Node/Wasm + 13 Java + 11 Go + 12 Rust), 0 zlyhaní, 0 chýb.
```

---

## Dokumentácia a Špecifikácia

- **[Návody a Príručky](docs/README.md)**:
  - **[Integration Quickstart Guide](docs/quickstart-guide.md)** (krok za krokom pre C#, Python, Rust, C/C++)
  - **[Návod na Migráciu z FlexNetu](docs/migracia-z-flexnetu.md)** (komparácia, options file mapping, dual-run stratégia)
  - [Manažérske zhrnutie a analýza trhu](docs/01-manazerske-zhrnutie.md)
  - [Doménový model](docs/04-domenovy-model.md)
  - [Architektúra a ADR rozhodnutia](docs/06-architektura.md)
  - [Bezpečnosť a modely hrozieb](docs/09-bezpecnost.md)
  - [Testovacia stratégia](docs/10-testovacia-strategia.md)
- **[Normatívna Otvorená Špecifikácia (`spec/`)](spec/README.md)** (licencovaná pod [CC BY 4.0](spec/LICENSE)):
  - [Artefakty a formáty](spec/01-artefakty.md)
  - [Formát licenčného kľúča](spec/02-license-key.md)
  - [Špecifikácia súboru `symlic/1`](spec/03-symlic-1.md)
  - [Lease Token](spec/04-lease-token.md)
  - [Seat Grant](spec/05-seat-grant.md)
  - [Revokačné zoznamy](spec/06-revocation-list.md)
  - [Floating protokol](spec/07-floating-protokol.md)
  - [Hardvérový fingerprint stanice](spec/08-fingerprint.md)

---

## Licencovanie

Projekt využíva **rozdelené licencovanie** podľa architektonického rozhodnutia ADR-006:

| Komponent | Cesta | Licencia | Účel |
|---|---|---|---|
| **Klientske SDK & Knižnice** | `src/Achilles.{Client,Format,Crypto,Protocol}`, `sdk/` | **Apache-2.0** | Umožňuje bezpečné statické aj dynamické linkovanie do proprietárnych komerčných aplikácií ISV dodávateľov. |
| **Server & Infraštruktúra** | `src/Achilles.{ControlPlane,Relay,Data,Domain,Cli}`, `deploy/` | **AGPL-3.0-only** | Zabezpečuje, že vylepšenia infraštruktúry a servera zostávajú open-source pod OSI licenciou. |
| **Otvorená Špecifikácia** | `spec/` | **CC BY 4.0** | Umožňuje komukoľvek nezávisle implementovať licenčné formáty a protokol. |

Podrobnosti o autorských právach a pravidlách pre prispievanie nájdete v súbore **[LICENSING.md](LICENSING.md)**.
