package io.symbolon;

import com.fasterxml.jackson.databind.DeserializationFeature;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.datatype.jsr310.JavaTimeModule;
import io.symbolon.exceptions.*;
import io.symbolon.models.*;

import java.io.IOException;
import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.nio.charset.StandardCharsets;
import java.time.Duration;
import java.time.Instant;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.concurrent.atomic.AtomicLong;
import java.util.concurrent.atomic.AtomicReference;

/**
 * High-performance, thread-safe Java Client SDK for the Symbolon License Server.
 * Supports concurrent floating leases, background jittered heartbeats, FPR-1..4 fingerprinting,
 * and automatic lease cleanup.
 */
public class SymbolonClient implements AutoCloseable {

    private final ClientOptions options;
    private final HttpClient httpClient;
    private final ObjectMapper objectMapper;
    private final Map<String, String> fingerprintComponents;
    private final String canonicalFingerprint;

    private final AtomicReference<CheckoutResponse> activeLease = new AtomicReference<>(null);
    private final AtomicLong clientSequence = new AtomicLong(0);
    private final AtomicBoolean isClosed = new AtomicBoolean(false);

    private final ConcurrentHashMap<String, ActiveFeatureInfo> activeFeatures = new ConcurrentHashMap<>();

    private ScheduledExecutorService heartbeatExecutor;
    private ScheduledFuture<?> heartbeatTask;
    private Thread shutdownHook;

    public SymbolonClient(ClientOptions options) {
        this.options = Objects.requireNonNull(options, "options cannot be null");
        this.httpClient = HttpClient.newBuilder()
                .connectTimeout(options.timeout())
                .build();

        this.objectMapper = new ObjectMapper()
                .registerModule(new JavaTimeModule())
                .configure(DeserializationFeature.FAIL_ON_UNKNOWN_PROPERTIES, false);

        this.fingerprintComponents = Fingerprint.getLocalFingerprintComponents();
        this.canonicalFingerprint = Fingerprint.computeCanonicalFingerprint(this.fingerprintComponents);

        if (options.autoReleaseOnShutdown()) {
            this.shutdownHook = new Thread(this::releaseSeatSafe, "symbolon-shutdown-hook");
            try {
                Runtime.getRuntime().addShutdownHook(this.shutdownHook);
            } catch (IllegalStateException ignored) {
                // JVM is already shutting down
            }
        }
    }

    public static SymbolonClient create(String serverUrl, String productCode) {
        return new SymbolonClient(ClientOptions.createDefault(serverUrl, productCode));
    }

    /**
     * Acquires a concurrent floating lease from the Symbolon server.
     */
    public synchronized CheckoutResponse acquireSeat(String licenseKey, List<String> features, int quantity) {
        ensureNotClosed();
        if (licenseKey == null || licenseKey.isBlank()) {
            throw new IllegalArgumentException("licenseKey cannot be null or empty");
        }

        CheckoutRequest req = new CheckoutRequest(
                licenseKey.trim(),
                this.fingerprintComponents,
                this.fingerprintComponents.get("machineId"),
                System.getProperty("user.name", "unknown"),
                features,
                quantity > 0 ? quantity : 1
        );

        String uri = String.format("%s/v1/leases", normalizeServerUrl(options.serverUrl()));
        CheckoutResponse response = sendPost(uri, req, CheckoutResponse.class);

        this.activeLease.set(response);
        this.clientSequence.set(0);

        startHeartbeatLoop();

        return response;
    }

    /**
     * Acquires a single seat for the given license key.
     */
    public CheckoutResponse acquireSeat(String licenseKey) {
        return acquireSeat(licenseKey, null, 1);
    }

    /**
     * Sends a renewal heartbeat to keep the active floating lease alive.
     */
    public synchronized RenewResponse heartbeat() {
        ensureNotClosed();
        CheckoutResponse currentLease = activeLease.get();
        if (currentLease == null) {
            throw new SymbolonException("NO_ACTIVE_LEASE", 0, "No active lease to renew");
        }

        long seq = clientSequence.incrementAndGet();
        RenewRequest req = new RenewRequest(seq, this.fingerprintComponents);

        String uri = String.format("%s/v1/leases/%s/renew", normalizeServerUrl(options.serverUrl()), currentLease.leaseId());
        RenewResponse response = sendPost(uri, req, RenewResponse.class);

        // Update active lease expiration timestamp
        CheckoutResponse updated = new CheckoutResponse(
                currentLease.leaseId(),
                currentLease.token(),
                currentLease.seat(),
                response.expiresAt(),
                currentLease.entitlements(),
                currentLease.overage()
        );
        this.activeLease.set(updated);

        return response;
    }

    /**
     * Explicitly releases the currently held floating lease.
     */
    public synchronized ReleaseResponse releaseSeat() {
        stopHeartbeatLoop();

        CheckoutResponse currentLease = activeLease.getAndSet(null);
        if (currentLease == null) {
            return new ReleaseResponse(false, "");
        }

        ReleaseRequest req = new ReleaseRequest("CLIENT_RELEASE");
        String uri = String.format("%s/v1/leases/%s/release", normalizeServerUrl(options.serverUrl()), currentLease.leaseId());

        try {
            return sendPost(uri, req, ReleaseResponse.class);
        } catch (SymbolonException ex) {
            // Even if release fails on server (e.g. server already expired lease), locally it is released
            return new ReleaseResponse(false, currentLease.leaseId());
        }
    }

    /**
     * Dynamically acquires an add-on feature under the active lease.
     */
    public FeatureAcquireResponse acquireFeature(String featureCode, String version) {
        ensureNotClosed();
        CheckoutResponse currentLease = activeLease.get();
        if (currentLease == null) {
            throw new SymbolonException("NO_ACTIVE_LEASE", 0, "Must acquire a base lease before acquiring add-on features");
        }

        FeatureAcquireRequest req = new FeatureAcquireRequest(featureCode, version);
        String uri = String.format("%s/v1/leases/%s/features", normalizeServerUrl(options.serverUrl()), currentLease.leaseId());
        FeatureAcquireResponse response = sendPost(uri, req, FeatureAcquireResponse.class);

        if (response.success()) {
            activeFeatures.put(featureCode, new ActiveFeatureInfo(featureCode, version, response.expiresAt()));
        }
        return response;
    }

    /**
     * Releases an add-on feature previously acquired under the active lease.
     */
    public FeatureReleaseResponse releaseFeature(String featureCode) {
        ensureNotClosed();
        CheckoutResponse currentLease = activeLease.get();
        if (currentLease == null) {
            throw new SymbolonException("NO_ACTIVE_LEASE", 0, "No active lease");
        }

        String uri = String.format("%s/v1/leases/%s/features/%s",
                normalizeServerUrl(options.serverUrl()), currentLease.leaseId(), featureCode);

        HttpRequest request = HttpRequest.newBuilder()
                .uri(URI.create(uri))
                .timeout(options.timeout())
                .DELETE()
                .header("Accept", "application/json")
                .header("User-Agent", "Symbolon-Java-SDK/" + options.clientVersion())
                .build();

        try {
            HttpResponse<String> httpResponse = httpClient.send(request, HttpResponse.BodyHandlers.ofString(StandardCharsets.UTF_8));
            handleErrorResponse(httpResponse);
            activeFeatures.remove(featureCode);
            return objectMapper.readValue(httpResponse.body(), FeatureReleaseResponse.class);
        } catch (IOException | InterruptedException e) {
            throw new NetworkException("Failed to release feature " + featureCode, e);
        }
    }

    /**
     * Returns true if an active lease is held and has not expired.
     */
    public boolean isLeaseActive() {
        CheckoutResponse lease = activeLease.get();
        if (lease == null) {
            return false;
        }
        return lease.expiresAt() == null || lease.expiresAt().isAfter(Instant.now());
    }

    /**
     * Returns the active checkout response if currently held.
     */
    public Optional<CheckoutResponse> getActiveLease() {
        return Optional.ofNullable(activeLease.get());
    }

    /**
     * Returns the canonical hardware fingerprint hash for this client.
     */
    public String getCanonicalFingerprint() {
        return canonicalFingerprint;
    }

    /**
     * Returns the raw hardware fingerprint components.
     */
    public Map<String, String> getFingerprintComponents() {
        return Collections.unmodifiableMap(fingerprintComponents);
    }

    private synchronized void startHeartbeatLoop() {
        stopHeartbeatLoop();

        this.heartbeatExecutor = Executors.newSingleThreadScheduledExecutor(r -> {
            Thread t = new Thread(r, "symbolon-heartbeat-worker");
            t.setDaemon(true);
            return t;
        });

        long intervalMillis = options.heartbeatInterval().toMillis();
        scheduleNextHeartbeat(intervalMillis);
    }

    private synchronized void scheduleNextHeartbeat(long baseIntervalMillis) {
        if (heartbeatExecutor == null || heartbeatExecutor.isShutdown()) {
            return;
        }

        // Apply ±10% jitter to prevent thundering herd
        double jitterMultiplier = 0.90 + (ThreadLocalRandom.current().nextDouble() * 0.20);
        long delayMillis = Math.max(1000L, (long) (baseIntervalMillis * jitterMultiplier));

        this.heartbeatTask = heartbeatExecutor.schedule(() -> {
            try {
                if (activeLease.get() != null && !isClosed.get()) {
                    heartbeat();
                    scheduleNextHeartbeat(baseIntervalMillis);
                }
            } catch (Exception ex) {
                // Heartbeat error handling: retry sooner if network error, or cancel if lease expired
                if (ex instanceof LeaseExpiredException) {
                    activeLease.set(null);
                } else {
                    // Retry with short backoff (5 seconds)
                    scheduleNextHeartbeat(5000L);
                }
            }
        }, delayMillis, TimeUnit.MILLISECONDS);
    }

    private synchronized void stopHeartbeatLoop() {
        if (heartbeatTask != null) {
            heartbeatTask.cancel(true);
            heartbeatTask = null;
        }
        if (heartbeatExecutor != null) {
            heartbeatExecutor.shutdownNow();
            heartbeatExecutor = null;
        }
    }

    private void releaseSeatSafe() {
        try {
            releaseSeat();
        } catch (Exception ignored) {
        }
    }

    private <T> T sendPost(String url, Object bodyObj, Class<T> responseClass) {
        try {
            String json = objectMapper.writeValueAsString(bodyObj);
            HttpRequest request = HttpRequest.newBuilder()
                    .uri(URI.create(url))
                    .timeout(options.timeout())
                    .header("Content-Type", "application/json")
                    .header("Accept", "application/json")
                    .header("User-Agent", "Symbolon-Java-SDK/" + options.clientVersion())
                    .POST(HttpRequest.BodyPublishers.ofString(json, StandardCharsets.UTF_8))
                    .build();

            HttpResponse<String> response = httpClient.send(request, HttpResponse.BodyHandlers.ofString(StandardCharsets.UTF_8));
            handleErrorResponse(response);

            return objectMapper.readValue(response.body(), responseClass);
        } catch (IOException | InterruptedException e) {
            if (e instanceof InterruptedException) {
                Thread.currentThread().interrupt();
            }
            throw new NetworkException("HTTP request failed to " + url, e);
        }
    }

    private void handleErrorResponse(HttpResponse<String> response) {
        int status = response.statusCode();
        if (status >= 200 && status < 300) {
            return;
        }

        String body = response.body();
        String message = "HTTP " + status;
        String detail = body;

        try {
            var node = objectMapper.readTree(body);
            if (node.has("message")) {
                message = node.get("message").asText();
            } else if (node.has("error")) {
                message = node.get("error").asText();
            }
            if (node.has("detail")) {
                detail = node.get("detail").asText();
            }
        } catch (Exception ignored) {
        }

        switch (status) {
            case 404 -> throw new LicenseNotFoundException(message, detail);
            case 429 -> throw new CapacityExhaustedException(message, detail);
            case 410 -> throw new LeaseExpiredException(message, detail);
            case 403 -> throw new FeatureDeniedException(message, detail);
            default -> throw new SymbolonException("HTTP_ERROR", status, message, detail);
        }
    }

    private String normalizeServerUrl(String url) {
        if (url == null) return "";
        return url.endsWith("/") ? url.substring(0, url.length() - 1) : url;
    }

    private void ensureNotClosed() {
        if (isClosed.get()) {
            throw new IllegalStateException("SymbolonClient has already been closed");
        }
    }

    @Override
    public synchronized void close() {
        if (isClosed.compareAndSet(false, true)) {
            releaseSeatSafe();
            if (shutdownHook != null) {
                try {
                    Runtime.getRuntime().removeShutdownHook(shutdownHook);
                } catch (Exception ignored) {
                }
            }
        }
    }
}
