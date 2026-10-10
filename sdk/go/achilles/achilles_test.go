package symbolon

import (
	"context"
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"
)

func TestFingerprintingDeterministic(t *testing.T) {
	comps := map[string]string{
		"host":      "alpha-station",
		"machineId": "uuid-1234-5678",
		"os":        "linux",
		"cpu":       "amd64",
	}

	fp1 := ComputeCanonicalFingerprint(comps)
	fp2 := ComputeCanonicalFingerprint(comps)

	if fp1 != fp2 {
		t.Fatalf("expected identical fingerprints, got %s and %s", fp1, fp2)
	}

	if !strings.HasPrefix(fp1, "sha256:") {
		t.Fatalf("expected sha256: prefix, got %s", fp1)
	}
}

func TestCalculateBucket(t *testing.T) {
	b1, err := CalculateBucket("SYM-ABCD-1234", "mach-01", "salt_test")
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	b2, err := CalculateBucket("SYM-ABCD-1234", "mach-01", "salt_test")
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}

	if b1 != b2 {
		t.Fatalf("expected deterministic bucket values, got %d and %d", b1, b2)
	}

	if b1 < 0 || b1 >= 100 {
		t.Fatalf("bucket out of range [0..99]: %d", b1)
	}

	// Case-insensitivity for license key
	b3, _ := CalculateBucket("sym-abcd-1234", "mach-01", "salt_test")
	if b1 != b3 {
		t.Fatalf("expected case-insensitivity: got %d vs %d", b1, b3)
	}
}

func TestRouteExperiment(t *testing.T) {
	exp := &Experiment{
		Id:                "exp_cloud_v1",
		Name:              "Cloud Latency Exp",
		Status:            StatusActive,
		TrafficAllocation: 100,
		Salt:              "pepper_salt_99",
		Variants: []ExperimentVariant{
			{VariantId: "control", Name: "Control", Weight: 50, IsControl: true},
			{VariantId: "treatment", Name: "Treatment", Weight: 50, IsControl: false},
		},
	}

	res1, err := RouteExperiment(exp, "ten_01", "SYM-KEY-100", "mach-node", nil)
	if err != nil {
		t.Fatalf("route failed: %v", err)
	}
	if !res1.IsInExperiment {
		t.Fatalf("expected client to be in experiment")
	}

	// Sticky session invariant test: 100 consecutive re-evaluations must yield identical variant
	for i := 0; i < 100; i++ {
		res2, _ := RouteExperiment(exp, "ten_01", "SYM-KEY-100", "mach-node", nil)
		if res1.VariantId != res2.VariantId {
			t.Fatalf("zero-drift invariant violated at iteration %d: %s vs %s", i, res1.VariantId, res2.VariantId)
		}
	}
}

func TestZTestAndWelch(t *testing.T) {
	zRes := CalculateTwoProportionZTest(90, 100, 96, 100)
	if zRes.PValue > 1.0 || zRes.PValue < 0.0 {
		t.Fatalf("invalid p-value: %f", zRes.PValue)
	}
	if zRes.CILower >= zRes.CIUpper {
		t.Fatalf("invalid confidence interval [%f, %f]", zRes.CILower, zRes.CIUpper)
	}

	tScore, pVal := CalculateWelchTTest(50.0, 5.0, 100, 42.0, 4.0, 100)
	if tScore >= 0 {
		t.Fatalf("expected negative t-score for latency reduction, got %f", tScore)
	}
	if pVal > 0.05 {
		t.Fatalf("expected statistically significant latency improvement, got p=%f", pVal)
	}
}

func TestPqcAlgorithmsAndReadiness(t *testing.T) {
	if !IsPostQuantum(AlgMLDSA65) {
		t.Fatalf("expected ML-DSA-65 to be post-quantum")
	}
	if !IsPostQuantum(AlgMLKEM768) {
		t.Fatalf("expected ML-KEM-768 to be post-quantum")
	}
	if IsPostQuantum(AlgES256) {
		t.Fatalf("ES256 is classical, not PQC")
	}

	if !IsCnsa2Compliant(AlgMLDSA87) {
		t.Fatalf("ML-DSA-87 must be CNSA 2.0 compliant")
	}
	if IsCnsa2Compliant(AlgMLDSA44) {
		t.Fatalf("ML-DSA-44 is not CNSA 2.0 compliant")
	}

	// Profile enforcement
	if !IsAlgorithmPermitted(AlgES256, PqcProfileHybridV1) {
		t.Fatalf("ES256 must be permitted in HybridV1")
	}
	if IsAlgorithmPermitted(AlgES256, PqcProfilePqcStrict) {
		t.Fatalf("ES256 must be rejected in PqcStrict")
	}

	keys := []KeyAuditInfo{
		{KeyId: "k1", Alg: AlgMLDSA65},
		{KeyId: "k2", Alg: AlgMLKEM768},
	}
	alg := AlgMLDSA65
	licenses := []LicenseAuditInfo{
		{LicenseId: "lic1", Alg: &alg},
	}

	audit := CalculatePqcReadiness(keys, licenses, PqcProfilePqcStrict)
	if audit.ReadinessScore < 99.0 {
		t.Fatalf("expected 100%% readiness score, got %f", audit.ReadinessScore)
	}
	if !audit.IsCnsa2Ready {
		t.Fatalf("expected CNSA 2.0 readiness to be true")
	}
}

func TestClientMockCheckoutAndRelease(t *testing.T) {
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.URL.Path == "/v1/leases" && r.Method == http.MethodPost {
			w.WriteHeader(http.StatusOK)
			json.NewEncoder(w).Encode(CheckoutResponse{
				LeaseId:      "les_mock_123",
				Token:        "symlease.mock.token",
				Seat:         1,
				ExpiresAt:    time.Now().Add(10 * time.Minute),
				Entitlements: []string{"core", "addon_fea"},
			})
			return
		}
		if strings.HasSuffix(r.URL.Path, "/renew") && r.Method == http.MethodPost {
			w.WriteHeader(http.StatusOK)
			json.NewEncoder(w).Encode(RenewResponse{
				LeaseSeq:  1,
				ExpiresAt: time.Now().Add(10 * time.Minute),
			})
			return
		}
		if strings.HasPrefix(r.URL.Path, "/v1/leases/") && r.Method == http.MethodDelete {
			w.WriteHeader(http.StatusOK)
			return
		}
		w.WriteHeader(http.StatusNotFound)
	}))
	defer server.Close()

	opts := DefaultClientOptions(server.URL, "cad-pro")
	opts.HeartbeatInterval = 50 * time.Millisecond
	client := NewClient(opts)

	ctx := context.Background()
	lease, err := client.AcquireSeat(ctx, "SYM-MOCK-KEY-001")
	if err != nil {
		t.Fatalf("acquire seat failed: %v", err)
	}

	if lease.LeaseId() != "les_mock_123" {
		t.Fatalf("unexpected leaseId: %s", lease.LeaseId())
	}
	if !lease.IsValid() {
		t.Fatalf("expected lease to be valid")
	}
	if !lease.HasFeature("addon_fea") {
		t.Fatalf("expected feature addon_fea to be entitled")
	}

	// Release lease
	if err := lease.Release(ctx); err != nil {
		t.Fatalf("release failed: %v", err)
	}

	if lease.IsValid() {
		t.Fatalf("released lease should no longer be valid")
	}
}

func TestClientMockTokensReserveAndCommit(t *testing.T) {
	commitCalled := false
	rollbackCalled := false

	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.URL.Path == "/v1/tokens/reserve" && r.Method == http.MethodPost {
			w.WriteHeader(http.StatusOK)
			json.NewEncoder(w).Encode(ReserveTokensResponse{
				Success:          true,
				ReservationId:    "res_go_123",
				ReservedAmount:   100.0,
				AvailableBalance: 900.0,
			})
			return
		}
		if r.URL.Path == "/v1/tokens/commit" && r.Method == http.MethodPost {
			commitCalled = true
			w.WriteHeader(http.StatusOK)
			json.NewEncoder(w).Encode(CommitTokensResponse{
				Success:         true,
				ConsumedCredits: 75.0,
				RefundedCredits: 25.0,
				NewBalance:      925.0,
			})
			return
		}
		if r.URL.Path == "/v1/tokens/rollback" && r.Method == http.MethodPost {
			rollbackCalled = true
			w.WriteHeader(http.StatusOK)
			json.NewEncoder(w).Encode(RollbackTokensResponse{
				Success:         true,
				RestoredCredits: 100.0,
				NewBalance:      1000.0,
			})
			return
		}
		w.WriteHeader(http.StatusNotFound)
	}))
	defer server.Close()

	opts := DefaultClientOptions(server.URL, "cad-pro")
	client := NewClient(opts)

	ctx := context.Background()
	scope, err := client.BeginMeteredScope(ctx, ReserveTokensRequest{
		WalletId:       "wlt_1",
		FeatureCode:    "render",
		EstimatedUnits: 100.0,
	})
	if err != nil {
		t.Fatalf("failed to begin metered scope: %v", err)
	}

	commitResp, err := scope.Commit(ctx, 75.0, false)
	if err != nil {
		t.Fatalf("commit failed: %v", err)
	}
	if !commitResp.Success || scope.AvailableBalance != 925.0 {
		t.Fatalf("unexpected commit state: %+v", commitResp)
	}

	// Close after commit must NOT trigger rollback
	if err := scope.Close(); err != nil {
		t.Fatalf("scope close failed: %v", err)
	}

	if !commitCalled {
		t.Fatalf("expected commit to be called")
	}
	if rollbackCalled {
		t.Fatalf("did not expect rollback when committed explicitly")
	}
}

func TestClientMockTokensAutoRollbackOnClose(t *testing.T) {
	rollbackCalled := false

	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.URL.Path == "/v1/tokens/reserve" && r.Method == http.MethodPost {
			w.WriteHeader(http.StatusOK)
			json.NewEncoder(w).Encode(ReserveTokensResponse{
				Success:          true,
				ReservationId:    "res_auto_rb",
				ReservedAmount:   50.0,
				AvailableBalance: 950.0,
			})
			return
		}
		if r.URL.Path == "/v1/tokens/rollback" && r.Method == http.MethodPost {
			rollbackCalled = true
			w.WriteHeader(http.StatusOK)
			json.NewEncoder(w).Encode(RollbackTokensResponse{
				Success:         true,
				RestoredCredits: 50.0,
				NewBalance:      1000.0,
			})
			return
		}
		w.WriteHeader(http.StatusNotFound)
	}))
	defer server.Close()

	opts := DefaultClientOptions(server.URL, "cad-pro")
	client := NewClient(opts)

	ctx := context.Background()
	scope, err := client.BeginMeteredScope(ctx, ReserveTokensRequest{
		WalletId:       "wlt_1",
		FeatureCode:    "ai",
		EstimatedUnits: 50.0,
	})
	if err != nil {
		t.Fatalf("failed to begin scope: %v", err)
	}

	// Closing without commit should trigger automatic rollback
	if err := scope.Close(); err != nil {
		t.Fatalf("scope close failed: %v", err)
	}

	if !rollbackCalled {
		t.Fatalf("expected auto-rollback on close without commit")
	}
}

func TestContainerAndCloudFingerprintGuard(t *testing.T) {
	tmpDir := t.TempDir()
	customPath := tmpDir + "/test_uuid.txt"

	// 1. Persisted UUID generation and consistency
	uuid1 := GetOrCreatePersistedContainerUUID(customPath)
	if len(uuid1) < 32 {
		t.Fatalf("expected valid UUID, got: %s", uuid1)
	}

	uuid2 := GetOrCreatePersistedContainerUUID(customPath)
	if uuid1 != uuid2 {
		t.Fatalf("expected persistent UUID across calls, got %s and %s", uuid1, uuid2)
	}

	// 2. Simulated container environment variable
	t.Setenv("KUBERNETES_SERVICE_HOST", "10.96.0.1")
	if !IsContainerOrCloud() {
		t.Fatalf("expected IsContainerOrCloud() to be true when KUBERNETES_SERVICE_HOST is set")
	}

	comps := GetLocalFingerprintComponentsWithVolume(customPath)
	if comps["isContainer"] != "true" {
		t.Fatalf("expected isContainer='true' in container environment")
	}
	if comps["machineId"] != uuid1 {
		t.Fatalf("expected machineId to equal persistent volume UUID %s, got %s", uuid1, comps["machineId"])
	}
}


