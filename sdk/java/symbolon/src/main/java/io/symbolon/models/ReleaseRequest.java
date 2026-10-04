package io.symbolon.models;

import com.fasterxml.jackson.annotation.JsonIgnoreProperties;

/**
 * Release payload sent when explicitly vacating a concurrent seat.
 */
@JsonIgnoreProperties(ignoreUnknown = true)
public record ReleaseRequest(
        String reason
) {
    public ReleaseRequest() {
        this("CLIENT_EXIT");
    }
}
