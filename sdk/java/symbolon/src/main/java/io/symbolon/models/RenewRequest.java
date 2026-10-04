package io.symbolon.models;

import com.fasterxml.jackson.annotation.JsonIgnoreProperties;
import com.fasterxml.jackson.annotation.JsonInclude;

import java.util.Map;

/**
 * Heartbeat renewal request sent to maintain active seat reservation.
 */
@JsonIgnoreProperties(ignoreUnknown = true)
@JsonInclude(JsonInclude.Include.NON_NULL)
public record RenewRequest(
        long clientSeq,
        Map<String, String> fingerprintComponents
) {
}
