# Symbolon — Open-Source Cloud & On-Premise Licenčný Server

[![Platform](https://img.shields.io/badge/.NET-10.0%20LTS-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Language](https://img.shields.io/badge/C%23-14.0-239120?logo=csharp)](https://learn.microsoft.com/dotnet/csharp/)
[![Tests](https://img.shields.io/badge/tests-78%20passed-brightgreen)](#výsledky-testovania)
[![License: AGPL-3.0](https://img.shields.io/badge/license-AGPL--3.0-blue.svg)](LICENSE)
[![License: Apache-2.0](https://img.shields.io/badge/client%20SDK-Apache--2.0-blue.svg)](LICENSES/Apache-2.0.txt)
[![Crypto](https://img.shields.io/badge/cryptography-ES256%20%2B%20ML--DSA--65%20(PQC)-orange)](#kryptografia)

**Symbolon** je podnikový licenčný server navrhnutý pre nezávislých dodávateľov softvéru (ISV), ktorí predávajú softvér nasadzovaný v cloude, on-premise, na desktope, v priemyselných zariadeniach alebo v striktne izolovaných (air-gapped) prostrediach.

Rieši to, čo dnešné cloud-first platformy ponúkajú iba ako obmedzený doplnok: **vysokovýkonné plávajúce (concurrent / floating) licencie**, deterministické účtovanie sedadiel bez distribuovaných zámkov, lokálne on-premise relay uzly s delegovanou kapacitou, offline validáciu a **post-kvantovú kryptografiu** (FIPS 204).

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
│   ├── Symbolon.Cli            (AGPL-3.0)    - CLI nástroj pre správu kľúčov, vydávanie licencií a diagnostiku
│   ├── Symbolon.Data           (AGPL-3.0)    - EF Core 10, Npgsql 10, multi-tenancy, materializované sedadlá, ledger
│   └── Symbolon.ControlPlane   (AGPL-3.0)    - Centrálny server (Public /v1, Admin /admin/v1, Relay /relay/v1, /metrics)
├── deploy/
│   ├── docker/
│   │   ├── Dockerfile.controlplane           - Multi-stage produkčný kontajner ControlPlane (.NET 10)
│   │   └── Dockerfile.relay                  - Multi-stage kontajner pre on-premise Relay s perzistentným SQLite
│   ├── docker-compose.yml                    - Kompletný stack: Postgres 17, ControlPlane, Relay, Prometheus, Grafana
│   ├── prometheus/
│   │   └── prometheus.yml                    - Konfigurácia zberu metrík z ControlPlane
│   ├── grafana/
│   │   ├── symbolon-dashboard.json           - Predpripravený monitorovací dashboard
│   │   └── provisioning/                     - Automatické prepojenie Prometheus dátového zdroja a dashboardu
│   ├── helm/symbolon/                        - Kubernetes Helm Chart (Deployment, Service, Ingress, HPA, Secret)
│   └── systemd/
│       └── symbolon-relay.service            - Bezpečný tvrdený systemd unit pre Linux distribúciu
└── tests/
    ├── Symbolon.Crypto.Tests                 - Testy kryptografických primitív a hybridných podpisov
    ├── Symbolon.Format.Tests                 - Validácia formátu symlic/1, Crockford Base32 a PEM obálky
    ├── Symbolon.Protocol.Tests               - Serializácia DTO a kanonizácia hardvérového fingerprintu
    ├── Symbolon.Domain.Tests                 - Testy LeaseEngine, TTL expirácie a idempotencie
    ├── Symbolon.Relay.Tests                  - Integračné testy on-premise Relay servera
    ├── Symbolon.Client.Tests                 - Testy ISV SDK, automatického obnovovania a jitteru
    ├── Symbolon.Cli.Tests                    - Testy príkazového riadka
    ├── Symbolon.Data.Tests                   - Testy PostgreSQL / SQLite úložiska sedadiel a auditného reťazca
    └── Symbolon.ControlPlane.Tests           - Komplexné integračné testy API, metrík a health sond
```

---

## Rýchly Štart (Quickstart)

### Požiadavky
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) alebo novší
- [Docker](https://www.docker.com/) a [Docker Compose](https://docs.docker.com/compose/) (pre kontajnerové nasadenie)

### 1. Zostavenie riešenia a spustenie testov

```bash
# Naklonovanie repozitára
git clone https://github.com/MAGORS-organisation/License-server.git
cd License-server

# Zostavenie celého solution
dotnet build Symbolon.slnx

# Spustenie všetkých 75 unit a integračných testov
dotnet test Symbolon.slnx
```

### 2. Spustenie celého prostredia cez Docker Compose

Spustí naraz **PostgreSQL 17**, **ControlPlane**, **Relay**, **Prometheus** a **Grafanu**:

```bash
docker compose -f deploy/docker-compose.yml up -d
```

Po spustení sú dostupné tieto služby:
- **Symbolon ControlPlane API:** `http://localhost:8080`
  - OpenAPI 3.1 dokumentácia: `http://localhost:8080/openapi/v1.json`
  - Health check: `http://localhost:8080/health/ready`
  - Prometheus metriky: `http://localhost:8080/metrics`
- **Symbolon On-Premise Relay:** `http://localhost:8081`
- **Prometheus UI:** `http://localhost:9090`
- **Grafana Dashboard:** `http://localhost:3000` (prihlásenie: `admin` / `admin`)
  - Predkonfigurovaný dashboard *„Symbolon Licensing Dashboard“* s grafmi sedadiel a upsell indikátormi.

Zastavenie stacku:
```bash
docker compose -f deploy/docker-compose.yml down
```

### 3. Lokálne spustenie bez Dockeru

```bash
# Spustenie Control Plane servera (predvolene SQLite alebo PostgreSQL podľa connection stringu)
dotnet run --project src/Symbolon.ControlPlane

# V druhom termináli spustenie on-premise Relay servera
dotnet run --project src/Symbolon.Relay
```

---

## Ako Používať `Symbolon.Cli` a Inštalačný Sprievodca (TUI Wizard)

Nástroj príkazového riadka poskytuje interaktívneho inštalačného sprievodcu, správu kľúčov, vydávanie licencií a ich overovanie:

```bash
# 1. Spustenie interaktívneho inštalačného sprievodcu (TUI Wizard)
# (alebo jednoducho spustite `dotnet run --project src/Symbolon.Cli` bez argumentov v termináli)
dotnet run --project src/Symbolon.Cli -- setup

# 2. Vygenerovanie nového páru podpisových kľúčov (ES256 alebo hybrid)
dotnet run --project src/Symbolon.Cli -- key gen -a es256 -o ./my-keys

# 3. Vydanie licenčného súboru .symlic
dotnet run --project src/Symbolon.Cli -- lic issue \
  --product "cad-pro" \
  --customer "Acme Corporation" \
  --seats 10 \
  --type floating \
  --key ./my-keys/private.jwk \
  --out license.symlic

# 4. Diagnostika a validácia licenčného súboru (kontrola podpisov, platnosti a claims)
dotnet run --project src/Symbolon.Cli -- doctor --file license.symlic --key ./my-keys/public.jwk
```

### Čo dokáže interaktívny TUI sprievodca (`setup` / `wizard`):
- **Inštalácia ControlPlane:** Interaktívne nastaví sieťový port, databázový engine (SQLite / PostgreSQL), vygeneruje podpisové kľúče (ES256 + ML-DSA-65), vytvorí `appsettings.json` a pripraví spúšťacie skripty pre Windows (`.cmd`, `.ps1`) a Linux (`.sh`).
- **Inštalácia On-Premise Relay:** Nakonfiguruje lokálny relay server, perzistentné SQLite úložisko, prepojenie na centrály ControlPlane a vygeneruje Linux `systemd` unit.
- **Rýchle vystavenie licencie:** Vygeneruje Crockford Base32 kľúč s kontrolným súčtom CRC-32C, podpíše JWS dokument a uloží formátovaný `.symlic` súbor v PEM obálke.
- **Systémový lekár (Doctor):** Diagnostikuje pripravenosť prostredia, overí podporu pre post-kvantovú kryptografiu (FIPS 204) a vypočíta hardvérový fingerprint stanice.

---

## Integrácia Klientskeho SDK (`Symbolon.Client`)

Pre vývojárov ISV aplikácií je určená knižnica `Symbolon.Client` (licencovaná pod permisívnou licenciou **Apache-2.0**):

```csharp
using Symbolon.Client;

// Konfigurácia klienta s primárnym serverom a voliteľným on-premise relay fallbackom
var options = new SymbolonClientOptions
{
    ServerUrl = new Uri("http://license.mycompany.com:8080"),
    RelayUrl = new Uri("http://local-relay:8081"),
    ProductCode = "cad-pro",
    HeartbeatInterval = TimeSpan.FromSeconds(30),
    HeartbeatJitterFactor = 0.10 // ±10% náhodný rozptyl
};

await using var client = new SymbolonClient(options);

// Získanie plávajúceho sedadla (automaticky posiela heartbeat na pozadí)
await using (var lease = await client.AcquireSeatAsync(
    licenseKey: "SYM-9ABC-DEF2-3456-7890",
    features: ["advanced-rendering", "export-step"],
    cancellationToken: ct))
{
    Console.WriteLine($"Sedadlo úspešne alokované! Lease ID: {lease.Token.LeaseId}");
    Console.WriteLine($"Platnosť do: {lease.Token.ExpiresAt}");

    // Beh vašej aplikácie...
    // Počas behu sa lease automaticky periodicky obnovuje.
}
// Po opustení using bloku sa sedadlo okamžite a bezpečne uvoľní späť do fondu.
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

### Administrátorské API (`/admin/v1`)
- `POST /admin/v1/tenants` — registrácia ISV tenanta.
- `POST /admin/v1/products` — vytvorenie produktu a definícia entitlements.
- `POST /admin/v1/policies` — definícia licenčných šablón (TTL, grace periódy, borrow limity).
- `POST /admin/v1/licenses` — vydanie licencie s materializáciou $N$ sedadiel a generovaním Crockford Base32 kľúča.
- `POST /admin/v1/licenses/{id}/revoke` — okamžitá revokácia licencie.
- `GET /admin/v1/reports/concurrency` — analytika vyťaženia floating licencií a počtu odmietnutí.
- `GET /admin/v1/audit` — prehľadávanie kryptograficky reťazeného auditného ledgeru.

### Synchronizačné API pre Relay (`/relay/v1`)
- `POST /relay/v1/register` — registrácia on-premise relay uzla.
- `POST /relay/v1/grants:request` — delegovanie kapacity sedadiel (`SeatGrant`) lokálnemu serveru.
- `POST /relay/v1/usage` — dávkový príjem auditných a telemetrických záznamov z relayov.

### Observabilita & Zdravie
- `GET /metrics` — Prometheus formát metrík (v0.0.4).
- `GET /health` — základná odozva služby.
- `GET /health/live` — Kubernetes liveness probe (beh procesu).
- `GET /health/ready` — Kubernetes readiness probe (overenie spojenia s PostgreSQL databázou).

---

## Observabilita a Metriky

ControlPlane exportuje tieto kľúčové doménové metriky cez Prometheus rozhranie:

| Metrika | Typ | Popis |
|---|---|---|
| `symbolon_seats_active` | Gauge | Aktuálny počet alokovaných floating sedadiel v reálnom čase. |
| `symbolon_checkout_denied_total` | Counter | Počet zamietnutých požiadaviek z dôvodu vyčerpanej kapacity (**kľúčový upsell indikátor** pre ISV). |
| `symbolon_lease_renewed_total` | Counter | Celkový počet úspešných predĺžení platnosti sedadiel (heartbeats). |
| `symbolon_lease_released_total` | Counter | Celkový počet explicitne uvoľnených sedadiel. |
| `symbolon_clock_skew_detected_total` | Counter | Počet zachytených časových anomálií a posunov systémových hodín. |

---

## Nasadenie v Kubernete (Helm)

V adresári [`deploy/helm/symbolon/`](deploy/helm/symbolon/) sa nachádza produkčný Helm chart:

```bash
# Inštalácia alebo upgrade pomocou Helmu
helm upgrade --install symbolon deploy/helm/symbolon/ \
  --namespace symbolon --create-namespace \
  --set database.host="postgres.production.svc" \
  --set database.password="tajne_heslo"
```

Chart obsahuje:
- Horizontálne škálovanie podov (**HPA**) riadené CPU a pamäťou.
- Nastavené bezpečnostné kontexty (`runAsNonRoot: true`, `readOnlyRootFilesystem: true`, zhodenie `ALL` capabilities).
- Automatické mapovanie Prometheus anotácií pre automatický discovery v Prometheus Operator / VictoriaMetrics.

---

## Výsledky Testovania

Všetkých 9 projektov má 100% úspešnosť testov bez zlyhania:

```text
Passed!  - Failed: 0, Passed:  9, Skipped: 0, Total:  9 - Symbolon.Crypto.Tests.dll
Passed!  - Failed: 0, Passed: 26, Skipped: 0, Total: 26 - Symbolon.Format.Tests.dll
Passed!  - Failed: 0, Passed:  6, Skipped: 0, Total:  6 - Symbolon.Protocol.Tests.dll
Passed!  - Failed: 0, Passed:  6, Skipped: 0, Total:  6 - Symbolon.Domain.Tests.dll
Passed!  - Failed: 0, Passed:  7, Skipped: 0, Total:  7 - Symbolon.Relay.Tests.dll
Passed!  - Failed: 0, Passed:  4, Skipped: 0, Total:  4 - Symbolon.Client.Tests.dll
Passed!  - Failed: 0, Passed:  6, Skipped: 0, Total:  6 - Symbolon.Cli.Tests.dll
Passed!  - Failed: 0, Passed:  5, Skipped: 0, Total:  5 - Symbolon.Data.Tests.dll
Passed!  - Failed: 0, Passed:  9, Skipped: 0, Total:  9 - Symbolon.ControlPlane.Tests.dll

Celkovo: 78 úspešných testov, 0 zlyhaní, 0 chýb. Trvanie: ~6 sekúnd.
```

---

## Dokumentácia a Špecifikácia

- **[Normatívna Otvorená Špecifikácia (`spec/`)](spec/README.md)** (licencovaná pod [CC BY 4.0](spec/LICENSE)):
  - [Artefakty a formáty](spec/01-artefakty.md)
  - [Formát licenčného kľúča](spec/02-license-key.md)
  - [Špecifikácia súboru `symlic/1`](spec/03-symlic-1.md)
  - [Lease Token](spec/04-lease-token.md)
  - [Seat Grant](spec/05-seat-grant.md)
  - [Revokačné zoznamy](spec/06-revocation-list.md)
  - [Floating protokol](spec/07-floating-protokol.md)
  - [Hardvérový fingerprint stanice](spec/08-fingerprint.md)
- **[Architektonické a Strategické Podklady (`docs/`)](docs/README.md)**:
  - [Manažérske zhrnutie a analýza trhu](docs/01-manazerske-zhrnutie.md)
  - [Doménový model](docs/04-domenovy-model.md)
  - [Architektúra a ADR rozhodnutia](docs/06-architektura.md)
  - [Bezpečnosť a modely hrozieb](docs/09-bezpecnost.md)
  - [Testovacia stratégia](docs/10-testovacia-strategia.md)

---

## Licencovanie

Projekt využíva **rozdelené licencovanie** podľa architektonického rozhodnutia ADR-006:

| Komponent | Cesta | Licencia | Účel |
|---|---|---|---|
| **Klientske SDK & Knižnice** | `src/Symbolon.{Client,Format,Crypto,Protocol}` | **Apache-2.0** | Umožňuje bezpečné statické aj dynamické linkovanie do proprietárnych komerčných aplikácií ISV dodávateľov. |
| **Server & Infraštruktúra** | `src/Symbolon.{ControlPlane,Relay,Data,Domain,Cli}`, `deploy/` | **AGPL-3.0-only** | Zabezpečuje, že vylepšenia infraštruktúry a servera zostávajú open-source pod OSI licenciou. |
| **Otvorená Špecifikácia** | `spec/` | **CC BY 4.0** | Umožňuje komukoľvek nezávisle implementovať licenčné formáty a protokol. |

Podrobnosti o autorských právach a pravidlách pre prispievanie nájdete v súbore **[LICENSING.md](LICENSING.md)**.
