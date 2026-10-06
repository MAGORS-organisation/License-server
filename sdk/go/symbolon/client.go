package symbolon

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"math/rand"
	"net/http"
	"net/url"
	"sync"
	"time"
)

// SymbolonClient manages interaction with the Symbolon license server.
type SymbolonClient struct {
	opts       ClientOptions
	httpClient *http.Client
}

// NewClient creates and initializes a new Symbolon client instance.
func NewClient(opts ClientOptions) *SymbolonClient {
	if opts.Timeout <= 0 {
		opts.Timeout = 10 * time.Second
	}
	if opts.HeartbeatInterval <= 0 {
		opts.HeartbeatInterval = 2 * time.Minute
	}
	if opts.GracePeriod <= 0 {
		opts.GracePeriod = 4 * time.Hour
	}

	return &SymbolonClient{
		opts: opts,
		httpClient: &http.Client{
			Timeout: opts.Timeout,
		},
	}
}

// SeatLease represents an actively held floating concurrent seat lease.
type SeatLease struct {
	client     *SymbolonClient
	response   CheckoutResponse
	licenseKey string
	components map[string]string

	mu             sync.RWMutex
	leaseSeq       int64
	expiresAt      time.Time
	activeFeatures map[string]*FeatureLease

	stopHeartbeat chan struct{}
	wg            sync.WaitGroup
	inGrace       bool
	graceStarted  time.Time
	isReleased    bool
}

// FeatureLease represents an actively held add-on feature lease.
type FeatureLease struct {
	parent      *SeatLease
	featureCode string
	isReleased  bool
	mu          sync.Mutex
}

// AcquireSeat acquires a floating concurrent seat lease from the server.
func (c *SymbolonClient) AcquireSeat(ctx context.Context, licenseKey string) (*SeatLease, error) {
	if licenseKey == "" {
		return nil, newError(ErrInvalidArgument, "licenseKey cannot be empty")
	}

	comps := GetLocalFingerprintComponents()
	reqBody := CheckoutRequest{
		LicenseKey:            licenseKey,
		FingerprintComponents: comps,
		MachineId:             comps["machineId"],
	}

	data, err := json.Marshal(reqBody)
	if err != nil {
		return nil, newError(ErrInvalidArgument, fmt.Sprintf("failed to serialize request: %v", err))
	}

	url := fmt.Sprintf("%s/v1/leases", c.opts.ServerURL)
	httpReq, err := http.NewRequestWithContext(ctx, http.MethodPost, url, bytes.NewReader(data))
	if err != nil {
		return nil, newError(ErrNetwork, fmt.Sprintf("failed to create HTTP request: %v", err))
	}

	httpReq.Header.Set("Content-Type", "application/json")
	httpReq.Header.Set("X-Symbolon-Client-Version", c.opts.ClientVersion)

	resp, err := c.httpClient.Do(httpReq)
	if err != nil {
		return nil, newError(ErrNetwork, fmt.Sprintf("checkout HTTP request failed: %v", err))
	}
	defer resp.Body.Close()

	if resp.StatusCode == http.StatusConflict || resp.StatusCode == http.StatusTooManyRequests {
		return nil, newHttpError(ErrCapacityExhausted, resp.StatusCode, "floating pool capacity exhausted (0 seats available)", "")
	}
	if resp.StatusCode == http.StatusNotFound {
		return nil, newHttpError(ErrLicenseNotFound, resp.StatusCode, "license key not found or inactive", "")
	}
	if resp.StatusCode != http.StatusOK && resp.StatusCode != http.StatusCreated {
		return nil, newHttpError(ErrUnknown, resp.StatusCode, fmt.Sprintf("server returned unexpected status %d", resp.StatusCode), "")
	}

	var checkoutResp CheckoutResponse
	if err := json.NewDecoder(resp.Body).Decode(&checkoutResp); err != nil {
		return nil, newError(ErrUnknown, fmt.Sprintf("failed to parse checkout response: %v", err))
	}

	lease := &SeatLease{
		client:         c,
		response:       checkoutResp,
		licenseKey:     licenseKey,
		components:     comps,
		leaseSeq:       0,
		expiresAt:      checkoutResp.ExpiresAt,
		activeFeatures: make(map[string]*FeatureLease),
		stopHeartbeat:  make(chan struct{}),
	}

	// Start background heartbeat loop
	lease.wg.Add(1)
	go lease.heartbeatLoop()

	return lease, nil
}

// LeaseId returns the unique identifier for the seat lease.
func (l *SeatLease) LeaseId() string {
	return l.response.LeaseId
}

// SeatNumber returns the assigned seat index.
func (l *SeatLease) SeatNumber() int {
	return l.response.Seat
}

// Token returns the signed JWS lease token.
func (l *SeatLease) Token() string {
	return l.response.Token
}

// IsValid returns true if the lease has not expired and has not exceeded grace period.
func (l *SeatLease) IsValid() bool {
	l.mu.RLock()
	defer l.mu.RUnlock()

	if l.isReleased {
		return false
	}

	now := time.Now()
	if now.Before(l.expiresAt) {
		return true
	}

	if l.inGrace && now.Sub(l.graceStarted) <= l.client.opts.GracePeriod {
		return true
	}

	return false
}

// HasFeature returns true if a feature is included in the base license entitlements or dynamically acquired.
func (l *SeatLease) HasFeature(featureCode string) bool {
	l.mu.RLock()
	defer l.mu.RUnlock()

	for _, ent := range l.response.Entitlements {
		if ent == "*" || strings.EqualFold(ent, featureCode) {
			return true
		}
	}

	_, exists := l.activeFeatures[strings.ToUpper(featureCode)]
	return exists
}

// UseFeature dynamically acquires an add-on module for this seat lease.
func (l *SeatLease) UseFeature(ctx context.Context, featureCode, version string) (*FeatureLease, error) {
	l.mu.Lock()
	defer l.mu.Unlock()

	if l.isReleased {
		return nil, newError(ErrExpired, "seat lease is already released")
	}

	codeUpper := strings.ToUpper(featureCode)
	if existing, found := l.activeFeatures[codeUpper]; found && !existing.isReleased {
		return existing, nil
	}

	reqBody := FeatureAcquireRequest{
		FeatureCode: featureCode,
		Version:     version,
	}
	data, _ := json.Marshal(reqBody)

	url := fmt.Sprintf("%s/v1/leases/%s/features/acquire", l.client.opts.ServerURL, l.LeaseId())
	httpReq, err := http.NewRequestWithContext(ctx, http.MethodPost, url, bytes.NewReader(data))
	if err != nil {
		return nil, newError(ErrNetwork, err.Error())
	}
	httpReq.Header.Set("Content-Type", "application/json")

	resp, err := l.client.httpClient.Do(httpReq)
	if err != nil {
		return nil, newError(ErrNetwork, err.Error())
	}
	defer resp.Body.Close()

	if resp.StatusCode == http.StatusConflict {
		return nil, newHttpError(ErrFeatureCapacityExhausted, resp.StatusCode, "feature pool exhausted", "")
	}
	if resp.StatusCode == http.StatusForbidden {
		return nil, newHttpError(ErrFeatureDenied, resp.StatusCode, "feature not entitled", "")
	}

	feat := &FeatureLease{
		parent:      l,
		featureCode: codeUpper,
	}
	l.activeFeatures[codeUpper] = feat
	return feat, nil
}

// ReleaseFeature releases an acquired add-on module back to the server pool.
func (f *FeatureLease) Release(ctx context.Context) error {
	f.mu.Lock()
	defer f.mu.Unlock()

	if f.isReleased {
		return nil
	}

	url := fmt.Sprintf("%s/v1/leases/%s/features/release", f.parent.client.opts.ServerURL, f.parent.LeaseId())
	reqBody := map[string]string{"featureCode": f.featureCode}
	data, _ := json.Marshal(reqBody)

	httpReq, err := http.NewRequestWithContext(ctx, http.MethodPost, url, bytes.NewReader(data))
	if err == nil {
		httpReq.Header.Set("Content-Type", "application/json")
		if resp, err := f.parent.client.httpClient.Do(httpReq); err == nil {
			resp.Body.Close()
		}
	}

	f.isReleased = true
	f.parent.mu.Lock()
	delete(f.parent.activeFeatures, f.featureCode)
	f.parent.mu.Unlock()

	return nil
}

// Release releases the seat lease back to the server and stops the background heartbeat.
func (l *SeatLease) Release(ctx context.Context) error {
	l.mu.Lock()
	if l.isReleased {
		l.mu.Unlock()
		return nil
	}
	l.isReleased = true
	close(l.stopHeartbeat)
	l.mu.Unlock()

	l.wg.Wait()

	url := fmt.Sprintf("%s/v1/leases/%s", l.client.opts.ServerURL, l.LeaseId())
	httpReq, err := http.NewRequestWithContext(ctx, http.MethodDelete, url, nil)
	if err != nil {
		return nil
	}

	resp, err := l.client.httpClient.Do(httpReq)
	if err == nil {
		resp.Body.Close()
	}
	return nil
}

// heartbeatLoop sends periodic renewals with adaptive ±10% jitter.
func (l *SeatLease) heartbeatLoop() {
	defer l.wg.Done()

	interval := l.client.opts.HeartbeatInterval
	if interval <= 0 {
		interval = 2 * time.Minute
	}

	for {
		// Calculate ±10% adaptive jitter
		jitterFactor := 0.9 + (rand.Float64() * 0.2) // 0.9 .. 1.1
		sleepDuration := time.Duration(float64(interval) * jitterFactor)

		select {
		case <-l.stopHeartbeat:
			return
		case <-time.After(sleepDuration):
			l.performHeartbeat()
		}
	}
}

func (l *SeatLease) performHeartbeat() {
	l.mu.Lock()
	seq := l.leaseSeq
	leaseId := l.LeaseId()
	l.mu.Unlock()

	reqBody := RenewRequest{
		ClientSeq:             seq,
		FingerprintComponents: l.components,
	}
	data, _ := json.Marshal(reqBody)

	url := fmt.Sprintf("%s/v1/leases/%s/renew", l.client.opts.ServerURL, leaseId)
	ctx, cancel := context.WithTimeout(context.Background(), l.client.opts.Timeout)
	defer cancel()

	httpReq, err := http.NewRequestWithContext(ctx, http.MethodPost, url, bytes.NewReader(data))
	if err != nil {
		l.handleHeartbeatFailure()
		return
	}
	httpReq.Header.Set("Content-Type", "application/json")

	resp, err := l.client.httpClient.Do(httpReq)
	if err != nil {
		l.handleHeartbeatFailure()
		return
	}
	defer resp.Body.Close()

	if resp.StatusCode == http.StatusOK {
		var renewResp RenewResponse
		if err := json.NewDecoder(resp.Body).Decode(&renewResp); err == nil {
			l.mu.Lock()
			l.leaseSeq = renewResp.LeaseSeq
			l.expiresAt = renewResp.ExpiresAt
			l.inGrace = false
			l.mu.Unlock()
		}
	} else {
		l.handleHeartbeatFailure()
	}
}

func (l *SeatLease) handleHeartbeatFailure() {
	l.mu.Lock()
	defer l.mu.Unlock()

	if !l.inGrace {
		l.inGrace = true
		l.graceStarted = time.Now()
	}
}

// TokenReservationScope manages an active metered reservation with auto-rollback semantics.
type TokenReservationScope struct {
	client           *SymbolonClient
	WalletId         string
	ReservationId    string
	FeatureCode      string
	ReservedAmount   float64
	AvailableBalance float64
	isCompleted      bool
	mu               sync.Mutex
}

func (s *TokenReservationScope) IsCompleted() bool {
	s.mu.Lock()
	defer s.mu.Unlock()
	return s.isCompleted
}

func (s *TokenReservationScope) Heartbeat(ctx context.Context, deltaUnits float64, isDurationMinutes bool) (*HeartbeatTokensResponse, error) {
	s.mu.Lock()
	defer s.mu.Unlock()
	if s.isCompleted {
		return nil, newError(ErrInvalidArgument, fmt.Sprintf("reservation '%s' is already completed", s.ReservationId))
	}
	resp, err := s.client.HeartbeatTokens(ctx, HeartbeatTokensRequest{
		ReservationId:     s.ReservationId,
		DeltaUnits:        deltaUnits,
		IsDurationMinutes: isDurationMinutes,
	})
	if err == nil && resp.Success {
		s.AvailableBalance = resp.AvailableBalance
	}
	return resp, err
}

func (s *TokenReservationScope) Commit(ctx context.Context, actualUnits float64, isDurationMinutes bool) (*CommitTokensResponse, error) {
	s.mu.Lock()
	defer s.mu.Unlock()
	if s.isCompleted {
		return nil, newError(ErrInvalidArgument, fmt.Sprintf("reservation '%s' is already completed", s.ReservationId))
	}
	resp, err := s.client.CommitTokens(ctx, CommitTokensRequest{
		ReservationId:     s.ReservationId,
		ActualUnits:       actualUnits,
		IsDurationMinutes: isDurationMinutes,
	})
	if err == nil && resp.Success {
		s.isCompleted = true
		s.AvailableBalance = resp.NewBalance
	}
	return resp, err
}

func (s *TokenReservationScope) Rollback(ctx context.Context, reason string) (*RollbackTokensResponse, error) {
	s.mu.Lock()
	defer s.mu.Unlock()
	if s.isCompleted {
		return &RollbackTokensResponse{Success: true, RestoredCredits: 0, NewBalance: s.AvailableBalance}, nil
	}
	resp, err := s.client.RollbackTokens(ctx, RollbackTokensRequest{
		ReservationId: s.ReservationId,
		Reason:        reason,
	})
	s.isCompleted = true
	if err == nil && resp.Success {
		s.AvailableBalance = resp.NewBalance
	}
	return resp, err
}

// Close ensures uncommitted reservations are rolled back automatically.
func (s *TokenReservationScope) Close() error {
	s.mu.Lock()
	completed := s.isCompleted
	s.mu.Unlock()
	if !completed {
		ctx, cancel := context.WithTimeout(context.Background(), s.client.opts.Timeout)
		defer cancel()
		_, err := s.Rollback(ctx, "Scope closed without explicit commit")
		return err
	}
	return nil
}

// ReserveTokens reserves credits from a wallet for a metered task.
func (c *SymbolonClient) ReserveTokens(ctx context.Context, req ReserveTokensRequest) (*ReserveTokensResponse, error) {
	data, err := json.Marshal(req)
	if err != nil {
		return nil, newError(ErrInvalidArgument, fmt.Sprintf("failed to serialize reserve request: %v", err))
	}

	urlStr := fmt.Sprintf("%s/v1/tokens/reserve", c.opts.ServerURL)
	httpReq, err := http.NewRequestWithContext(ctx, http.MethodPost, urlStr, bytes.NewReader(data))
	if err != nil {
		return nil, newError(ErrNetwork, fmt.Sprintf("failed to create request: %v", err))
	}
	httpReq.Header.Set("Content-Type", "application/json")

	resp, err := c.httpClient.Do(httpReq)
	if err != nil {
		return nil, newError(ErrNetwork, fmt.Sprintf("reserve HTTP request failed: %v", err))
	}
	defer resp.Body.Close()

	var result ReserveTokensResponse
	if err := json.NewDecoder(resp.Body).Decode(&result); err != nil {
		return nil, newError(ErrNetwork, fmt.Sprintf("failed to decode response: %v", err))
	}
	return &result, nil
}

// HeartbeatTokens reports consumption and refreshes a token reservation.
func (c *SymbolonClient) HeartbeatTokens(ctx context.Context, req HeartbeatTokensRequest) (*HeartbeatTokensResponse, error) {
	data, err := json.Marshal(req)
	if err != nil {
		return nil, newError(ErrInvalidArgument, fmt.Sprintf("failed to serialize heartbeat request: %v", err))
	}

	urlStr := fmt.Sprintf("%s/v1/tokens/heartbeat", c.opts.ServerURL)
	httpReq, err := http.NewRequestWithContext(ctx, http.MethodPost, urlStr, bytes.NewReader(data))
	if err != nil {
		return nil, newError(ErrNetwork, fmt.Sprintf("failed to create request: %v", err))
	}
	httpReq.Header.Set("Content-Type", "application/json")

	resp, err := c.httpClient.Do(httpReq)
	if err != nil {
		return nil, newError(ErrNetwork, fmt.Sprintf("heartbeat HTTP request failed: %v", err))
	}
	defer resp.Body.Close()

	var result HeartbeatTokensResponse
	if err := json.NewDecoder(resp.Body).Decode(&result); err != nil {
		return nil, newError(ErrNetwork, fmt.Sprintf("failed to decode response: %v", err))
	}
	return &result, nil
}

// CommitTokens settles a reservation with actual consumed units.
func (c *SymbolonClient) CommitTokens(ctx context.Context, req CommitTokensRequest) (*CommitTokensResponse, error) {
	data, err := json.Marshal(req)
	if err != nil {
		return nil, newError(ErrInvalidArgument, fmt.Sprintf("failed to serialize commit request: %v", err))
	}

	urlStr := fmt.Sprintf("%s/v1/tokens/commit", c.opts.ServerURL)
	httpReq, err := http.NewRequestWithContext(ctx, http.MethodPost, urlStr, bytes.NewReader(data))
	if err != nil {
		return nil, newError(ErrNetwork, fmt.Sprintf("failed to create request: %v", err))
	}
	httpReq.Header.Set("Content-Type", "application/json")

	resp, err := c.httpClient.Do(httpReq)
	if err != nil {
		return nil, newError(ErrNetwork, fmt.Sprintf("commit HTTP request failed: %v", err))
	}
	defer resp.Body.Close()

	var result CommitTokensResponse
	if err := json.NewDecoder(resp.Body).Decode(&result); err != nil {
		return nil, newError(ErrNetwork, fmt.Sprintf("failed to decode response: %v", err))
	}
	return &result, nil
}

// RollbackTokens releases an uncommitted reservation back to the wallet.
func (c *SymbolonClient) RollbackTokens(ctx context.Context, req RollbackTokensRequest) (*RollbackTokensResponse, error) {
	data, err := json.Marshal(req)
	if err != nil {
		return nil, newError(ErrInvalidArgument, fmt.Sprintf("failed to serialize rollback request: %v", err))
	}

	urlStr := fmt.Sprintf("%s/v1/tokens/rollback", c.opts.ServerURL)
	httpReq, err := http.NewRequestWithContext(ctx, http.MethodPost, urlStr, bytes.NewReader(data))
	if err != nil {
		return nil, newError(ErrNetwork, fmt.Sprintf("failed to create request: %v", err))
	}
	httpReq.Header.Set("Content-Type", "application/json")

	resp, err := c.httpClient.Do(httpReq)
	if err != nil {
		return nil, newError(ErrNetwork, fmt.Sprintf("rollback HTTP request failed: %v", err))
	}
	defer resp.Body.Close()

	var result RollbackTokensResponse
	if err := json.NewDecoder(resp.Body).Decode(&result); err != nil {
		return nil, newError(ErrNetwork, fmt.Sprintf("failed to decode response: %v", err))
	}
	return &result, nil
}

// GetTokenWalletBalance retrieves real-time balance and overdraft status.
func (c *SymbolonClient) GetTokenWalletBalance(ctx context.Context, walletId string) (*TokenWalletBalance, error) {
	escapedId := url.PathEscape(walletId)
	urlStr := fmt.Sprintf("%s/v1/tokens/wallets/%s/balance", c.opts.ServerURL, escapedId)

	httpReq, err := http.NewRequestWithContext(ctx, http.MethodGet, urlStr, nil)
	if err != nil {
		return nil, newError(ErrNetwork, fmt.Sprintf("failed to create request: %v", err))
	}
	httpReq.Header.Set("Accept", "application/json")

	resp, err := c.httpClient.Do(httpReq)
	if err != nil {
		return nil, newError(ErrNetwork, fmt.Sprintf("balance HTTP request failed: %v", err))
	}
	defer resp.Body.Close()

	if resp.StatusCode != http.StatusOK {
		return nil, newHttpError(ErrNetwork, resp.StatusCode, "failed to get balance", "")
	}

	var result TokenWalletBalance
	if err := json.NewDecoder(resp.Body).Decode(&result); err != nil {
		return nil, newError(ErrNetwork, fmt.Sprintf("failed to decode response: %v", err))
	}
	return &result, nil
}

// BeginMeteredScope initiates an auto-rollback metered reservation scope.
func (c *SymbolonClient) BeginMeteredScope(ctx context.Context, req ReserveTokensRequest) (*TokenReservationScope, error) {
	res, err := c.ReserveTokens(ctx, req)
	if err != nil {
		return nil, err
	}
	if !res.Success || res.ReservationId == "" {
		reason := res.FailureReason
		if reason == "" {
			reason = "Insufficient credits or wallet inactive"
		}
		return nil, newError(ErrCapacityExhausted, fmt.Sprintf("token reservation failed: %s", reason))
	}

	return &TokenReservationScope{
		client:           c,
		WalletId:         req.WalletId,
		ReservationId:    res.ReservationId,
		FeatureCode:      req.FeatureCode,
		ReservedAmount:   res.ReservedAmount,
		AvailableBalance: res.AvailableBalance,
	}, nil
}

