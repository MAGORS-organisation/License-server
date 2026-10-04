package io.symbolon.models;

import com.fasterxml.jackson.annotation.JsonIgnoreProperties;

/**
 * Server confirmation of released seat.
 */
@JsonIgnoreProperties(ignoreUnknown = true)
public record ReleaseResponse(
        boolean released,
        String leaseId
) {
}
