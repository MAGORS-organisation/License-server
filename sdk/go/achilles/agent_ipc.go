package symbolon

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"net/http"
	"time"
)

// AgentStatus represents the state returned by symbolon-agent on 127.0.0.1:8189.
type AgentStatus struct {
	Status         string  `json:"status"`
	MachineID      string  `json:"machineId"`
	LicenseKey     *string `json:"licenseKey"`
	LeaseID        *string `json:"leaseId"`
	SeatNo         *int    `json:"seatNo"`
	OfflineAllowed bool    `json:"offlineAllowed"`
	LastError      *string `json:"lastError"`
	ExpiresAt      *string `json:"expiresAt"`
}

// AgentActionResponse represents the result of acquire, release, or borrow commands.
type AgentActionResponse struct {
	Success bool   `json:"success"`
	Message string `json:"message"`
}

// AgentClient provides communication with the local desktop/tray agent daemon.
type AgentClient struct {
	BaseURL    string
	HTTPClient *http.Client
}

// NewAgentClient creates a new agent client targeting the given base URL (default: http://127.0.0.1:8189).
func NewAgentClient(baseURL string) *AgentClient {
	if baseURL == "" {
		baseURL = "http://127.0.0.1:8189"
	}
	return &AgentClient{
		BaseURL: baseURL,
		HTTPClient: &http.Client{
			Timeout: 5 * time.Second,
		},
	}
}

// GetStatus queries the local agent status at GET /v1/status.
func (c *AgentClient) GetStatus(ctx context.Context) (*AgentStatus, error) {
	req, err := http.NewRequestWithContext(ctx, http.MethodGet, c.BaseURL+"/v1/status", nil)
	if err != nil {
		return nil, err
	}

	resp, err := c.HTTPClient.Do(req)
	if err != nil {
		return nil, fmt.Errorf("symbolon: failed to connect to agent daemon: %w", err)
	}
	defer resp.Body.Close()

	if resp.StatusCode != http.StatusOK {
		return nil, fmt.Errorf("symbolon: agent returned status %d", resp.StatusCode)
	}

	var status AgentStatus
	if err := json.NewDecoder(resp.Body).Decode(&status); err != nil {
		return nil, fmt.Errorf("symbolon: failed to decode agent status: %w", err)
	}

	return &status, nil
}

// Acquire requests a floating seat lease from the agent daemon.
func (c *AgentClient) Acquire(ctx context.Context, clientID string) (*AgentActionResponse, error) {
	payload := map[string]string{"clientId": clientID}
	body, _ := json.Marshal(payload)

	req, err := http.NewRequestWithContext(ctx, http.MethodPost, c.BaseURL+"/v1/acquire", bytes.NewReader(body))
	if err != nil {
		return nil, err
	}
	req.Header.Set("Content-Type", "application/json")

	resp, err := c.HTTPClient.Do(req)
	if err != nil {
		return nil, err
	}
	defer resp.Body.Close()

	var action AgentActionResponse
	if err := json.NewDecoder(resp.Body).Decode(&action); err != nil {
		return nil, err
	}

	return &action, nil
}

// Release returns the active floating lease.
func (c *AgentClient) Release(ctx context.Context) (*AgentActionResponse, error) {
	req, err := http.NewRequestWithContext(ctx, http.MethodPost, c.BaseURL+"/v1/release", nil)
	if err != nil {
		return nil, err
	}

	resp, err := c.HTTPClient.Do(req)
	if err != nil {
		return nil, err
	}
	defer resp.Body.Close()

	var action AgentActionResponse
	if err := json.NewDecoder(resp.Body).Decode(&action); err != nil {
		return nil, err
	}

	return &action, nil
}

// Borrow requests an offline roaming lease for N days.
func (c *AgentClient) Borrow(ctx context.Context, days int) (*AgentActionResponse, error) {
	payload := map[string]int{"days": days}
	body, _ := json.Marshal(payload)

	req, err := http.NewRequestWithContext(ctx, http.MethodPost, c.BaseURL+"/v1/borrow", bytes.NewReader(body))
	if err != nil {
		return nil, err
	}
	req.Header.Set("Content-Type", "application/json")

	resp, err := c.HTTPClient.Do(req)
	if err != nil {
		return nil, err
	}
	defer resp.Body.Close()

	var action AgentActionResponse
	if err := json.NewDecoder(resp.Body).Decode(&action); err != nil {
		return nil, err
	}

	return &action, nil
}
