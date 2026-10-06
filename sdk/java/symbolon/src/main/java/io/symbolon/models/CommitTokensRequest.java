package io.symbolon.models;

import com.fasterxml.jackson.annotation.JsonIgnoreProperties;
import com.fasterxml.jackson.annotation.JsonInclude;

@JsonIgnoreProperties(ignoreUnknown = true)
@JsonInclude(JsonInclude.Include.NON_NULL)
public record CommitTokensRequest(
        String reservationId,
        Double actualUnits,
        Boolean isDurationMinutes
) {
    public CommitTokensRequest(String reservationId, Double actualUnits) {
        this(reservationId, actualUnits, false);
    }
}
