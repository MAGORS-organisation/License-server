# Symbolon Integration Quickstart Guide

Tento ucelený sprievodca vás krok za krokom prevedie integráciou klientskeho licenčného mechanizmu do vašej aplikácie. Symbolon poskytuje oficiálne odľahčené SDK pre 7 programovacích jazykov a prostredí:

- **C# / .NET 10** (`Symbolon.Client`) — plná asynchrónna podpora, `IAsyncDisposable`, automatický heartbeat na pozadí.
- **Python 3.10+** (`symbolon`) — kontextový manažér `with`, bezobslužný daemon thread pre heartbeat s ±10% jitterom.
- **Go 1.22+** (`sdk/go/symbolon`) — pure-Go bez CGO, goroutine worker s jitterom ±10%, offline grace zotavenie.
- **Java 17+** (`sdk/java/symbolon`) — pre Spring Boot / Quarkus / CAD desktop, `AutoCloseable`, scheduled daemon executor.
- **Rust (2021 Edition)** (`symbolon`) — idiomatický RAII vzor s automatickým uvoľnením cez `Drop` trait.
- **C99 a C++17** (`symbolon.h`) — čisté ANSI C API bez externých závislostí a moderný C++ RAII wrapper (`ScopedLease`).
- **WebAssembly / TypeScript** (`sdk/wasm`) — pre Node.js a prehliadače, offline verifikácia JWS a Post-Quantum pripravenosť.

Všetky klientske knižnice sú licencované pod permisívnou licenciou **Apache-2.0**, čo umožňuje ich bezpečné dynamické aj statické linkovanie do uzavretých komerčných produktov.

---

## 1. Architektonický Model Integrácie

Symbolon funguje na princípe **plávajúcich sedadiel (floating concurrent seats)** s deterministickou alokáciou v $O(1)$:

```
┌────────────────────────────────────────────────────────┐
│               ISV Klientska Aplikácia                  │
│                                                        │
│  1. Spustenie  ──> AcquireSeatAsync(licenseKey)        │
│                         │                              │
│                         ▼                              │
│  2. Práca      ──> Periodický Heartbeat (na pozadí)    │
│                         │                              │
│                         ▼                              │
│  3. Ukončenie  ──> Dispose / Release (uvoľnenie)       │
└─────────────────────────┬──────────────────────────────┘
                          │ HTTP/REST (JSON / JWS)
                          ▼
┌────────────────────────────────────────────────────────┐
│  Symbolon Server (ControlPlane alebo lokálny Relay)    │
│  - Overenie platnosti licencie (.symlic)               │
│  - Atómová rezervácia sedadla (FOR UPDATE SKIP LOCKED) │
│  - Vydanie kryptograficky podpísaného JWS tokenu       │
└────────────────────────────────────────────────────────┘
```

---

## 2. Integrácia v C# / .NET 10

### Inštalácia
Pridajte referenciu na balík `Symbolon.Client` do vášho `.csproj`:

```xml
<ItemGroup>
    <PackageReference Include="Symbolon.Client" Version="1.0.0" />
</ItemGroup>
```

### Minimálny príklad (Hello Floating License)

```csharp
using Symbolon.Client;

// 1. Nastavenie možností klienta
var options = new SymbolonClientOptions
{
    ServerUri = new Uri("http://localhost:8080"),
    LicenseKey = "SYM-9ABC-DEF2-3456-7890",
    HeartbeatInterval = TimeSpan.FromSeconds(30),
    GracePeriod = TimeSpan.FromSeconds(60)
};

// 2. Vytvorenie klienta (IDisposable)
using var client = new SymbolonClient(options);

Console.WriteLine("Žiadam o pridelenie plávajúceho sedadla...");

// 3. Alokácia sedadla cez IAsyncDisposable blok
await using (var lease = await client.AcquireSeatAsync(features: ["3d-engine", "export-pdf"]))
{
    if (!lease.Acquired)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Pridelenie licencie zlyhalo: {lease.Reason}");
        return;
    }

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"✓ Sedadlo #{lease.SeatNo} úspešne pridelené!");
    Console.WriteLine($"  Lease ID: {lease.LeaseId}");
    Console.ResetColor();

    // 4. Reakcia na zmenu stavu (napr. výpadok siete a prechod do grace period)
    lease.StateChanged += (sender, args) =>
    {
        if (args.NewState == SeatState.GracePeriod)
        {
            Console.WriteLine("⚠️ Varovanie: Spojenie so serverom sa prerušilo. Beží ochranná lehota!");
        }
        else if (args.NewState == SeatState.Lost)
        {
            Console.WriteLine("❌ Chyba: Platnosť sedadla vypršala. Aplikácia sa musí ukončiť alebo uložiť prácu.");
        }
    };

    // 5. Hlavná práca aplikácie...
    // Počas tohto času beží automatický asynchrónny heartbeat s náhodným jitterom ±10%.
    await Task.Delay(TimeSpan.FromSeconds(10));
}
// 6. Po opustení await using bloku sa sedadlo automaticky uvoľní späť do fondu!
Console.WriteLine("Sedadlo bolo korektne vrátené do fondu.");
```

---

## 3. Integrácia v Python (3.10+)

### Inštalácia
```bash
pip install symbolon
# alebo pri lokálnom vývoji z repozitára:
pip install -e sdk/python/symbolon
```

### Použitie cez kontextový manažér (`with`)

```python
import time
from symbolon import SymbolonClient, SeatAllocationDenied, SymbolonException

def main():
    # 1. Inicializácia klienta
    client = SymbolonClient(
        server_url="http://localhost:8080",
        product_code="cad-pro",
        relay_url="http://192.168.1.50:8081", # voliteľný lokálny on-premise relay fallback
        heartbeat_interval=30.0,
        jitter_factor=0.10                    # ±10% rozptyl pre zamedzenie thundering herd
    )

    license_key = "SYM-9ABC-DEF2-3456-7890"

    print("Žiadam server o plávajúce sedadlo...")
    try:
        # 2. Získanie sedadla s automatickým kontextovým manažérom
        with client.acquire_seat(license_key, features=["rendering", "cad-core"]) as lease:
            print(f"✓ Získané sedadlo #{lease.seat_number} (Lease ID: {lease.lease_id})")
            print("Automatický heartbeat beží na pozadí v samostatnom daemon vlákne.")

            # Simulácia práce aplikácie
            for sec in range(5):
                print(f"  Aplikácia beží... ({sec + 1}/5)")
                time.sleep(1)

        # 3. Po ukončení bloku with je sedadlo automaticky a bezpečne uvoľnené!
        print("Sedadlo bolo úspešne uvoľnené.")

    except SeatAllocationDenied as ex:
        print(f"❌ Kapacita servera vyčerpaná: {ex}")
    except SymbolonException as ex:
        print(f"❌ Chyba licenčného subsystému: {ex}")

if __name__ == "__main__":
    main()
```

---

## 4. Integrácia v Rust (2021 Edition)

### Konfigurácia `Cargo.toml`
```toml
[dependencies]
symbolon = { path = "../sdk/rust/symbolon" } # alebo z crates.io
```

### Použitie s idiomatickým RAII vzorom (`Drop`)

```rust
use symbolon_client::{get_local_fingerprint, SymbolonClient};
use std::thread;
use std::time::Duration;

fn main() {
    println!("=== Symbolon Rust Klientska Integrácia ===");

    // 1. Získanie hardvérového odtlačku stroja
    let fp = get_local_fingerprint();
    println!("Hardvérový fingerprint stanice: {fp}");

    // 2. Vytvorenie inštancie klienta
    let client = SymbolonClient::new("http://localhost:8080", "cad-pro");
    let license_key = "SYM-9ABC-DEF2-3456-7890";

    // 3. Alokácia sedadla (RAII vzor)
    match client.acquire_seat(license_key) {
        Ok(lease) => {
            println!("✓ Úspešne alokované sedadlo #{} (Lease ID: {})", 
                     lease.seat_number(), lease.lease_id());

            // 4. Overenie oprávnenia pre funkčný modul (napr. FEA Solver)
            if lease.has_feature("FEA_SOLVER") {
                println!("Modul FEA_SOLVER je priamo obsiahnutý v licencii.");
            }

            // 5. Dynamická alokácia prídavného modulu za behu s RAII ochranou
            {
                match lease.acquire_feature("FEA_SOLVER", Some("2026.1")) {
                    Ok(solver_feat) => {
                        println!("✓ Dynamicky pridelený modul {} (v{})", solver_feat.feature_code, solver_feat.version.as_deref().unwrap_or("*"));
                        // Simulácia výpočtu...
                        println!("Výpočtové jadro FEA beží...");
                        // Po opustení tohto bloku sa solver_feat automaticky uvoľní cez Drop!
                    }
                    Err(err) => eprintln!("Modul FEA_SOLVER nie je dostupný: {err}"),
                }
            }

            // Simulácia práce aplikácie
            println!("Aplikácia spracováva dáta...");
            thread::sleep(Duration::from_secs(3));

            // Keď premenná `lease` opustí scope, automaticky sa zavolá trait Drop,
            // ktorý odošle požiadavku na okamžité uvoľnenie hlavného sedadla.
        }
        Err(err) => {
            eprintln!("❌ Zlyhalo pridelenie sedadla: {err}");
        }
    }

    println!("Blok ukončený, sedadlo je voľné pre ďalších používateľov.");
}
```

---

## 5. Integrácia v C / C++ (C99 & C++17)

Pre systémové aplikácie, herné enginy, CAD nástroje alebo embedded prostredia je určená knižnica [`symbolon.h`](file:///c:/Licenčný%20server/sdk/c_cpp/include/symbolon.h).

### Príklad v ANSI C (C99)

```c
#include <stdio.h>
#include "symbolon.h"

int main(void)
{
    // 1. Inicializácia klienta
    symbolon_client_t* client = NULL;
    symbolon_status_t status = symbolon_client_create("http://localhost:8080", "cad-pro", &client);
    if (status != SYMBOLON_OK) {
        fprintf(stderr, "Chyba inicializácie klienta: %d\n", status);
        return 1;
    }

    // 2. Alokácia plávajúceho sedadla
    symbolon_lease_t* lease = NULL;
    status = symbolon_acquire_seat(client, "SYM-9ABC-DEF2-3456-7890", &lease);

    if (status == SYMBOLON_OK) {
        char lease_id[64];
        symbolon_lease_get_id(lease, lease_id, sizeof(lease_id));
        printf("✓ Sedadlo pridelené! Lease ID: %s, Sedadlo #%d\n",
               lease_id, symbolon_lease_get_seat_number(lease));

        // 3. Overenie a dynamická alokácia modulu (FEA Solver)
        int has_fea = 0;
        symbolon_lease_has_feature(lease, "FEA_SOLVER", &has_fea);
        printf("FEA Solver pred alokáciou: %s\n", has_fea ? "AKTÍVNY" : "NEAKTÍVNY");

        symbolon_feature_lease_t* feat_lease = NULL;
        if (symbolon_acquire_feature(lease, "FEA_SOLVER", "2026.1", &feat_lease) == SYMBOLON_OK) {
            printf("✓ Dynamicky alokovaný modul FEA_SOLVER!\n");
            // Beží výpočtové jadro...
            // Uvoľnenie modulu späť do fondu:
            symbolon_release_feature(feat_lease);
            printf("Modul FEA_SOLVER bol uvoľnený.\n");
        }

        // 4. Manuálny heartbeat (ak sa nepoužíva vstavané vlákno)
        symbolon_renew_seat(lease);

        // 5. Uvoľnenie sedadla pri ukončení
        symbolon_release_seat(lease);
        printf("Sedadlo uvoľnené späť do fondu.\n");
    } 
    else if (status == SYMBOLON_ERR_CAPACITY_EXHAUSTED) {
        fprintf(stderr, "Kapacita vyčerpaná: Všetky plávajúce licencie sú obsadené.\n");
    } 
    else {
        fprintf(stderr, "Chyba komunikácie so serverom: %d\n", status);
    }

    // 6. Uvoľnenie klienta
    symbolon_client_destroy(client);
    return 0;
}
```

### Príklad v C++17 (Moderné RAII cez `ScopedLease` & `ScopedFeatureLease`)

```cpp
#include <iostream>
#include "symbolon.h"

void run_application() {
    symbolon_client_t* raw_client = nullptr;
    if (symbolon_client_create("http://localhost:8080", "cad-pro", &raw_client) != SYMBOLON_OK) {
        throw std::runtime_error("Nepodarilo sa vytvoriť Symbolon klienta");
    }

    symbolon_lease_t* raw_lease = nullptr;
    symbolon_status_t status = symbolon_acquire_seat(raw_client, "SYM-9ABC-DEF2-3456-7890", &raw_lease);
    
    if (status != SYMBOLON_OK) {
        symbolon_client_destroy(raw_client);
        throw std::runtime_error("Licencia nie je dostupná");
    }

    // Moderný RAII wrapper: automaticky uvoľní sedadlo pri návrate z funkcie alebo pri výnimke
    symbolon::ScopedLease lease(raw_lease);
    std::cout << "Licencia aktívna, aplikácia beží..." << std::endl;

    // Dynamická alokácia modulu chránená vnútorným RAII ScopedFeatureLease
    symbolon_feature_lease_t* raw_feat = nullptr;
    if (symbolon_acquire_feature(lease.get(), "FEA_SOLVER", "2026.1", &raw_feat) == SYMBOLON_OK) {
        symbolon::ScopedFeatureLease feat(raw_feat);
        std::cout << "FEA Solver modul je aktívny v ScopedFeatureLease..." << std::endl;
        // Po ukončení tohto vnútorného bloku deštruktor ScopedFeatureLease automaticky vráti modul!
    }

    // ... ďalší výkonný kód aplikácie ...
    // Po opustení run_application() deštruktor ScopedLease vráti hlavné sedadlo!
    symbolon_client_destroy(raw_client);
}
```

---

## 6. Integrácia v Go (Golang 1.22+)

Klientske SDK pre Go (`sdk/go/symbolon`) je pure-Go implementácia bez nutnosti CGO.

### Príklad použitia

```go
package main

import (
	"context"
	"fmt"
	"log"
	"time"

	"github.com/symbolon/sdk/go/symbolon"
)

func main() {
	opts := symbolon.DefaultOptions("http://localhost:8080", "SYM-9ABC-DEF2-3456-7890")
	opts.Product = "cad-pro"
	opts.Features = []string{"core", "module.cad-export"}

	client, err := symbolon.NewClient(opts)
	if err != nil {
		log.Fatalf("Chyba inicializácie: %v", err)
	}

	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()

	// Získanie sedadla s automatickou goroutine heartbeat slučkou (jitter ±10%)
	lease, err := client.AcquireSeat(ctx)
	if err != nil {
		log.Fatalf("Nepodarilo sa získať sedadlo: %v", err)
	}
	defer lease.Release(context.Background())

	fmt.Printf("Sedadlo #%d úspešne pridelené. Token platí do: %s\n", lease.Seat, lease.ExpiresAt)

	// Aplikácia vykonáva prácu
	time.Sleep(10 * time.Second)
}
```

---

## 7. Integrácia v Java 17+ (Enterprise / Spring Boot)

Java SDK (`sdk/java/symbolon`) poskytuje bezpečné enterprise riešenie pre Spring Boot, Quarkus, Micronaut aj desktopové Java CAD/CAM nástroje s nulovými vonkajšími tranzivnými závislosťami.

### Inštalácia cez Maven (`pom.xml`)

```xml
<dependency>
    <groupId>dev.symbolon</groupId>
    <artifactId>symbolon-client</artifactId>
    <version>1.0.0</version>
</dependency>
```

### Príklad použitia s `AutoCloseable` (Try-With-Resources)

```java
package com.example.app;

import dev.symbolon.ClientOptions;
import dev.symbolon.SymbolonClient;
import dev.symbolon.models.CheckoutResponse;

import java.util.List;

public class Application {
    public static void main(String[] args) {
        ClientOptions options = ClientOptions.builder()
                .serverUrl("http://localhost:8080")
                .licenseKey("SYM-9ABC-DEF2-3456-7890")
                .product("cad-pro")
                .features(List.of("core", "module.cad-export"))
                .heartbeatIntervalSeconds(120)
                .build();

        try (SymbolonClient client = new SymbolonClient(options)) {
            CheckoutResponse lease = client.acquireSeat();
            System.out.printf("Sedadlo #%d úspešne pridelené (Lease ID: %s)%n", 
                lease.getSeat(), lease.getLeaseId());

            // A/B testovací variant deterministicky vyhodnotený pre túto inštanciu
            String variant = client.getAssignedVariant("exp_new_render_pipeline");
            System.out.println("Aktívny experimentálny variant: " + variant);

            // Výkonný kód aplikácie...
            Thread.sleep(5000);
            
            // Pri opustení try bloku client.close() automaticky uvoľní sedadlo (graceful release)!
        } catch (Exception e) {
            System.err.println("Licenčná chyba: " + e.getMessage());
        }
    }
}
```

---

## 8. Integrácia vo WebAssembly / TypeScript (Node.js & Web)

SDK pre WebAssembly a TypeScript (`sdk/wasm`) umožňuje offline verifikáciu licencií `symlic/1` a lízingových tokenov priamo v moderných webových prehliadačoch alebo v Node.js/Electron desktop aplikáciách.

### Príklad v TypeScript / JavaScript

```typescript
import { SymbolonValidator } from './symbolon-validator.mjs';

async function verifyLicense() {
    const validator = new SymbolonValidator({
        trustedIssuer: 'https://licenses.acme.example',
        expectedAudience: 'cad-web-app'
    });

    const licensePem = `-----BEGIN SYMBOLON LICENSE-----
eyJwYXlsb2FkIjoiZXlKcGMzTWlPaUpvZEhSd2N6b3Z...
-----END SYMBOLON LICENSE-----`;

    const result = await validator.verify(licensePem);
    if (result.isValid) {
        console.log(`Licencia overená! Max sedadiel: ${result.claims.symlic.limits.maxSeats}`);
        console.log(`Povolene moduly:`, result.claims.symlic.entitlements.map(e => e.code));
    } else {
        console.error(`Neplatná licencia: ${result.error}`);
    }
}
```

---

## 9. Zásady a Osvedčené Postupy (Best Practices)

1. **Adaptívny Jitter (±10%)**:
   - Všetky oficiálne Symbolon SDK automaticky aplikujú náhodný rozptyl času obnovovania. Nikdy nevypínajte jitter pri tisíckach súbežných inštalácií, inak riskujete zahltenie servera (efekt „thundering herd“).
2. **Grace Period**:
   - Ak sieťový požiadavok na obnovenie sedadla zlyhá z dôvodu dočasného výpadku Wi-Fi alebo reštartu smerovača, klient zostáva v stave `GracePeriod` po dobu definovanú v politike (štandardne 60–120 sekúnd). Neodpájajte používateľa okamžite pri prvom zlyhaní siete!
3. **Validácia Licenčného Kľúča na Klientovi**:
   - Pred odoslaním požiadavky na sieť Symbolon SDK lokálne verifikuje CRC-32C kontrolný súčet Crockford Base32 kľúča. Tým sa okamžite odhalia preklepy (napr. zámena `0` za `O` alebo `1` za `I`) bez zbytočného zaťažovania sieťového spojenia.
4. **On-Premise Relay Fallback**:
   - V podnikových sieťach konfigurujte `RelayUrl`. Ak je centrálny cloud nedostupný, SDK sa automaticky obráti na lokálny relay server v LAN sieti.
5. **Post-Quantum Cryptography Gating**:
   - Pri overovaní certifikátov a dlhodobých licencií využívajte hybridný profil `hybrid-v1` (ES256 + ML-DSA-65). Pri prechode na CNSA 2.0 / NIS 2 aktivujte profil `pqc-strict`.

---

## 10. Kreditové a Metered Pay-As-You-Go Licencovanie (Token Wallets)

Pre operácie účtované podľa reálnej spotreby (AI inferencia, rendering, cloudové výpočty, exporty) Symbolon poskytuje **Token & Metered Pay-As-You-Go** model s peňaženkami (`TokenWallet`) a bezpečnostným rozsahom **Auto-Rollback Scope**.

Ak aplikácia spadne, vyhodí výnimku alebo skončí predčasne, nezužitkované rezervované kredity sa vďaka RAII vzoru (`IAsyncDisposable`, `with`, `Close`, `try-with-resources`) automaticky vrátia späť zákazníkovi — **nulové riziko úniku kreditov**.

### C# / .NET 10
```csharp
// Začatie metered operácie s odhadom 100 kreditov
await using var scope = await client.BeginMeteredScopeAsync("wlt_enterprise", "ai_inference", 100m);

// Dlhšie bežiaca úloha môže odosielať priebežný heartbeat
await scope.HeartbeatAsync(deltaUnits: 25m);

// Po úspešnom dokončení potvrdíme reálnu spotrebu (napr. 75 jednotiek)
// Zvyšných 25 kreditov sa okamžite refunduje do peňaženky
await scope.CommitAsync(actualUnits: 75m);
// Ak by nastala výnimka pred CommitAsync, DisposeAsync automaticky zavolá Rollback
```

### Python (3.10+)
```python
# Kontextový manažér s garantovaným auto-rollbackom pri výnimke
with client.metered_scope("wlt_enterprise", "ai_inference", 100.0) as scope:
    scope.heartbeat(delta_units=25.0)
    
    # Reálna práca...
    scope.commit(actual_units=75.0)
```

### Go (Golang 1.22+)
```go
scope, err := client.BeginMeteredScope(ctx, symbolon.ReserveTokensRequest{
    WalletId:       "wlt_enterprise",
    FeatureCode:    "ai_inference",
    EstimatedUnits: 100.0,
})
if err != nil {
    log.Fatal(err)
}
defer scope.Close() // Automatický rollback pri neočakávanom návrate pred commitom

// Potvrdenie spotrebovaných jednotiek
commitResp, err := scope.Commit(ctx, 75.0, false)
```

### Java 17+ (Spring Boot / Quarkus)
```java
var req = new ReserveTokensRequest("wlt_enterprise", "ai_inference", 100.0);
try (TokenReservationScope scope = client.beginMeteredScope(req)) {
    scope.heartbeat(25.0, false);
    
    // Potvrdenie reálnej spotreby
    scope.commit(75.0, false);
} // V prípade chyby close() automaticky uvoľní a vráti rezervované kredity
```

---

## 11. Kontajnerový a Cloudový Fingerprint Guard (FPR-10 .. FPR-14, §7.6)

V kontajneroch (Docker, Podman, Kubernetes, AWS ECS, Azure Container Apps, Google Cloud Run) a cloudových inštanciách je zber fyzického hardvérového fingerprintu anti-pattern:
- Golden images a základné obrazy nesú identický `machine-id` do stoviek replík.
- VM klony a autoscaling duplikujú CPU model aj MAC adresy virtuálnych adaptérov.
- Každé nasadenie nového podu by viedlo k nežiaducemu node-lock zlyhaniu alebo rýchlemu vyčerpaniu limitu aktivácií.

V súlade s normatívnou špecifikáciou `spec/08-fingerprint.md` (FPR-10 až FPR-14) všetky oficiálne Symbolon SDK (.NET, Python, Go, Java, Rust, C/C++):
1. **Automaticky detegujú kontajnerové/cloudové prostredie** (kontrolou `/.dockerenv`, `/run/.containerenv`, `/proc/1/cgroup`, premenných `KUBERNETES_SERVICE_HOST`, `container`, `DOTNET_RUNNING_IN_CONTAINER`, `AWS_EXECUTION_ENV`, `AZURE_CONTAINER_APP_NAME`, `GOOGLE_CLOUD_PROJECT`).
2. **Nikdy nepoužívajú fyzický hardvér v kontajneri** (FPR-12).
3. **Generujú a perzistujú stabilný UUID v pripojenom volume** (`~/.symbolon/container_instance_uuid.txt` alebo v konfigurovateľnom zväzku).
4. **Logujú odporúčanie (FPR-13)** použiť pre kontajnerové a autoscaling pracovné záťaže **floating licencie s krátkym lease TTL (10 min)** namiesto viazania na uzol (node-locking).

### Príklad detekcie a získania komponentov naprieč jazykmi:
- **.NET:** `DeviceFingerprint.IsContainerOrCloud()`, `DeviceFingerprint.Collect(...)`
- **Python:** `symbolon.fingerprint.is_container_or_cloud()`, `symbolon.fingerprint.get_hardware_components(...)`
- **Go:** `symbolon.IsContainerOrCloud()`, `symbolon.GetLocalFingerprintComponents()`
- **Java:** `Fingerprint.isContainerOrCloud()`, `Fingerprint.getLocalFingerprintComponents()`
- **Rust:** `symbolon::fingerprint::is_container_or_cloud()`, `symbolon::fingerprint::get_hardware_components()`
- **C/C++:** `symbolon_is_container_or_cloud(&is_container)`, `symbolon_get_hardware_fingerprint(buf, len)`

---

## 12. Air-Gapped USB Digest & Audit Chain Synchronizácia (GNT-1..10, FLT-32..35, §7.7)

Pre izolované priemyselné prevádzky, kritickú infraštruktúru a SCADA siete bez priameho prístupu na internet Symbolon implementuje bezpečný kryptografický protokol prenosu licencií cez fyzické médiá (USB):

```
[Izolovaná sieť / Air-Gap]                               [Online Sieť]
Relay server / Operátor                                  Control Plane Server
  │                                                            │
  ├─ 1. symbolon grant request ──> req.symreq                  │
  │     (obsahuje usageDigest hash-chain od minulého grantu)   │
  │                                                            │
  │     ═════════════ Prenos cez USB kľúč ═════════════════>  │
  │                                                            │
  │                                   2. symbolon grant issue ─┤
  │                                      (--in req.symreq)     │
  │                                      alebo POST /v1/offline/requests
  │                                                            │
  │                                   vytvorí grant.symgrant  ─┤
  │                                                            │
  │     <════════════ Prenos cez USB kľúč ═════════════════    │
  │                                                            │
  ├─ 3. symbolon grant import ──> pool rozšírený o sedadlá     │
  │     (--in grant.symgrant)                                  │
```

### Kľúčové bezpečnostné garancie:
- **Usage Digest Continuity (FLT-33):** Každá žiadosť `.symreq` musí obsahovať koreň Merkleho stromu / hash-chainu auditných udalostí od posledného grantu. Control Plane odmietne vydať nový grant, ak predchádzajúci reťazec nenadväzuje — relay nemôže donekonečna čerpať kapacitu bez preukázania reálneho využitia.
- **Anti-Replay Nonce (FLT-34):** Každý `.symreq` má jedinečný nonce registrovaný v databáze Control Plane; opakované odoslanie toho istého súboru skončí s chybou `409 Conflict`.
- **Monotónna sekvencia (GNT-7):** Každý grant nesie prísne rastúce sekvenčné číslo (`seq`), ktoré pri importe do relayu automaticky zneplatňuje a nahrádza starší grant (`supersedes`).


