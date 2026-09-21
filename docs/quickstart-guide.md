# Symbolon Integration Quickstart Guide

Tento ucelený sprievodca vás krok za krokom prevedie integráciou klientskeho licenčného mechanizmu do vašej aplikácie. Symbolon poskytuje oficiálne odľahčené SDK pre 4 hlavné programovacie jazyky:

- **C# / .NET 10** (`Symbolon.Client`) — plná asynchrónna podpora, `IAsyncDisposable`, automatický heartbeat.
- **Python 3.10+** (`symbolon`) — kontextový manažér `with`, bezobslužný daemon thread pre heartbeat s ±10% jitterom.
- **Rust (2021 Edition)** (`symbolon`) — idiomatický RAII vzor s automatickým uvoľnením cez `Drop` trait.
- **C99 a C++17** (`symbolon.h`) — čisté ANSI C API bez externých závislostí a moderný C++ RAII wrapper (`ScopedLease`).

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

            // Simulácia práce aplikácie
            println!("Aplikácia spracováva dáta...");
            thread::sleep(Duration::from_secs(3));

            // Keď premenná `lease` opustí scope, automaticky sa zavolá trait Drop,
            // ktorý odošle požiadavku na okamžité uvoľnenie sedadla.
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

Pre systémové aplikácie, herné enginy, CAD nástroje alebo embedded prostredia je určená single-header knižnica [`symbolon.h`](file:///c:/Licenčný%20server/sdk/c_cpp/include/symbolon.h).

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

        // 3. Manuálny heartbeat (ak sa nepoužíva vstavané vlákno)
        symbolon_renew_seat(lease);

        // 4. Uvoľnenie sedadla pri ukončení
        symbolon_release_seat(lease);
        printf("Sedadlo uvoľnené späť do fondu.\n");
    } 
    else if (status == SYMBOLON_ERR_CAPACITY_EXHAUSTED) {
        fprintf(stderr, "Kapacita vyčerpaná: Všetky plávajúce licencie sú obsadené.\n");
    } 
    else {
        fprintf(stderr, "Chyba komunikácie so serverom: %d\n", status);
    }

    // 5. Uvoľnenie klienta
    symbolon_client_destroy(client);
    return 0;
}
```

### Príklad v C++17 (Moderné RAII cez `ScopedLease`)

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
    // ... výkonný kód aplikácie ...

    symbolon_client_destroy(raw_client);
}
```

---

## 6. Zásady a Osvedčené Postupy (Best Practices)

1. **Adaptívny Jitter (±10%)**:
   - Všetky oficiálne Symbolon SDK automaticky aplikujú náhodný rozptyl času obnovovania. Nikdy nevypínajte jitter pri tisíckach súbežných inštalácií, inak riskujete zahltenie servera (efekt „thundering herd“).
2. **Grace Period**:
   - Ak sieťový požiadavok na obnovenie sedadla zlyhá z dôvodu dočasného výpadku Wi-Fi alebo reštartu smerovača, klient zostáva v stave `GracePeriod` po dobu definovanú v politike (štandardne 60–120 sekúnd). Neodpájajte používateľa okamžite pri prvom zlyhaní siete!
3. **Validácia Licenčného Kľúča na Klientovi**:
   - Pred odoslaním požiadavky na sieť Symbolon SDK lokálne verifikuje CRC-32C kontrolný súčet Crockford Base32 kľúča. Tým sa okamžite odhalia preklepy (napr. zámena `0` za `O` alebo `1` za `I`) bez zbytočného zaťažovania sieťového spojenia.
4. **On-Premise Relay Fallback**:
   - V podnikových sieťach konfigurujte `RelayUrl`. Ak je centrálny cloud nedostupný, SDK sa automaticky obráti na lokálny relay server v LAN sieti.
