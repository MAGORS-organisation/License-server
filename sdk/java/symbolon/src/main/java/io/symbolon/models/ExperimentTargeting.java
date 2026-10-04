package io.symbolon.models;

import com.fasterxml.jackson.annotation.JsonIgnoreProperties;

import java.util.List;
import java.util.Map;

/**
 * Targeting rules determining if a client is eligible for experiment assignment.
 */
@JsonIgnoreProperties(ignoreUnknown = true)
public record ExperimentTargeting(
        List<String> targetTenants,
        List<String> licenseKeyPrefixes,
        List<String> allowedSdkVersions,
        List<String> operatingSystems
) {
    public boolean matches(String tenantId, String licenseKey, Map<String, String> context) {
        if (targetTenants != null && !targetTenants.isEmpty()) {
            if (tenantId == null || !targetTenants.contains(tenantId)) {
                return false;
            }
        }

        if (licenseKeyPrefixes != null && !licenseKeyPrefixes.isEmpty()) {
            if (licenseKey == null) {
                return false;
            }
            boolean matched = false;
            for (String prefix : licenseKeyPrefixes) {
                if (licenseKey.startsWith(prefix)) {
                    matched = true;
                    break;
                }
            }
            if (!matched) {
                return false;
            }
        }

        if (allowedSdkVersions != null && !allowedSdkVersions.isEmpty()) {
            String sdkVer = context != null ? context.get("sdk_version") : null;
            if (sdkVer == null || !allowedSdkVersions.contains(sdkVer)) {
                return false;
            }
        }

        if (operatingSystems != null && !operatingSystems.isEmpty()) {
            String osName = context != null ? context.get("os") : null;
            if (osName == null) {
                return false;
            }
            boolean matched = false;
            for (String os : operatingSystems) {
                if (osName.toLowerCase().contains(os.toLowerCase())) {
                    matched = true;
                    break;
                }
            }
            if (!matched) {
                return false;
            }
        }

        return true;
    }
}
