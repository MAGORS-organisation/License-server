use symbolon_client::{get_local_fingerprint, SymbolonClient};

fn main() {
    println!("=== Symbolon Rust SDK Example ===");

    // 1. Get hardware fingerprint
    let fp = get_local_fingerprint();
    println!("Local Machine Fingerprint: {fp}");

    // 2. Create client
    let client = SymbolonClient::new("http://localhost:8080", "cad-pro");

    // 3. Acquire seat (RAII pattern)
    match client.acquire_seat("SYM-9ABC-DEF2-3456-7890") {
        Ok(lease) => {
            println!("Acquired Seat #{} (Lease ID: {})", lease.seat_number(), lease.lease_id());
            // When `lease` goes out of scope, Drop releases the seat automatically!
        }
        Err(e) => {
            eprintln!("Failed to acquire seat: {e}");
        }
    }

    println!("Exited lease scope. Seat released.");
}
