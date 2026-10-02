package symbolon

import (
	"crypto/sha256"
	"encoding/hex"
	"fmt"
	"os"
	"runtime"
	"sort"
	"strings"
)

// GetLocalFingerprintComponents gathers canonical hardware components from the current machine
// according to FPR-1 to FPR-4 specifications.
func GetLocalFingerprintComponents() map[string]string {
	comps := make(map[string]string)

	// 1. Hostname
	if host, err := os.Hostname(); err == nil && host != "" {
		comps["host"] = strings.TrimSpace(host)
	} else {
		comps["host"] = "localhost"
	}

	// 2. OS platform
	comps["os"] = runtime.GOOS

	// 3. Architecture
	comps["cpu"] = runtime.GOARCH

	// 4. Machine identifier (from environment, host, or fallback UUID)
	machineId := os.Getenv("SYMBOLON_MACHINE_ID")
	if machineId == "" {
		machineId = os.Getenv("COMPUTERNAME")
	}
	if machineId == "" {
		machineId = comps["host"]
	}
	comps["machineId"] = machineId

	return comps
}

// ComputeCanonicalFingerprint produces a deterministic SHA-256 canonical hash string
// formatted as "sha256:<hex>" from the sorted component key-value pairs (FPR-2).
func ComputeCanonicalFingerprint(components map[string]string) string {
	if len(components) == 0 {
		return "sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
	}

	keys := make([]string, 0, len(components))
	for k := range components {
		keys = append(keys, k)
	}
	sort.Strings(keys)

	var sb strings.Builder
	for i, k := range keys {
		if i > 0 {
			sb.WriteString(";")
		}
		sb.WriteString(fmt.Sprintf("%s=%s", strings.ToLower(k), strings.TrimSpace(components[k])))
	}

	hash := sha256.Sum256([]byte(sb.String()))
	return fmt.Sprintf("sha256:%s", hex.EncodeToString(hash[:]))
}

// GetLocalFingerprint computes the canonical SHA-256 fingerprint for the current system.
func GetLocalFingerprint() string {
	return ComputeCanonicalFingerprint(GetLocalFingerprintComponents())
}
