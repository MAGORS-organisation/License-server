# Symbolon — Open-Source Cloud & On-Premise Licenčný Server

[![Platform](https://img.shields.io/badge/.NET-10.0%20LTS-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Language](https://img.shields.io/badge/C%23-14.0-239120?logo=csharp)](https://learn.microsoft.com/dotnet/csharp/)
[![Tests](https://img.shields.io/badge/tests-231%20passed%20(+13%20Python%2C%20+11%20Wasm)-brightgreen)](#výsledky-testovania)
[![License: AGPL-3.0](https://img.shields.io/badge/license-AGPL--3.0-blue.svg)](LICENSE)
[![License: Apache-2.0](https://img.shields.io/badge/client%20SDK-Apache--2.0-blue.svg)](LICENSES/Apache-2.0.txt)
[![CRA Compliant](https://img.shields.io/badge/CRA%20Compliance-EU%202024%2F2847-success)](SECURITY.md)
[![SBOM: CycloneDX v1.6](https://img.shields.io/badge/SBOM-CycloneDX%20v1.6-blue)](spec/README.md)
[![Crypto](https://img.shields.io/badge/cryptography-ES256%20%2B%20ML--DSA--65%20(PQC)-orange)](#kryptografia)

**Symbolon** je podnikový licenčný server navrhnutý pre nezávislých dodávateľov softvéru (ISV), ktorí predávajú softvér nasadzovaný v cloude, on-premise, na desktope, v priemyselných zariadeniach alebo v striktne izolovaných (air-gapped) prostrediach.

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
10. **Air-Gap Offline Portál & Scenáre**
    - Plnohodnotná podpora pre striktne izolované priemyselné závody a bezsieťové prostredia.
    - Výmena `.symreq` → `.symgrant` cez USB alebo Web UI s povinným hash-chain uzlom `usageDigest` zabraňujúcim neoprávnenému generovaniu offline grantov bez evidencie spotreby.
    - Offline node-lock aktivácie viazané na hardvérový fingerprint stanice.
11. **Viacjazyčné Klientske SDK (C#, Python, Rust, C/C++)**
    - Oficiálne klientske knižnice pod licenciou **Apache-2.0** v priečinku `sdk/` s automatickým vláknom pre heartbeat, adaptívnym jitterom ±10% a RAII / kontextovým manažérom.
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

---

## Architektúra Riešenia

Celé riešenie je postavené na **.NET 10 LTS** v súlade s normatívnou [špecifikáciou](spec/README.md) a rozdelené do modulárnych projektov:

```
Symbolon.slnx
├── src/
│   ├── Symbolon.Crypto         (Apache-2.0)  - ES256, ML-DSA-65 (FIPS 204), KeyRing, JWK/JWKS (RFC 9964)
│   ├── Symbolon.Format         (Apache-2.0)  - symlic/1 JWS, Crockford Base32 + CRC-32C, PEM Armor, LIC-34
│   ├── Symbolon.Protocol       (Apache-2.0)  - symlease+jwt, SHA-256 fingerprint kanonizácia, DTO kontrakty
│   ├── Symbolon.Domain         (AGPL-3.0)    - LeaseEngine, ISeatStore, IAuditLedger, stavové automaty, idempotencia
│   ├── Symbolon.Relay          (AGPL-3.0)    - Samostatný on-premise relay server (SQLite WAL, Minimal API)
│   ├── Symbolon.Client         (Apache-2.0)  - Klientske ISV SDK, IAsyncDisposable SeatLease, automatický heartbeat
│   ├── Symbolon.Cli            (AGPL-3.0)    - CLI nástroj pre správu kľúčov, vydávanie licencií, SBOM a diagnostiku
│   ├── Symbolon.Data           (AGPL-3.0)    - EF Core 10, Npgsql 10, multi-tenancy, materializované sedadlá, ledger
│   ├── Symbolon.Operator       (AGPL-3.0)    - Kubernetes Operator, CRDs, reconcilery, manifest generátory
│   └── Symbolon.ControlPlane   (AGPL-3.0)    - Centrálny server (Public, Admin, Relay, Compliance, /metrics, Retro TUI)
├── deploy/
│   ├── config/                               - Vzorové konfiguračné súbory a mapovania z FlexNetu (.opt -> policy.json)
│   ├── docker/                               - Multi-stage Dockerfile pre ControlPlane a Relay
│   ├── docker-compose.yml                    - Kompletný stack: Postgres 17, ControlPlane, Relay, Prometheus, Grafana
│   ├── prometheus/                           - Prometheus konfigurácia zberu metrík
│   ├── grafana/                              - Provisioning a predkonfigurovaný dashboard
│   ├── helm/symbolon/                        - Kubernetes Helm Chart (Deployment, Service, Ingress, PDB, NetworkPolicy, ServiceMonitor)
│   └── systemd/                              - Tvrdený Linux systemd unit pre on-premise Relay
├── sdk/
│   ├── wasm/                                 - WebAssembly & WebCrypto JS/TS SDK (@symbolon/validator, 100% offline)
│   ├── python/symbolon/                      - Python SDK (pip installable, context manager, daemon heartbeat)
│   ├── rust/symbolon/                        - Rust crate (Tokio async, RAII Drop pattern)
│   └── c_cpp/                                - C99 / C++17 single-header knižnica (ScopedLease RAII)
└── tests/
    ├── Symbolon.Crypto.Tests                 - Testy kryptografických primitív a hybridných podpisov
    ├── Symbolon.Format.Tests                 - Validácia formátu symlic/1, Crockford Base32 a PEM obálky
    ├── Symbolon.Protocol.Tests               - Serializácia DTO a kanonizácia hardvérového fingerprintu
    ├── Symbolon.Domain.Tests                 - Testy LeaseEngine, TTL expirácie a idempotencie
    ├── Symbolon.Relay.Tests                  - Integračné testy on-premise Relay servera
    ├── Symbolon.Client.Tests                 - Testy ISV SDK, automatického obnovovania a jitteru
    ├── Symbolon.Cli.Tests                    - Testy príkazového riadka
    ├── Symbolon.Data.Tests                   - Testy PostgreSQL / SQLite úložiska sedadiel a auditného reťazca
    ├── Symbolon.Operator.Tests               - Testy Kubernetes Operatora, reconcilerov a manifestov
    └── Symbolon.ControlPlane.Tests           - Komplexné testy API, metrík, CRA reportov a CycloneDX SBOM
```

---

## Rýchly Štart (Quickstart)

### 1. Zostavenie riešenia a spustenie testov

```bash
# Naklonovanie repozitára
git clone https://github.com/MAGORS-organisation/License-server.git
cd License-server

# Zostavenie celého solution
dotnet build Symbolon.slnx

# Spustenie všetkých 216 testov v .NET (+ 13 v Pythone, + 11 v Node.js/Wasm)
dotnet test Symbolon.slnx
python -m unittest discover sdk/python/symbolon/tests
node --test sdk/wasm/tests/validator.test.js
```

### 2. Spustenie celého prostredia cez Docker Compose

Spustí naraz **PostgreSQL 17**, **ControlPlane** (s Retro Web TUI), **Relay**, **Prometheus** a **Grafanu**:

```bash
docker compose -f deploy/docker-compose.yml up -d
```

Dostupné služby:
- **Symbolon Retro Web UI & ControlPlane API:** `http://localhost:8080`
  - Retro TUI Konzola pre správu: `http://localhost:8080/`
  - CycloneDX v1.6 SBOM: `http://localhost:8080/v1/compliance/sbom`
  - Cyber Resilience Act (CRA) report: `http://localhost:8080/v1/compliance/cra`
  - OpenAPI 3.1 dokumentácia: `http://localhost:8080/openapi/v1.json`
  - Health check: `http://localhost:8080/health/ready`
  - Prometheus metriky: `http://localhost:8080/metrics`
- **Symbolon On-Premise Relay:** `http://localhost:8081`
- **Prometheus UI:** `http://localhost:9090`
- **Grafana Dashboard:** `http://localhost:3000` (prihlásenie: `admin` / `admin`)

---

## Ukážka Klientskej Integrácie (4 Jazyky)

Podrobný návod krok za krokom nájdete v **[Integration Quickstart Guide](docs/quickstart-guide.md)**.

### C# (.NET 10)
```csharp
using Symbolon.Client;

var options = new SymbolonClientOptions {
    ServerUri = new Uri("http://localhost:8080"),
    LicenseKey = "SYM-9ABC-DEF2-3456-7890"
};
using var client = new SymbolonClient(options);

await using var lease = await client.AcquireSeatAsync(["cad-core", "rendering"]);
if (lease.Acquired) {
    Console.WriteLine($"Sedadlo #{lease.SeatNo} pridelené! Heartbeat beží na pozadí.");
    // Beh vašej aplikácie...
}
// Sedadlo sa automaticky uvoľní pri opustení bloku.
```

### Python (3.10+)
```python
from symbolon import SymbolonClient

client = SymbolonClient("http://localhost:8080", product_code="cad-pro")
with client.acquire_seat("SYM-9ABC-DEF2-3456-7890", features=["cad-core"]) as lease:
    print(f"Sedadlo #{lease.seat_number} alokované. Aplikácia beží...")
# Automatické uvoľnenie po opustení bloku with
```

### Rust (2021 Edition)
```rust
use symbolon_client::SymbolonClient;

let client = SymbolonClient::new("http://localhost:8080", "cad-pro");
let lease = client.acquire_seat("SYM-9ABC-DEF2-3456-7890")?;
println!("Sedadlo #{} alokované!", lease.seat_number());
// Pri opustení scope sa vďaka RAII Drop sedadlo okamžite vráti do fondu.
```

### C99 & C++17
```cpp
#include "symbolon.h"

symbolon_client_t* client = nullptr;
symbolon_client_create("http://localhost:8080", "cad-pro", &client);

symbolon_lease_t* raw_lease = nullptr;
if (symbolon_acquire_seat(client, "SYM-9ABC-DEF2-3456-7890", &raw_lease) == SYMBOLON_OK) {
    symbolon::ScopedLease lease(raw_lease); // C++ RAII wrapper
    // ... výkonný kód aplikácie ...
}
symbolon_client_destroy(client);
```

---

## Ako Používať `Symbolon.Cli`

```bash
# 1. Spustenie interaktívneho inštalačného sprievodcu (TUI Wizard)
dotnet run --project src/Symbolon.Cli -- setup

# 2. Vygenerovanie nového páru podpisových kľúčov (ES256 alebo hybrid ML-DSA-65)
dotnet run --project src/Symbolon.Cli -- key gen -a es256 -o ./my-keys

# 3. Vydanie licenčného súboru .symlic
dotnet run --project src/Symbolon.Cli -- lic issue \
  --product "cad-pro" \
  --customer "Acme Corporation" \
  --seats 10 \
  --type floating \
  --key ./my-keys/private.jwk \
  --out license.symlic

# 4. Export SBOM v štandarde CycloneDX v1.6
dotnet run --project src/Symbolon.Cli -- sbom --out sbom.json

# 5. Overenie integrity binárneho artefaktu
dotnet run --project src/Symbolon.Cli -- verify-artifact --file app.dll --expected-hash <sha256>

# 6. Diagnostika prostredia (Doctor)
dotnet run --project src/Symbolon.Cli -- doctor --file license.symlic --key ./my-keys/public.jwk
```

---

## Prehľad API Endpointov

### Verejné Klientske API (`/v1`)
- `POST /v1/leases` — získanie plávajúceho sedadla (checkout) a vydanie podpísaného JWS lease tokenu.
- `POST /v1/leases/{id}/renew` — predĺženie platnosti sedadla (heartbeat) s kontrolou seq monotónnosti.
- `DELETE /v1/leases/{id}` — okamžité uvoľnenie sedadla.
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
- `GET /admin/v1/keys` — inventár podpisových kľúčov (aktívne, deprecované a revokované).
- `POST /admin/v1/keys/rotate` — bezvýpadková rotácia nového podpisového kľúča (ES256 / hybrid).
- `POST /admin/v1/keys/{kid}/revoke` — okamžitá revokácia kompromitovaného kľúča a jeho vyradenie z JWKS.
- `GET /admin/v1/reports/concurrency` — analytika vyťaženia floating licencií a počtu odmietnutí.
- `GET /admin/v1/audit` — prehľadávanie kryptograficky reťazeného auditného ledgeru.

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

Všetkých 10 testovacích projektov má 100% úspešnosť testov bez zlyhania:

```text
Passed!  - Failed: 0, Passed: 28, Skipped: 0, Total: 28 - Symbolon.Crypto.Tests.dll
Passed!  - Failed: 0, Passed: 37, Skipped: 0, Total: 37 - Symbolon.Format.Tests.dll
Passed!  - Failed: 0, Passed: 16, Skipped: 0, Total: 16 - Symbolon.Protocol.Tests.dll
Passed!  - Failed: 0, Passed: 35, Skipped: 0, Total: 35 - Symbolon.Domain.Tests.dll
Passed!  - Failed: 0, Passed:  9, Skipped: 0, Total:  9 - Symbolon.Relay.Tests.dll
Passed!  - Failed: 0, Passed: 11, Skipped: 0, Total: 11 - Symbolon.Client.Tests.dll
Passed!  - Failed: 0, Passed: 22, Skipped: 0, Total: 22 - Symbolon.Cli.Tests.dll
Passed!  - Failed: 0, Passed: 10, Skipped: 0, Total: 10 - Symbolon.Data.Tests.dll
Passed!  - Failed: 0, Passed:  8, Skipped: 0, Total:  8 - Symbolon.Operator.Tests.dll
Passed!  - Failed: 0, Passed: 71, Skipped: 0, Total: 71 - Symbolon.ControlPlane.Tests.dll

Viacjazyčné SDK & WebAssembly testovacie sady:
Passed!  - Failed: 0, Passed: 13, Skipped: 0, Total: 13 - Python SDK (unittest)
Passed!  - Failed: 0, Passed: 11, Skipped: 0, Total: 11 - WebAssembly / WebCrypto SDK (node:test)

Celkovo: 271 úspešných automatizovaných testov (247 .NET + 13 Python + 11 Node/Wasm), 0 zlyhaní, 0 chýb.
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
| **Klientske SDK & Knižnice** | `src/Symbolon.{Client,Format,Crypto,Protocol}`, `sdk/` | **Apache-2.0** | Umožňuje bezpečné statické aj dynamické linkovanie do proprietárnych komerčných aplikácií ISV dodávateľov. |
| **Server & Infraštruktúra** | `src/Symbolon.{ControlPlane,Relay,Data,Domain,Cli}`, `deploy/` | **AGPL-3.0-only** | Zabezpečuje, že vylepšenia infraštruktúry a servera zostávajú open-source pod OSI licenciou. |
| **Otvorená Špecifikácia** | `spec/` | **CC BY 4.0** | Umožňuje komukoľvek nezávisle implementovať licenčné formáty a protokol. |

Podrobnosti o autorských právach a pravidlách pre prispievanie nájdete v súbore **[LICENSING.md](LICENSING.md)**.
