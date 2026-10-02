package main

import (
	"context"
	"fmt"
	"log"
	"time"

	"github.com/MAGORS-organisation/License-server/sdk/go/symbolon"
)

func main() {
	opts := symbolon.DefaultClientOptions("http://localhost:8080", "cad-pro")
	client := symbolon.NewClient(opts)

	ctx := context.Background()
	licenseKey := "SYM-ABCD-EFGH-1234"

	fmt.Println("Attempting to acquire floating concurrent seat lease...")
	lease, err := client.AcquireSeat(ctx, licenseKey)
	if err != nil {
		log.Fatalf("Failed to acquire seat: %v", err)
	}
	defer lease.Release(ctx)

	fmt.Printf("Successfully acquired seat #%d (Lease ID: %s)\n", lease.SeatNumber(), lease.LeaseId())
	fmt.Printf("Hardware Fingerprint: %s\n", symbolon.GetLocalFingerprint())

	// Dynamically acquire an add-on module
	feat, err := lease.UseFeature(ctx, "FEA_SOLVER", "2026.1")
	if err != nil {
		fmt.Printf("Optional feature FEA_SOLVER not available: %v\n", err)
	} else {
		fmt.Println("Dynamically acquired feature: FEA_SOLVER")
		defer feat.Release(ctx)
	}

	// Application workload
	fmt.Println("Application running with valid license... (simulating 5s work)")
	time.Sleep(5 * time.Second)

	fmt.Println("Work completed. Releasing seat lease cleanly.")
}
