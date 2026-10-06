package symbolon

import (
	"crypto/rand"
	"crypto/sha256"
	"encoding/hex"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"runtime"
	"sort"
	"strings"
)

// IsContainerOrCloud detects if running inside Docker, Kubernetes pod, or cloud environment (FPR-10, FPR-11).
func IsContainerOrCloud() bool {
	// 1. Container marker files
	if _, err := os.Stat("/.dockerenv"); err == nil {
		return true
	}
	if _, err := os.Stat("/run/.containerenv"); err == nil {
		return true
	}

	// 2. Linux /proc/1/cgroup inspection
	if data, err := os.ReadFile("/proc/1/cgroup"); err == nil {
		content := strings.ToLower(string(data))
		for _, marker := range []string{"docker", "containerd", "kubepods", "lxc"} {
			if strings.Contains(content, marker) {
				return true
			}
		}
	}

	// 3. Container and Cloud environment variables (FPR-11)
	cloudEnvVars := []string{
		"KUBERNETES_SERVICE_HOST",
		"container",
		"DOTNET_RUNNING_IN_CONTAINER",
		"AWS_EXECUTION_ENV",
		"ECS_CONTAINER_METADATA_URI",
		"AZURE_CONTAINER_APP_NAME",
		"GOOGLE_CLOUD_PROJECT",
	}
	for _, env := range cloudEnvVars {
		if val := os.Getenv(env); val != "" {
			return true
		}
	}

	return false
}

// GetOrCreatePersistedContainerUUID retrieves or creates a persistent UUID in volume storage (FPR-12).
func GetOrCreatePersistedContainerUUID(customVolumePath string) string {
	var path string
	if customVolumePath != "" {
		path = customVolumePath
	} else {
		baseDir := os.Getenv("LOCALAPPDATA")
		if baseDir == "" {
			if home, err := os.UserHomeDir(); err == nil {
				baseDir = filepath.Join(home, ".symbolon")
			} else {
				baseDir = os.TempDir()
			}
		} else {
			baseDir = filepath.Join(baseDir, "symbolon")
		}
		_ = os.MkdirAll(baseDir, 0755)
		path = filepath.Join(baseDir, "container_instance_uuid.txt")
	}

	if data, err := os.ReadFile(path); err == nil {
		val := strings.TrimSpace(string(data))
		if len(val) >= 32 {
			return val
		}
	}

	// Generate UUID v4
	var b [16]byte
	_, _ = io.ReadFull(rand.Reader, b[:])
	b[6] = (b[6] & 0x0f) | 0x40 // version 4
	b[8] = (b[8] & 0x3f) | 0x80 // RFC 4122 variant
	newUUID := fmt.Sprintf("%08x-%04x-%04x-%04x-%012x", b[0:4], b[4:6], b[6:8], b[8:10], b[10:16])

	_ = os.WriteFile(path, []byte(newUUID), 0644)
	return newUUID
}

// GetLocalFingerprintComponents gathers canonical hardware or container components (FPR-1, FPR-10, FPR-12).
func GetLocalFingerprintComponents() map[string]string {
	return GetLocalFingerprintComponentsWithVolume("")
}

// GetLocalFingerprintComponentsWithVolume gathers components with custom volume path for container UUID.
func GetLocalFingerprintComponentsWithVolume(customVolumePath string) map[string]string {
	comps := make(map[string]string)

	if IsContainerOrCloud() {
		// FPR-12: If container or cloud detected, NEVER use hardware!
		// FPR-13: Log recommendation to use floating license with short TTL
		fmt.Fprintf(os.Stderr, "WARNING: Containerized or cloud environment detected. Hardware node-locking is an anti-pattern in containers. Recommended: floating license with short lease TTL (FPR-13).\n")
		comps["machineId"] = GetOrCreatePersistedContainerUUID(customVolumePath)
		comps["isContainer"] = "true"
		comps["os"] = runtime.GOOS
		comps["cpu"] = runtime.GOARCH
		return comps
	}

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
