package io.symbolon.examples;

import io.symbolon.ClientOptions;
import io.symbolon.SymbolonClient;
import io.symbolon.models.CheckoutResponse;

import java.time.Duration;

/**
 * Example demonstrating concurrent floating lease acquisition and automatic heartbeat in Java.
 */
public class SimpleCheckoutExample {

    public static void main(String[] args) {
        String serverUrl = System.getenv().getOrDefault("SYMBOLON_SERVER_URL", "http://localhost:5000");
        String licenseKey = System.getenv().getOrDefault("SYMBOLON_LICENSE_KEY", "SYM-4K7QT-9M2XA-B8ZH3-P6RWY-C1J8N");

        ClientOptions options = ClientOptions.builder()
                .serverUrl(serverUrl)
                .productCode("enterprise-cad")
                .clientVersion("2.4.0")
                .timeout(Duration.ofSeconds(10))
                .heartbeatInterval(Duration.ofSeconds(30))
                .autoReleaseOnShutdown(true)
                .build();

        System.out.println("Connecting to Symbolon License Server at: " + serverUrl);

        try (SymbolonClient client = new SymbolonClient(options)) {
            System.out.println("Local Fingerprint: " + client.getCanonicalFingerprint());

            System.out.println("Requesting seat checkout for key: " + licenseKey);
            CheckoutResponse lease = client.acquireSeat(licenseKey);

            System.out.printf("Seat Acquired! LeaseId=%s, SeatNumber=%d, ExpiresAt=%s%n",
                    lease.leaseId(), lease.seat(), lease.expiresAt());

            System.out.println("Active entitlements: " + lease.entitlements());
            System.out.println("Background heartbeat loop is actively running (press Ctrl+C to exit)...");

            // Simulate application work for 10 seconds
            Thread.sleep(10000);

            System.out.println("Application work finished. Explicitly releasing seat...");
            var release = client.releaseSeat();
            System.out.println("Seat released successfully: " + release.released());
        } catch (Exception ex) {
            System.err.println("Licensing error: " + ex.getMessage());
            ex.printStackTrace();
        }
    }
}
