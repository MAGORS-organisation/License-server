package symbolon

import (
	"context"
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"testing"
	"time"
)

func TestParseToken_Valid(t *testing.T) {
	raw := "eyJhbGciOiJFUzI1NiJ9.eyJzdWIiOiJ1c2VyLTEiLCJleHAiOjE5OTk5OTk5OTksImZlYXR1cmVzIjpbImFkdmFuY2VkIl19.c2lnbmF0dXJl"
	token, err := ParseToken(raw)
	if err != nil {
		t.Fatalf("unexpected error parsing token: %v", err)
	}

	if token.Header != "eyJhbGciOiJFUzI1NiJ9" {
		t.Errorf("expected header eyJhbGciOiJFUzI1NiJ9, got %s", token.Header)
	}
	if !token.ContainsFeature("advanced") {
		t.Errorf("expected feature 'advanced' to be present")
	}
	if token.ContainsFeature("nonexistent") {
		t.Errorf("expected feature 'nonexistent' to be absent")
	}

	now := time.Unix(1700000000, 0)
	if token.IsExpired(now) {
		t.Errorf("expected token not to be expired at %v", now)
	}

	future := time.Unix(2100000000, 0)
	if !token.IsExpired(future) {
		t.Errorf("expected token to be expired at %v", future)
	}

	digest := token.ComputeDigest()
	if len(digest) != 64 {
		t.Errorf("expected 64-char sha256 hex digest, got %s", digest)
	}
}

func TestParseToken_Invalid(t *testing.T) {
	cases := []string{"", "single", "part1.part2", "part1..part3"}
	for _, tc := range cases {
		_, err := ParseToken(tc)
		if err == nil {
			t.Errorf("expected error for token '%s', got nil", tc)
		}
	}
}

func TestAgentClient_GetStatus(t *testing.T) {
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.URL.Path != "/v1/status" {
			http.NotFound(w, r)
			return
		}
		json.NewEncoder(w).Encode(AgentStatus{
			Status:         "active",
			MachineID:      "mach-test-99",
			OfflineAllowed: true,
		})
	}))
	defer server.Close()

	client := NewAgentClient(server.URL)
	status, err := client.GetStatus(context.Background())
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}

	if status.Status != "active" {
		t.Errorf("expected status 'active', got '%s'", status.Status)
	}
	if status.MachineID != "mach-test-99" {
		t.Errorf("expected machine ID 'mach-test-99', got '%s'", status.MachineID)
	}
}
