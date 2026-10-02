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
