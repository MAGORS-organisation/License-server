package symbolon

import (
	"crypto/sha256"
	"encoding/binary"
	"fmt"
	"math"
	"strings"
)

// ExperimentStatus represents the operational lifecycle state of an experiment.
type ExperimentStatus string

const (
	StatusDraft      ExperimentStatus = "Draft"
	StatusActive     ExperimentStatus = "Active"
	StatusPaused     ExperimentStatus = "Paused"
	StatusCompleted  ExperimentStatus = "Completed"
	StatusRolledBack ExperimentStatus = "RolledBack"
)

// ExperimentOverrides defines client-side configuration changes imposed by a variant.
type ExperimentOverrides struct {
	LeaseTtlSeconds  *int64            `json:"leaseTtlSeconds,omitempty"`
	PolicyRulesYaml  *string           `json:"policyRulesYaml,omitempty"`
	FeatureFlags     map[string]bool   `json:"featureFlags,omitempty"`
	CustomMetadata   map[string]string `json:"customMetadata,omitempty"`
}

// ExperimentVariant represents an individual branch in an A/B or multivariate experiment.
type ExperimentVariant struct {
	VariantId string              `json:"variantId"`
	Name      string              `json:"name"`
	Weight    int                 `json:"weight"`
	IsControl bool                `json:"isControl"`
	Overrides ExperimentOverrides `json:"overrides"`
}

// ExperimentTargeting defines criteria for scoping experiment participation.
type ExperimentTargeting struct {
	TargetTenants       []string `json:"targetTenants,omitempty"`
	LicenseKeyPrefixes  []string `json:"licenseKeyPrefixes,omitempty"`
	AllowedSdkVersions  []string `json:"allowedSdkVersions,omitempty"`
	OperatingSystems    []string `json:"operatingSystems,omitempty"`
}

// Matches evaluates whether a client context satisfies targeting rules.
func (t *ExperimentTargeting) Matches(tenantId, licenseKey string, context map[string]string) bool {
	if len(t.TargetTenants) > 0 {
		found := false
		for _, tenant := range t.TargetTenants {
			if tenant == tenantId {
				found = true
				break
			}
		}
		if !found {
			return false
		}
	}

	if len(t.LicenseKeyPrefixes) > 0 {
		keyUpper := strings.ToUpper(licenseKey)
		found := false
		for _, prefix := range t.LicenseKeyPrefixes {
			if strings.HasPrefix(keyUpper, strings.ToUpper(prefix)) {
				found = true
				break
			}
		}
		if !found {
			return false
		}
	}

	if context != nil {
		if len(t.AllowedSdkVersions) > 0 {
			sdk := context["sdk_version"]
			if sdk == "" {
				sdk = context["sdkVersion"]
			}
			found := false
			for _, v := range t.AllowedSdkVersions {
				if v == sdk {
					found = true
					break
				}
			}
			if !found {
				return false
			}
		}

		if len(t.OperatingSystems) > 0 {
			osName := context["os_platform"]
			if osName == "" {
				osName = context["osPlatform"]
			}
			if osName == "" {
				osName = context["os"]
			}
			found := false
			for _, o := range t.OperatingSystems {
				if strings.Contains(strings.ToLower(osName), strings.ToLower(o)) {
					found = true
					break
				}
			}
			if !found {
				return false
			}
		}
	}

	return true
}

// ExperimentCircuitBreaker configures automated rollback when error thresholds are exceeded.
type ExperimentCircuitBreaker struct {
	MaxErrorRate float64 `json:"maxErrorRate"`
	MinSamples   int     `json:"minSamples"`
	AutoRollback bool    `json:"autoRollback"`
}

// Experiment defines complete A/B experiment configuration.
type Experiment struct {
	Id                 string                   `json:"id"`
	Name               string                   `json:"name"`
	Description        string                   `json:"description,omitempty"`
	Status             ExperimentStatus         `json:"status"`
	TrafficAllocation  int                      `json:"trafficAllocation"`
	Salt               string                   `json:"salt"`
	PromotedVariantId  *string                  `json:"promotedVariantId,omitempty"`
	Targeting          ExperimentTargeting      `json:"targeting"`
	Variants           []ExperimentVariant      `json:"variants"`
	CircuitBreaker     ExperimentCircuitBreaker `json:"circuitBreaker"`
}

// ExperimentEvaluationResult describes the assignment of a client to an experiment variant.
type ExperimentEvaluationResult struct {
	ExperimentId    string              `json:"experimentId"`
	VariantId       string              `json:"variantId"`
	IsInExperiment  bool                `json:"isInExperiment"`
	IsControl       bool                `json:"isControl"`
	Overrides       ExperimentOverrides `json:"overrides"`
}

// CalculateBucket computes a deterministic, stateless bucket [0..99] using SHA-256 little-endian uint32 modulo 100.
// Guarantees zero-drift sticky session invariant across identical (licenseKey, machineId, salt).
func CalculateBucket(licenseKey, machineId, salt string) (int, error) {
	keyClean := strings.TrimSpace(strings.ToUpper(licenseKey))
	machClean := strings.TrimSpace(machineId)

	if keyClean == "" {
		return 0, newError(ErrInvalidArgument, "licenseKey cannot be empty")
	}
	if machClean == "" {
		return 0, newError(ErrInvalidArgument, "machineId cannot be empty")
	}

	input := fmt.Sprintf("%s:%s:%s", keyClean, machClean, salt)
	hash := sha256.Sum256([]byte(input))
	val := binary.LittleEndian.Uint32(hash[:4])
	return int(val % 100), nil
}

// RouteExperiment evaluates an experiment and assigns client to a variant or baseline.
func RouteExperiment(
	exp *Experiment,
	tenantId string,
	licenseKey string,
	machineId string,
	context map[string]string,
) (ExperimentEvaluationResult, error) {
	if exp == nil {
		return ExperimentEvaluationResult{}, newError(ErrInvalidArgument, "experiment cannot be nil")
	}

	baselineResult := ExperimentEvaluationResult{
		ExperimentId:   exp.Id,
		VariantId:      "baseline",
		IsInExperiment: false,
		IsControl:      true,
	}

	// Completed experiment with promoted variant
	if exp.Status == StatusCompleted && exp.PromotedVariantId != nil {
		for _, v := range exp.Variants {
			if v.VariantId == *exp.PromotedVariantId {
				return ExperimentEvaluationResult{
					ExperimentId:   exp.Id,
					VariantId:      v.VariantId,
					IsInExperiment: true,
					IsControl:      v.IsControl,
					Overrides:      v.Overrides,
				}, nil
			}
		}
	}

	if exp.Status != StatusActive {
		return baselineResult, nil
	}

	if !exp.Targeting.Matches(tenantId, licenseKey, context) {
		return baselineResult, nil
	}

	bucket, err := CalculateBucket(licenseKey, machineId, exp.Salt)
	if err != nil {
		return baselineResult, err
	}

	if bucket >= exp.TrafficAllocation || exp.TrafficAllocation <= 0 {
		return baselineResult, nil
	}

	if len(exp.Variants) == 0 {
		return baselineResult, nil
	}

	totalWeight := 0
	for _, v := range exp.Variants {
		if v.Weight > 0 {
			totalWeight += v.Weight
		}
	}
	if totalWeight <= 0 {
		return baselineResult, nil
	}

	scaledPoint := (bucket * totalWeight) / exp.TrafficAllocation

	accumulated := 0
	for _, v := range exp.Variants {
		if v.Weight > 0 {
			accumulated += v.Weight
			if scaledPoint < accumulated {
				return ExperimentEvaluationResult{
					ExperimentId:   exp.Id,
					VariantId:      v.VariantId,
					IsInExperiment: true,
					IsControl:      v.IsControl,
					Overrides:      v.Overrides,
				}, nil
			}
		}
	}

	fallback := exp.Variants[len(exp.Variants)-1]
	return ExperimentEvaluationResult{
		ExperimentId:   exp.Id,
		VariantId:      fallback.VariantId,
		IsInExperiment: true,
		IsControl:      fallback.IsControl,
		Overrides:      fallback.Overrides,
	}, nil
}

// Statistical Engine constants
const Z95 = 1.959963984540054

// Erf computes error function erf(x) using Abramowitz and Stegun 7.1.26 polynomial approximation (max error 1.5e-7).
func Erf(x float64) float64 {
	sign := 1.0
	if x < 0 {
		sign = -1.0
	}
	absX := math.Abs(x)

	a1 := 0.254829592
	a2 := -0.284496736
	a3 := 1.421413741
	a4 := -1.453152027
	a5 := 1.061405429
	p := 0.3275911

	t := 1.0 / (1.0 + p*absX)
	y := 1.0 - (((((a5*t+a4)*t)+a3)*t+a2)*t+a1)*t*math.Exp(-absX*absX)
	return sign * y
}

// NormalCDF returns the cumulative distribution function for standard normal distribution N(0,1).
func NormalCDF(z float64) float64 {
	return 0.5 * (1.0 + Erf(z/math.Sqrt2))
}

// ZTestResult holds the outcome of a Two-Proportion Z-Test.
type ZTestResult struct {
	ZScore  float64
	PValue  float64
	CILower float64
	CIUpper float64
}

// CalculateTwoProportionZTest performs a pooled two-proportion Z-test comparing control and treatment success rates.
func CalculateTwoProportionZTest(successesA, trialsA, successesB, trialsB uint64) ZTestResult {
	if trialsA == 0 || trialsB == 0 {
		return ZTestResult{ZScore: 0.0, PValue: 1.0, CILower: 0.0, CIUpper: 0.0}
	}

	pA := float64(successesA) / float64(trialsA)
	pB := float64(successesB) / float64(trialsB)
	diff := pB - pA

	pooledP := float64(successesA+successesB) / float64(trialsA+trialsB)
	pooledVar := pooledP * (1.0 - pooledP) * ((1.0 / float64(trialsA)) + (1.0 / float64(trialsB)))

	zScore := 0.0
	if pooledVar > 0 {
		zScore = diff / math.Sqrt(pooledVar)
	}

	pVal := math.Max(0.0, math.Min(1.0, 2.0*(1.0-NormalCDF(math.Abs(zScore)))))

	seDiff := math.Sqrt((pA*(1.0-pA)/float64(trialsA)) + (pB*(1.0-pB)/float64(trialsB)))
	ciLower := diff - (Z95 * seDiff)
	ciUpper := diff + (Z95 * seDiff)

	return ZTestResult{
		ZScore:  zScore,
		PValue:  pVal,
		CILower: ciLower,
		CIUpper: ciUpper,
	}
}

// CalculateWelchTTest performs Welch's t-test for difference in continuous metrics (e.g. latency).
func CalculateWelchTTest(meanA, stdDevA float64, nA uint64, meanB, stdDevB float64, nB uint64) (float64, float64) {
	if nA <= 1 || nB <= 1 {
		return 0.0, 1.0
	}

	varA := stdDevA * stdDevA
	varB := stdDevB * stdDevB
	denom := math.Sqrt((varA / float64(nA)) + (varB / float64(nB)))

	if denom <= 0 {
		return 0.0, 1.0
	}

	tScore := (meanB - meanA) / denom
	pVal := math.Max(0.0, math.Min(1.0, 2.0*(1.0-NormalCDF(math.Abs(tScore)))))
	return tScore, pVal
}
