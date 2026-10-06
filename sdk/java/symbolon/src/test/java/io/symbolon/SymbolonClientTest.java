package io.symbolon;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.datatype.jsr310.JavaTimeModule;
import com.sun.net.httpserver.HttpServer;
import io.symbolon.exceptions.CapacityExhaustedException;
import io.symbolon.exceptions.LicenseNotFoundException;
import io.symbolon.models.CheckoutResponse;
import io.symbolon.models.FeatureAcquireResponse;
import io.symbolon.models.FeatureReleaseResponse;
import io.symbolon.models.ReleaseResponse;
import io.symbolon.models.RenewResponse;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;

import java.io.IOException;
import java.io.OutputStream;
import java.net.InetSocketAddress;
import java.nio.charset.StandardCharsets;
import java.time.Duration;
import java.time.Instant;
import java.util.List;

import static org.junit.jupiter.api.Assertions.*;

class SymbolonClientTest {

    private HttpServer mockServer;
    private int mockPort;
    private final ObjectMapper mapper = new ObjectMapper().registerModule(new JavaTimeModule());

    @BeforeEach
    void setUp() throws IOException {
        mockServer = HttpServer.create(new InetSocketAddress(0), 0);
        mockPort = mockServer.getAddress().getPort();
        mockServer.start();
    }

    @AfterEach
    void tearDown() {
        if (mockServer != null) {
            mockServer.stop(0);
        }
    }

    @Test
    @DisplayName("AcquireSeat, heartbeat, and release flow successfully")
    void testEndToEndClientFlow() throws Exception {
        String testLeaseId = "lease_test_12345";
        Instant expTime = Instant.now().plus(Duration.ofMinutes(10));

        // Mock /v1/leases POST
        mockServer.createContext("/v1/leases", exchange -> {
            if ("POST".equalsIgnoreCase(exchange.getRequestMethod())) {
                CheckoutResponse resp = new CheckoutResponse(
                        testLeaseId,
                        "token_payload_abc",
                        1,
                        expTime,
                        List.of("core", "cad-export"),
                        false
                );
                byte[] bytes = mapper.writeValueAsBytes(resp);
                exchange.getResponseHeaders().set("Content-Type", "application/json");
                exchange.sendResponseHeaders(200, bytes.length);
                try (OutputStream os = exchange.getResponseBody()) {
                    os.write(bytes);
                }
            } else {
                exchange.sendResponseHeaders(405, -1);
            }
        });

        // Mock /v1/leases/{id}/renew POST
        mockServer.createContext("/v1/leases/" + testLeaseId + "/renew", exchange -> {
            RenewResponse resp = new RenewResponse(1L, expTime.plus(Duration.ofMinutes(10)));
            byte[] bytes = mapper.writeValueAsBytes(resp);
            exchange.getResponseHeaders().set("Content-Type", "application/json");
            exchange.sendResponseHeaders(200, bytes.length);
            try (OutputStream os = exchange.getResponseBody()) {
                os.write(bytes);
            }
        });

        // Mock /v1/leases/{id}/release POST
        mockServer.createContext("/v1/leases/" + testLeaseId + "/release", exchange -> {
            ReleaseResponse resp = new ReleaseResponse(true, testLeaseId);
            byte[] bytes = mapper.writeValueAsBytes(resp);
            exchange.getResponseHeaders().set("Content-Type", "application/json");
            exchange.sendResponseHeaders(200, bytes.length);
            try (OutputStream os = exchange.getResponseBody()) {
                os.write(bytes);
            }
        });

        ClientOptions options = ClientOptions.builder()
                .serverUrl("http://localhost:" + mockPort)
                .productCode("cad-app")
                .timeout(Duration.ofSeconds(2))
                .heartbeatInterval(Duration.ofMinutes(1))
                .autoReleaseOnShutdown(false)
                .build();

        try (SymbolonClient client = new SymbolonClient(options)) {
            // 1. Acquire
            CheckoutResponse checkout = client.acquireSeat("SYM-TEST-KEY-001");
            assertNotNull(checkout);
            assertEquals(testLeaseId, checkout.leaseId());
            assertTrue(client.isLeaseActive());
            assertEquals(testLeaseId, client.getActiveLease().orElseThrow().leaseId());

            // 2. Heartbeat
            RenewResponse renew = client.heartbeat();
            assertNotNull(renew);
            assertEquals(1L, renew.leaseSeq());

            // 3. Release
            ReleaseResponse release = client.releaseSeat();
            assertTrue(release.released());
            assertFalse(client.isLeaseActive());
        }
    }

    @Test
    @DisplayName("AcquireSeat maps HTTP 429 to CapacityExhaustedException")
    void testCapacityExhausted() {
        mockServer.createContext("/v1/leases", exchange -> {
            String errJson = "{\"error\":\"CAPACITY_EXHAUSTED\",\"message\":\"All floating seats in pool are occupied\"}";
            byte[] bytes = errJson.getBytes(StandardCharsets.UTF_8);
            exchange.getResponseHeaders().set("Content-Type", "application/json");
            exchange.sendResponseHeaders(429, bytes.length);
            try (OutputStream os = exchange.getResponseBody()) {
                os.write(bytes);
            }
        });

        ClientOptions options = ClientOptions.builder()
                .serverUrl("http://localhost:" + mockPort)
                .productCode("cad-app")
                .autoReleaseOnShutdown(false)
                .build();

        try (SymbolonClient client = new SymbolonClient(options)) {
            assertThrows(CapacityExhaustedException.class, () -> client.acquireSeat("SYM-TEST-KEY-001"));
        }
    }

    @Test
    @DisplayName("AcquireSeat maps HTTP 404 to LicenseNotFoundException")
    void testLicenseNotFound() {
        mockServer.createContext("/v1/leases", exchange -> {
            String errJson = "{\"error\":\"LICENSE_NOT_FOUND\",\"message\":\"No such license key registered\"}";
            byte[] bytes = errJson.getBytes(StandardCharsets.UTF_8);
            exchange.getResponseHeaders().set("Content-Type", "application/json");
            exchange.sendResponseHeaders(404, bytes.length);
            try (OutputStream os = exchange.getResponseBody()) {
                os.write(bytes);
            }
        });

        ClientOptions options = ClientOptions.builder()
                .serverUrl("http://localhost:" + mockPort)
                .productCode("cad-app")
                .autoReleaseOnShutdown(false)
                .build();

        try (SymbolonClient client = new SymbolonClient(options)) {
            assertThrows(LicenseNotFoundException.class, () -> client.acquireSeat("SYM-INVALID-KEY"));
        }
    }

    @Test
    @DisplayName("Metered token reserve and explicit commit settles credits")
    void testTokenReserveAndCommit() {
        mockServer.createContext("/v1/tokens/reserve", exchange -> {
            var resp = new io.symbolon.models.ReserveTokensResponse(true, "res_java_1", 100.0, 900.0, 100.0, null);
            byte[] bytes = mapper.writeValueAsBytes(resp);
            exchange.getResponseHeaders().set("Content-Type", "application/json");
            exchange.sendResponseHeaders(200, bytes.length);
            try (OutputStream os = exchange.getResponseBody()) {
                os.write(bytes);
            }
        });

        mockServer.createContext("/v1/tokens/commit", exchange -> {
            var resp = new io.symbolon.models.CommitTokensResponse(true, 80.0, 20.0, 920.0, null);
            byte[] bytes = mapper.writeValueAsBytes(resp);
            exchange.getResponseHeaders().set("Content-Type", "application/json");
            exchange.sendResponseHeaders(200, bytes.length);
            try (OutputStream os = exchange.getResponseBody()) {
                os.write(bytes);
            }
        });

        ClientOptions options = ClientOptions.builder()
                .serverUrl("http://localhost:" + mockPort)
                .productCode("cad-app")
                .autoReleaseOnShutdown(false)
                .build();

        try (SymbolonClient client = new SymbolonClient(options)) {
            try (TokenReservationScope scope = client.beginMeteredScope(new io.symbolon.models.ReserveTokensRequest("wlt_1", "render", 100.0))) {
                assertEquals("res_java_1", scope.getReservationId());
                assertEquals(900.0, scope.getAvailableBalance());

                var commitResp = scope.commit(80.0, false);
                assertTrue(commitResp.success());
                assertTrue(scope.isCompleted());
                assertEquals(920.0, scope.getAvailableBalance());
            }
        }
    }

    @Test
    @DisplayName("Metered token auto-rollback occurs when scope closes without commit")
    void testTokenAutoRollbackOnClose() {
        java.util.concurrent.atomic.AtomicBoolean rollbackCalled = new java.util.concurrent.atomic.AtomicBoolean(false);

        mockServer.createContext("/v1/tokens/reserve", exchange -> {
            var resp = new io.symbolon.models.ReserveTokensResponse(true, "res_auto_rb", 50.0, 950.0, 50.0, null);
            byte[] bytes = mapper.writeValueAsBytes(resp);
            exchange.getResponseHeaders().set("Content-Type", "application/json");
            exchange.sendResponseHeaders(200, bytes.length);
            try (OutputStream os = exchange.getResponseBody()) {
                os.write(bytes);
            }
        });

        mockServer.createContext("/v1/tokens/rollback", exchange -> {
            rollbackCalled.set(true);
            var resp = new io.symbolon.models.RollbackTokensResponse(true, 50.0, 1000.0, null);
            byte[] bytes = mapper.writeValueAsBytes(resp);
            exchange.getResponseHeaders().set("Content-Type", "application/json");
            exchange.sendResponseHeaders(200, bytes.length);
            try (OutputStream os = exchange.getResponseBody()) {
                os.write(bytes);
            }
        });

        ClientOptions options = ClientOptions.builder()
                .serverUrl("http://localhost:" + mockPort)
                .productCode("cad-app")
                .autoReleaseOnShutdown(false)
                .build();

        try (SymbolonClient client = new SymbolonClient(options)) {
            try (TokenReservationScope scope = client.beginMeteredScope(new io.symbolon.models.ReserveTokensRequest("wlt_1", "nlp", 50.0))) {
                assertEquals("res_auto_rb", scope.getReservationId());
                // Close scope without committing
            }
            assertTrue(rollbackCalled.get(), "expected rollback to be called on uncommitted scope close");
        }
    }
}
