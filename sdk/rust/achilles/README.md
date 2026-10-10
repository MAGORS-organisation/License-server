# Symbolon Rust SDK

Official Rust client library for the **Symbolon Floating & Enterprise License Server**.

## Pridanie závislosti

V `Cargo.toml`:
```toml
[dependencies]
symbolon-client = { path = "path/to/sdk/rust/symbolon" }
```

## Použitie (RAII vzor)

```rust
use symbolon_client::SymbolonClient;

fn main() {
    let client = SymbolonClient::new("http://license.acme.corp:8080", "cad-pro");

    // 1. Získanie plávajúceho sedadla
    let lease = client.acquire_seat("SYM-9ABC-DEF2-3456-7890").expect("Failed to acquire seat");
    println!("Sedadlo pridelené: #{}", lease.seat_number());

    // 2. Overenie oprávnenia pre modul
    if lease.has_feature("core_modeling") {
        println!("Core modeler povolený.");
    }

    // 3. Dynamická alokácia náročného modulu (napr. FEA Solver) cez RAII blok
    {
        let solver_feat = lease.acquire_feature("FEA_SOLVER", Some("2026.1"))
            .expect("FEA Solver kapacita vyčerpaná");
        
        println!("FEA modul aktívny: {}", solver_feat.feature_code);
        // Beží výpočet metódou konečných prvkov...
        // Po opustení tohto vnútorného bloku `Drop` automaticky uvoľní licenciu modulu!
    }

    // Po opustení hlavného scope sa implementáciou `Drop` hlavné sedadlo automaticky uvoľní späť do fondu!
}
```
