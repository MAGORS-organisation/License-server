# Symbolon Java Client SDK

Official Java client library for the **Symbolon Floating & Enterprise License Server** (.NET 10). Designed for enterprise Spring Boot, Quarkus, Micronaut, and desktop CAD/CAM Java applications.

## Features

- **Concurrent Floating Lease Acquisition**: Acquire, renew, and release concurrent seats over HTTP/REST Minimal APIs.
- **Automated Jittered Heartbeats**: Background scheduled daemon thread maintaining lease validity with $\pm 10\%$ jitter to prevent thundering herd.
- **Hardware Fingerprinting (FPR-1..4)**: Hardware fingerprinting incorporating Hostname, OS platform, CPU architecture, and Machine Identifier with canonical SHA-256 sorting.
- **Stateless A/B Experiment Routing**: Zero-drift sticky session routing matching C#, Go, and Python engines.
- **Post-Quantum Cryptography Readiness**: NIST FIPS 203 (ML-KEM), FIPS 204 (ML-DSA), FIPS 205 (SLH-DSA) algorithm identification and compliance auditing (CNSA 2.0 / EU NIS 2).
- **Graceful Lifecycle Management**: Implements `AutoCloseable` with optional JVM shutdown hook for automatic seat release on application termination.

## Requirements

- **Java 17** or higher (tested with Java 17 and Java 21 LTS)
- **Maven 3.8+** or **Gradle 7+**

## Installation

Add the dependency to your `pom.xml`:

```xml
<dependency>
    <groupId>io.symbolon</groupId>
    <artifactId>symbolon-client</artifactId>
    <version>1.0.0</version>
</dependency>
```

Or in `build.gradle`:

```groovy
implementation 'io.symbolon:symbolon-client:1.0.0'
```

## Quick Start

```java
import io.symbolon.ClientOptions;
import io.symbolon.SymbolonClient;
import io.symbolon.models.CheckoutResponse;

public class App {
    public static void main(String[] args) {
        ClientOptions options = ClientOptions.builder()
                .serverUrl("http://localhost:5000")
                .productCode("cad-app")
                .clientVersion("1.0.0")
                .autoReleaseOnShutdown(true)
                .build();

        try (SymbolonClient client = new SymbolonClient(options)) {
            // Acquire floating seat
            CheckoutResponse lease = client.acquireSeat("SYM-4K7QT-9M2XA-B8ZH3-P6RWY-C1J8N");
            System.out.println("Seat acquired! Lease ID: " + lease.leaseId());

            // Run your enterprise business logic...
            doWork();

            // Lease is automatically renewed in the background and released on close/exit!
        }
    }
}
```

## License

Apache-2.0 License.
