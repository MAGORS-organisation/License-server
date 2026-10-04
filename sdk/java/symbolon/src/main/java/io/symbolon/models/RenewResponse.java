package io.symbolon.models;

import com.fasterxml.jackson.annotation.JsonIgnoreProperties;

import java.time.Instant;

/**
 * Server response after successful heartbeat renewal.
 */
@JsonIgnoreProperties(ignoreUnknown = true)
public record RenewResponse(
        long leaseSeq,
        Instant expiresAt
) {
}
