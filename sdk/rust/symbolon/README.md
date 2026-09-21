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

    // Získanie plávajúceho sedadla
    let lease = client.acquire_seat("SYM-9ABC-DEF2-3456-7890").expect("Failed to acquire seat");
    println!("Sedadlo pridelené: #{}", lease.seat_number());

    // Beží aplikácia...
    // Po opustení scope sa implementáciou `Drop` sedadlo automaticky uvoľní späť do fondu!
}
```
