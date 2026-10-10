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

            // 4. Check feature entitlements
            println!("Has core module: {}", lease.has_feature("core"));
            println!("Has FEA solver: {}", lease.has_feature("FEA_SOLVER"));

            // 5. Dynamically acquire feature module with RAII scope
            {
                println!("Requesting dynamic feature FEA_SOLVER...");
                match lease.acquire_feature("FEA_SOLVER", Some("2026.1")) {
                    Ok(feat) => {
                        println!("✓ Acquired feature {} (version: {:?})", feat.feature_code, feat.version);
                        println!("Now has FEA solver: {}", lease.has_feature("FEA_SOLVER"));

                        // Perform intensive simulation with acquired feature...
                        println!("Simulation kernel running...");

                        // When `feat` goes out of scope, Drop releases the feature seat automatically!
                    }
                    Err(err) => {
                        eprintln!("Feature acquisition denied: {err}");
                    }
                }
            }

            println!("Feature scope ended. Has FEA solver: {}", lease.has_feature("FEA_SOLVER"));

            // 6. Alternative closure-based RAII helper
            let calculation_result = lease.use_feature("CAM_POST", None, |_feat| {
                println!("Inside use_feature closure for CAM_POST...");
                42
            });
            println!("Closure result: {:?}", calculation_result);

            // When `lease` goes out of scope, Drop releases the main seat automatically!
        }
        Err(e) => {
            eprintln!("Failed to acquire seat: {e}");
        }
    }

    println!("Exited lease scope. Seat released.");
}
