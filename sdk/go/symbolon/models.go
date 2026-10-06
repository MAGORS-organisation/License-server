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

// ReserveTokensRequest represents a pay-as-you-go credit reservation request.
type ReserveTokensRequest struct {
	WalletId          string  `json:"walletId"`
	FeatureCode       string  `json:"featureCode"`
	EstimatedUnits    float64 `json:"estimatedUnits"`
	IsDurationMinutes bool    `json:"isDurationMinutes,omitempty"`
	ReservationTtl    string  `json:"reservationTtl,omitempty"`
	ClientRef         string  `json:"clientRef,omitempty"`
	MachineId         string  `json:"machineId,omitempty"`
}

// ReserveTokensResponse represents the server response after credit reservation.
type ReserveTokensResponse struct {
	Success            bool    `json:"success"`
	ReservationId      string  `json:"reservationId,omitempty"`
	ReservedAmount     float64 `json:"reservedAmount"`
	AvailableBalance   float64 `json:"availableBalance"`
	OverdraftRemaining float64 `json:"overdraftRemaining,omitempty"`
	FailureReason      string  `json:"failureReason,omitempty"`
}

// HeartbeatTokensRequest represents an incremental heartbeat for an active reservation.
type HeartbeatTokensRequest struct {
	ReservationId     string  `json:"reservationId"`
	DeltaUnits        float64 `json:"deltaUnits"`
	IsDurationMinutes bool    `json:"isDurationMinutes,omitempty"`
}

// HeartbeatTokensResponse represents the response to a token heartbeat.
type HeartbeatTokensResponse struct {
	Success           bool    `json:"success"`
	TotalConsumed     float64 `json:"totalConsumed"`
	RemainingReserved float64 `json:"remainingReserved"`
	AvailableBalance  float64 `json:"availableBalance"`
	FailureReason     string  `json:"failureReason,omitempty"`
}

// CommitTokensRequest settles consumed credits upon metered operation completion.
type CommitTokensRequest struct {
	ReservationId     string  `json:"reservationId"`
	ActualUnits       float64 `json:"actualUnits"`
	IsDurationMinutes bool    `json:"isDurationMinutes,omitempty"`
}

// CommitTokensResponse represents the final settlement response of a reservation.
type CommitTokensResponse struct {
	Success         bool    `json:"success"`
	ConsumedCredits float64 `json:"consumedCredits"`
	RefundedCredits float64 `json:"refundedCredits"`
	NewBalance      float64 `json:"newBalance"`
	FailureReason   string  `json:"failureReason,omitempty"`
}

// RollbackTokensRequest restores reserved credits back to the wallet.
type RollbackTokensRequest struct {
	ReservationId string `json:"reservationId"`
	Reason        string `json:"reason,omitempty"`
}

// RollbackTokensResponse represents the server response after a rollback.
type RollbackTokensResponse struct {
	Success         bool    `json:"success"`
	RestoredCredits float64 `json:"restoredCredits"`
	NewBalance      float64 `json:"newBalance"`
	FailureReason   string  `json:"failureReason,omitempty"`
}

// TokenWalletBalance represents real-time balance and overdraft info of a wallet.
type TokenWalletBalance struct {
	WalletId         string  `json:"walletId"`
	WalletCode       string  `json:"walletCode"`
	WalletName       string  `json:"walletName"`
	TotalCredits     float64 `json:"totalCredits"`
	Balance          float64 `json:"balance"`
	ReservedCredits  float64 `json:"reservedCredits"`
	AvailableBalance float64 `json:"availableBalance"`
	OverdraftLimit   float64 `json:"overdraftLimit"`
	State            string  `json:"state"`
	IsLowBalance     bool    `json:"isLowBalance"`
	ExpiresAt        string  `json:"expiresAt,omitempty"`
}

