// Package symbolon provides the official Go client SDK for the Symbolon Floating & Enterprise License Server.
package symbolon

import (
	"fmt"
	"time"
)

// CheckoutRequest represents the parameters for acquiring a floating concurrent seat.
type CheckoutRequest struct {
	LicenseKey            string            `json:"licenseKey"`
	FingerprintComponents map[string]string `json:"fingerprintComponents"`
	MachineId             string            `json:"machineId,omitempty"`
	UserId                string            `json:"userId,omitempty"`
	Features              []string          `json:"features,omitempty"`
	Quantity              int               `json:"quantity,omitempty"`
}

// CheckoutResponse represents the response received upon successful seat acquisition.
type CheckoutResponse struct {
	LeaseId      string    `json:"leaseId"`
	Token        string    `json:"token"`
	Seat         int       `json:"seat"`
	ExpiresAt    time.Time `json:"expiresAt"`
	Entitlements []string  `json:"entitlements,omitempty"`
	Overage      bool      `json:"overage,omitempty"`
}

// RenewRequest represents the heartbeat payload sent to maintain seat reservation.
type RenewRequest struct {
	ClientSeq             int64             `json:"clientSeq"`
	FingerprintComponents map[string]string `json:"fingerprintComponents"`
}

// RenewResponse represents the server response after a successful lease renewal.
type RenewResponse struct {
	LeaseSeq  int64     `json:"leaseSeq"`
	ExpiresAt time.Time `json:"expiresAt"`
}

// FeatureAcquireRequest represents a request to dynamically acquire an add-on module.
type FeatureAcquireRequest struct {
	FeatureCode string `json:"featureCode"`
	Version     string `json:"version,omitempty"`
}

// FeatureAcquireResponse represents the result of acquiring an add-on module.
type FeatureAcquireResponse struct {
	Success     bool      `json:"success"`
	FeatureCode string    `json:"featureCode"`
	InUse       int       `json:"inUse"`
	MaxSeats    int       `json:"maxSeats"`
	ExpiresAt   time.Time `json:"expiresAt,omitempty"`
}

// FeatureReleaseResponse represents the result of releasing an add-on module.
type FeatureReleaseResponse struct {
	Success     bool   `json:"success"`
	FeatureCode string `json:"featureCode"`
}

// ActiveFeatureInfo represents information about currently acquired add-on features.
type ActiveFeatureInfo struct {
	FeatureCode string    `json:"featureCode"`
	Version     string    `json:"version,omitempty"`
	ExpiresAt   time.Time `json:"expiresAt,omitempty"`
}

// ClientOptions holds configuration parameters for SymbolonClient.
type ClientOptions struct {
	ServerURL         string
	ProductCode       string
	ClientVersion     string
	Timeout           time.Duration
	HeartbeatInterval time.Duration
	GracePeriod       time.Duration
}

// DefaultClientOptions returns a ClientOptions with sensible enterprise defaults.
func DefaultClientOptions(serverURL, productCode string) ClientOptions {
	return ClientOptions{
		ServerURL:         serverURL,
		ProductCode:       productCode,
		ClientVersion:     "1.0.0",
		Timeout:           10 * time.Second,
		HeartbeatInterval: 2 * time.Minute,
		GracePeriod:       4 * time.Hour,
	}
}

// ErrorCode defines standardized error codes returned by Symbolon SDK.
type ErrorCode string

const (
	ErrInvalidArgument          ErrorCode = "invalid_argument"
	ErrNetwork                  ErrorCode = "network_error"
	ErrCapacityExhausted        ErrorCode = "capacity_exhausted"
	ErrLicenseNotFound          ErrorCode = "license_not_found"
	ErrExpired                  ErrorCode = "lease_expired"
	ErrFeatureDenied            ErrorCode = "feature_denied"
	ErrFeatureCapacityExhausted ErrorCode = "feature_capacity_exhausted"
	ErrStaleSequence            ErrorCode = "stale_sequence"
	ErrProofRequired            ErrorCode = "proof_required"
	ErrUnknown                  ErrorCode = "unknown_error"
)

// SymbolonError represents a structured error returned by the Symbolon SDK.
type SymbolonError struct {
	Code       ErrorCode
	Message    string
	StatusCode int
	Detail     string
}

func (e *SymbolonError) Error() string {
	if e.StatusCode > 0 {
		return fmt.Sprintf("symbolon [%s] HTTP %d: %s", e.Code, e.StatusCode, e.Message)
	}
	return fmt.Sprintf("symbolon [%s]: %s", e.Code, e.Message)
}

func newError(code ErrorCode, message string) *SymbolonError {
	return &SymbolonError{
		Code:    code,
		Message: message,
	}
}

func newHttpError(code ErrorCode, statusCode int, message, detail string) *SymbolonError {
	return &SymbolonError{
		Code:       code,
		Message:    message,
		StatusCode: statusCode,
		Detail:     detail,
	}
}
