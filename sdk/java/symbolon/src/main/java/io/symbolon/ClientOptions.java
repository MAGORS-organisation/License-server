package io.symbolon;

import java.time.Duration;

/**
 * Configuration options for SymbolonClient.
 */
public record ClientOptions(
        String serverUrl,
        String productCode,
        String clientVersion,
        Duration timeout,
        Duration heartbeatInterval,
        Duration gracePeriod,
        boolean autoReleaseOnShutdown
) {
    public static final Duration DEFAULT_TIMEOUT = Duration.ofSeconds(10);
    public static final Duration DEFAULT_HEARTBEAT_INTERVAL = Duration.ofMinutes(2);
    public static final Duration DEFAULT_GRACE_PERIOD = Duration.ofHours(4);

    public static ClientOptions createDefault(String serverUrl, String productCode) {
        return new ClientOptions(
                serverUrl,
                productCode,
                "1.0.0",
                DEFAULT_TIMEOUT,
                DEFAULT_HEARTBEAT_INTERVAL,
                DEFAULT_GRACE_PERIOD,
                true
        );
    }

    public static Builder builder() {
        return new Builder();
    }

    public static final class Builder {
        private String serverUrl;
        private String productCode;
        private String clientVersion = "1.0.0";
        private Duration timeout = DEFAULT_TIMEOUT;
        private Duration heartbeatInterval = DEFAULT_HEARTBEAT_INTERVAL;
        private Duration gracePeriod = DEFAULT_GRACE_PERIOD;
        private boolean autoReleaseOnShutdown = true;

        public Builder serverUrl(String serverUrl) {
            this.serverUrl = serverUrl;
            return this;
        }

        public Builder productCode(String productCode) {
            this.productCode = productCode;
            return this;
        }

        public Builder clientVersion(String clientVersion) {
            this.clientVersion = clientVersion;
            return this;
        }

        public Builder timeout(Duration timeout) {
            this.timeout = timeout;
            return this;
        }

        public Builder heartbeatInterval(Duration heartbeatInterval) {
            this.heartbeatInterval = heartbeatInterval;
            return this;
        }

        public Builder gracePeriod(Duration gracePeriod) {
            this.gracePeriod = gracePeriod;
            return this;
        }

        public Builder autoReleaseOnShutdown(boolean autoReleaseOnShutdown) {
            this.autoReleaseOnShutdown = autoReleaseOnShutdown;
            return this;
        }

        public ClientOptions build() {
            if (serverUrl == null || serverUrl.isBlank()) {
                throw new IllegalArgumentException("serverUrl is required");
            }
            if (productCode == null || productCode.isBlank()) {
                throw new IllegalArgumentException("productCode is required");
            }
            return new ClientOptions(
                    serverUrl,
                    productCode,
                    clientVersion != null ? clientVersion : "1.0.0",
                    timeout != null ? timeout : DEFAULT_TIMEOUT,
                    heartbeatInterval != null ? heartbeatInterval : DEFAULT_HEARTBEAT_INTERVAL,
                    gracePeriod != null ? gracePeriod : DEFAULT_GRACE_PERIOD,
                    autoReleaseOnShutdown
            );
        }
    }
}
