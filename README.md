# Symbolon — Open-Source Cloud & On-Premise Licenčný Server

[![Platform](https://img.shields.io/badge/.NET-10.0%20LTS-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Language](https://img.shields.io/badge/C%23-14.0-239120?logo=csharp)](https://learn.microsoft.com/dotnet/csharp/)
[![Tests](https://img.shields.io/badge/tests-127%20passed%20(+3%20Python)-brightgreen)](#výsledky-testovania)
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
12. **Hardware Enclave Attestation (R6)**
    - Podpora overovania kryptografických citácií TPM 2.0 PCR a Confidential Computing Enclaves (Intel SGX, AMD SEV-SNP) chránená challenge-response mechanizmom proti replay útokom.
13. **Cloud-Native Zero-Trust Mesh & OCI Bundles**
    - P2P Relay Mesh koordinátor s **Lamportovými logickými hodinami** a disjunktnými partíciami sedadiel zabraňujúci split-brain overage.
    - Štandardizované balíčky OCI Image Manifest v1 pre air-gapped Kubernetes repozitáre (`symbolon oci pack/verify`).
14. **Jednookenné Desktop Prostredie & Živá Telemetria**
    - Kompletná správa servera v jedinom okne s priebežným monitoringom systémových zdrojov (CPU, RAM, DISK, NET, IP, Používateľ) v reálnom čase.

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
│   └── Symbolon.ControlPlane   (AGPL-3.0)    - Centrálny server (Public, Admin, Relay, Compliance, /metrics, Retro TUI)
├── deploy/
│   ├── config/                               - Vzorové konfiguračné súbory a mapovania z FlexNetu (.opt -> policy.json)
│   ├── docker/                               - Multi-stage Dockerfile pre ControlPlane a Relay
│   ├── docker-compose.yml                    - Kompletný stack: Postgres 17, ControlPlane, Relay, Prometheus, Grafana
│   ├── prometheus/                           - Prometheus konfigurácia zberu metrík
│   ├── grafana/                              - Provisioning a predkonfigurovaný dashboard
│   ├── helm/symbolon/                        - Kubernetes Helm Chart (Deployment, Service, Ingress, HPA, Secret)
│   └── systemd/                              - Tvrdený Linux systemd unit pre on-premise Relay
├── sdk/
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

# Spustenie všetkých 98 testov v .NET (+ 2 unit testy v Pythone)
dotnet test Symbolon.slnx
python -m unittest discover sdk/python/symbolon/tests
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

Všetkých 9 projektov má 100% úspešnosť testov bez zlyhania:

```text
Passed!  - Failed: 0, Passed:  9, Skipped: 0, Total:  9 - Symbolon.Crypto.Tests.dll
Passed!  - Failed: 0, Passed: 37, Skipped: 0, Total: 37 - Symbolon.Format.Tests.dll
Passed!  - Failed: 0, Passed:  8, Skipped: 0, Total:  8 - Symbolon.Protocol.Tests.dll
Passed!  - Failed: 0, Passed:  6, Skipped: 0, Total:  6 - Symbolon.Domain.Tests.dll
Passed!  - Failed: 0, Passed:  9, Skipped: 0, Total:  9 - Symbolon.Relay.Tests.dll
Passed!  - Failed: 0, Passed:  5, Skipped: 0, Total:  5 - Symbolon.Client.Tests.dll
Passed!  - Failed: 0, Passed: 10, Skipped: 0, Total: 10 - Symbolon.Cli.Tests.dll
Passed!  - Failed: 0, Passed:  7, Skipped: 0, Total:  7 - Symbolon.Data.Tests.dll
Passed!  - Failed: 0, Passed: 36, Skipped: 0, Total: 36 - Symbolon.ControlPlane.Tests.dll

Celkovo: 127 úspešných testov v .NET (+ 3 unit testy v Pythone), 0 zlyhaní, 0 chýb. Trvanie: ~6 sekúnd.
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
