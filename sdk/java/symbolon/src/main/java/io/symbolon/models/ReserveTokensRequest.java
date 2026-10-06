package io.symbolon.models;

import com.fasterxml.jackson.annotation.JsonIgnoreProperties;
import com.fasterxml.jackson.annotation.JsonInclude;

@JsonIgnoreProperties(ignoreUnknown = true)
@JsonInclude(JsonInclude.Include.NON_NULL)
public record ReserveTokensRequest(
        String walletId,
        String featureCode,
        Double estimatedUnits,
        Boolean isDurationMinutes,
        String reservationTtl,
        String clientRef,
        String machineId
) {
    public ReserveTokensRequest(String walletId, String featureCode, Double estimatedUnits) {
        this(walletId, featureCode, estimatedUnits, false, null, null, null);
    }
}
