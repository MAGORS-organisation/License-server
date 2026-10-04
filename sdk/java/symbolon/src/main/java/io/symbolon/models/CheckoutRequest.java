package io.symbolon.models;

import com.fasterxml.jackson.annotation.JsonIgnoreProperties;
import com.fasterxml.jackson.annotation.JsonInclude;

import java.util.List;
import java.util.Map;

/**
 * Parameters sent to acquire a floating concurrent seat.
 */
@JsonIgnoreProperties(ignoreUnknown = true)
@JsonInclude(JsonInclude.Include.NON_NULL)
public record CheckoutRequest(
        String licenseKey,
        Map<String, String> fingerprintComponents,
        String machineId,
        String userId,
        List<String> features,
        Integer quantity
) {
    public CheckoutRequest(String licenseKey, Map<String, String> fingerprintComponents) {
        this(licenseKey, fingerprintComponents, null, null, null, null);
    }
}
