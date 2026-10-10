# Official Symbolon Go Client SDK

The official Go client SDK for the [Symbolon Floating & Enterprise License Server](https://github.com/MAGORS-organisation/License-server). Licensed under **Apache-2.0**.

---

## Installation

```bash
go get github.com/MAGORS-organisation/License-server/sdk/go/symbolon
```

## Features

- **Concurrent Floating Seat Checkouts**: Atomic checkout with hardware fingerprinting (FPR-1..4).
- **Automated Background Heartbeat**: Background goroutine with adaptive ±10% jitter and offline grace period recovery.
- **Dynamic Entitlement & Module Leasing**: On-the-fly acquisition and release of add-on features.
- **Client-Side A/B Testing**: Deterministic stateless router (SHA-256 slice [0..99]) with zero-drift sticky session guarantee.
- **Post-Quantum Cryptography Audit**: FIPS 203/204/205 inspection and CNSA 2.0 / EU NIS 2 compliance evaluation.

## Quickstart

```go
package main

import (
    "context"
    "fmt"
    "log"

    "github.com/MAGORS-organisation/License-server/sdk/go/symbolon"
)

func main() {
    opts := symbolon.DefaultClientOptions("http://localhost:8080", "my-app")
    client := symbolon.NewClient(opts)

    ctx := context.Background()
    lease, err := client.AcquireSeat(ctx, "SYM-XXXX-XXXX-XXXX")
    if err != nil {
        log.Fatalf("Checkout failed: %v", err)
    }
    defer lease.Release(ctx)

    fmt.Printf("Holding seat #%d (Lease: %s)\n", lease.SeatNumber(), lease.LeaseId())
}
```
