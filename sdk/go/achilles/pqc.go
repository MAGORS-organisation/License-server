package symbolon

import (
	"fmt"
	"math"
	"strings"
)

// PqcProfile defines the cryptographic enforcement profile.
type PqcProfile string

const (
	PqcProfileHybridV1 PqcProfile = "hybrid-v1"
	PqcProfilePqcStrict PqcProfile = "pqc-strict"
)

// Standard PQC Algorithm identifiers.
const (
	AlgES256            = "ES256"
	AlgMLDSA44          = "ML-DSA-44"
	AlgMLDSA65          = "ML-DSA-65"
	AlgMLDSA87          = "ML-DSA-87"
	AlgMLKEM512         = "ML-KEM-512"
	AlgMLKEM768         = "ML-KEM-768"
	AlgMLKEM1024        = "ML-KEM-1024"
	AlgSLHDSASHA2128S   = "SLH-DSA-SHA2-128s"
	AlgSLHDSASHA2128F   = "SLH-DSA-SHA2-128f"
	AlgSLHDSASHAKE128S  = "SLH-DSA-SHAKE-128s"
)

var postQuantumAlgorithms = map[string]bool{
	strings.ToUpper(AlgMLDSA44):         true,
	strings.ToUpper(AlgMLDSA65):         true,
	strings.ToUpper(AlgMLDSA87):         true,
	strings.ToUpper(AlgMLKEM512):        true,
	strings.ToUpper(AlgMLKEM768):        true,
	strings.ToUpper(AlgMLKEM1024):       true,
	strings.ToUpper(AlgSLHDSASHA2128S):  true,
	strings.ToUpper(AlgSLHDSASHA2128F):  true,
	strings.ToUpper(AlgSLHDSASHAKE128S): true,
}

var cnsa2Algorithms = map[string]bool{
	strings.ToUpper(AlgMLDSA65):         true,
	strings.ToUpper(AlgMLDSA87):         true,
	strings.ToUpper(AlgMLKEM768):        true,
	strings.ToUpper(AlgMLKEM1024):       true,
	strings.ToUpper(AlgSLHDSASHA2128S):  true,
	strings.ToUpper(AlgSLHDSASHAKE128S): true,
}

// IsPostQuantum returns true if the specified algorithm is quantum-safe according to NIST PQC standards.
func IsPostQuantum(alg string) bool {
	return postQuantumAlgorithms[strings.ToUpper(strings.TrimSpace(alg))]
}

// IsCnsa2Compliant returns true if the algorithm meets US CNSA 2.0 requirements.
func IsCnsa2Compliant(alg string) bool {
	return cnsa2Algorithms[strings.ToUpper(strings.TrimSpace(alg))]
}

// IsAlgorithmPermitted returns true if the algorithm is allowed under the given PQC security profile.
func IsAlgorithmPermitted(alg string, profile PqcProfile) bool {
	if profile == PqcProfilePqcStrict {
		return IsPostQuantum(alg)
	}
	return strings.EqualFold(alg, AlgES256) || IsPostQuantum(alg)
}

// KeyAuditInfo represents cryptographic key metadata for readiness auditing.
type KeyAuditInfo struct {
	KeyId string `json:"keyId"`
	Alg   string `json:"alg"`
}

// LicenseAuditInfo represents license metadata for readiness auditing.
type LicenseAuditInfo struct {
	LicenseId string  `json:"licenseId"`
	Alg       *string `json:"alg,omitempty"`
}

// PqcReadinessAudit represents the comprehensive quantum vulnerability and readiness score.
type PqcReadinessAudit struct {
	ReadinessScore   float64    `json:"readinessScore"`
	ActiveProfile    PqcProfile `json:"activeProfile"`
	TotalKeys        int        `json:"totalKeys"`
	PqcKeys          int        `json:"pqcKeys"`
	ClassicalKeys    int        `json:"classicalKeys"`
	TotalLicenses    int        `json:"totalLicenses"`
	PqcLicenses      int        `json:"pqcLicenses"`
	AtRiskLicenses   int        `json:"atRiskLicenses"`
	IsCnsa2Ready     bool       `json:"isCnsa2Ready"`
	IsNis2Ready      bool       `json:"isNis2Ready"`
	ActionItems      []string   `json:"actionItems"`
	Summary          string     `json:"summary"`
}

// CalculatePqcReadiness computes the PQC Readiness Index (0..100%) and audit findings.
func CalculatePqcReadiness(keys []KeyAuditInfo, licenses []LicenseAuditInfo, activeProfile PqcProfile) PqcReadinessAudit {
	totalKeys := len(keys)
	pqcKeys := 0
	for _, k := range keys {
		if IsPostQuantum(k.Alg) {
			pqcKeys++
		}
	}
	classicalKeys := totalKeys - pqcKeys

	totalLicenses := len(licenses)
	pqcLicenses := 0
	atRiskLicenses := 0

	for _, lic := range licenses {
		alg := AlgES256
		if lic.Alg != nil && *lic.Alg != "" {
			alg = *lic.Alg
		}
		if IsPostQuantum(alg) {
			pqcLicenses++
		} else {
			atRiskLicenses++
		}
	}

	keyScore := 30.0
	if totalKeys > 0 {
		keyScore = (float64(pqcKeys) / float64(totalKeys)) * 60.0
	}

	licScore := 15.0
	if totalLicenses > 0 {
		licScore = (float64(pqcLicenses) / float64(totalLicenses)) * 30.0
	}

	profScore := 5.0
	if activeProfile == PqcProfilePqcStrict {
		profScore = 10.0
	}

	totalScore := math.Round(math.Max(0.0, math.Min(100.0, keyScore+licScore+profScore))*10) / 10

	allKeysCnsa2 := totalKeys > 0
	for _, k := range keys {
		if !IsCnsa2Compliant(k.Alg) {
			allKeysCnsa2 = false
			break
		}
	}

	isCnsa2 := activeProfile == PqcProfilePqcStrict && classicalKeys == 0 && allKeysCnsa2
	isNis2 := totalScore >= 70.0

	var actions []string
	if classicalKeys > 0 {
		actions = append(actions, fmt.Sprintf("Rotate %d classical keys to ML-DSA-65 or ML-KEM-768.", classicalKeys))
	}
	if atRiskLicenses > 0 {
		actions = append(actions, fmt.Sprintf("Re-sign %d perpetual or long-lived licenses with ML-DSA-65.", atRiskLicenses))
	}
	if activeProfile != PqcProfilePqcStrict {
		actions = append(actions, "Upgrade server profile to 'pqc-strict' (Zero Classical Cryptography).")
	}

	summary := "CRITICAL: Infrastructure relies on classical cryptography vulnerable to Q-Day."
	if totalScore >= 95.0 {
		summary = "EXCELLENT: Fully ready for Post-Quantum Era (FIPS 203 & 204)."
	} else if totalScore >= 70.0 {
		summary = "GOOD: Hybrid profile active. Complete migration to pure PQC."
	} else if totalScore >= 40.0 {
		summary = "PARTIAL: Detected vulnerable classical components."
	}

	return PqcReadinessAudit{
		ReadinessScore:   totalScore,
		ActiveProfile:    activeProfile,
		TotalKeys:        totalKeys,
		PqcKeys:          pqcKeys,
		ClassicalKeys:    classicalKeys,
		TotalLicenses:    totalLicenses,
		PqcLicenses:      pqcLicenses,
		AtRiskLicenses:   atRiskLicenses,
		IsCnsa2Ready:     isCnsa2,
		IsNis2Ready:      isNis2,
		ActionItems:      actions,
		Summary:          summary,
	}
}
