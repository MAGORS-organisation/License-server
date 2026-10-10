package symbolon

import (
	"crypto/sha256"
	"encoding/hex"
	"errors"
	"strconv"
	"strings"
	"time"
)

var (
	ErrInvalidTokenStructure = errors.New("symbolon: token structure is invalid")
	ErrTokenExpired          = errors.New("symbolon: license token has expired")
	ErrFeatureNotEntitled    = errors.New("symbolon: feature not entitled in license")
)

// ParsedToken represents a 3-part compact license token without heavy allocations.
type ParsedToken struct {
	Header       string
	Payload      string
	Signature    string
	SigningInput string
}

// ParseToken splits raw compact token into its header, payload, and signature components.
func ParseToken(raw string) (*ParsedToken, error) {
	parts := strings.Split(raw, ".")
	if len(parts) != 3 {
		return nil, ErrInvalidTokenStructure
	}
	if parts[0] == "" || parts[1] == "" || parts[2] == "" {
		return nil, ErrInvalidTokenStructure
	}

	return &ParsedToken{
		Header:       parts[0],
		Payload:      parts[1],
		Signature:    parts[2],
		SigningInput: parts[0] + "." + parts[1],
	}, nil
}

// ComputeDigest returns the SHA-256 hex string of the signing input.
func (p *ParsedToken) ComputeDigest() string {
	h := sha256.Sum256([]byte(p.SigningInput))
	return hex.EncodeToString(h[:])
}

// ContainsFeature checks whether feature is present in the payload string without parsing JSON.
func (p *ParsedToken) ContainsFeature(feature string) bool {
	return strings.Contains(p.Payload, feature)
}

// IsExpired checks if the expiration timestamp in the token is in the past.
func (p *ParsedToken) IsExpired(currentTime time.Time) bool {
	idx := strings.Index(p.Payload, "\"exp\":")
	if idx == -1 {
		return false
	}
	rest := p.Payload[idx+6:]
	end := strings.IndexFunc(rest, func(r rune) bool {
		return r < '0' || r > '9'
	})
	if end == -1 {
		end = len(rest)
	}

	expSec, err := strconv.ParseInt(rest[:end], 10, 64)
	if err != nil {
		return false
	}

	return currentTime.Unix() > expSec
}
