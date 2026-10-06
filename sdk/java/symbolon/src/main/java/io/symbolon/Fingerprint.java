package io.symbolon;

import java.io.File;
import java.io.IOException;
import java.net.InetAddress;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.util.Collections;
import java.util.HashMap;
import java.util.Map;
import java.util.TreeMap;
import java.util.UUID;

/**
 * Utility for gathering canonical hardware fingerprint components, container isolation,
 * and fuzzy matching (FPR-1 to FPR-4, FPR-10 to FPR-14).
 */
public final class Fingerprint {

    private Fingerprint() {
    }

    /**
     * Detects if the runtime environment is inside Docker, Kubernetes, or cloud instances (FPR-10, FPR-11).
     */
    public static boolean isContainerOrCloud() {
        // 1. Container marker files
        if (new File("/.dockerenv").exists() || new File("/run/.containerenv").exists()) {
            return true;
        }

        // 2. Linux /proc/1/cgroup inspection
        File cgroupFile = new File("/proc/1/cgroup");
        if (cgroupFile.exists()) {
            try {
                String content = Files.readString(cgroupFile.toPath(), StandardCharsets.UTF_8).toLowerCase();
                if (content.contains("docker") || content.contains("containerd") ||
                    content.contains("kubepods") || content.contains("lxc")) {
                    return true;
                }
            } catch (Exception ignored) {
            }
        }

        // 3. Container and Cloud environment variables (FPR-11)
        String[] cloudEnvVars = new String[]{
            "KUBERNETES_SERVICE_HOST",
            "container",
            "DOTNET_RUNNING_IN_CONTAINER",
            "AWS_EXECUTION_ENV",
            "ECS_CONTAINER_METADATA_URI",
            "AZURE_CONTAINER_APP_NAME",
            "GOOGLE_CLOUD_PROJECT"
        };
        for (String env : cloudEnvVars) {
            String val = System.getenv(env);
            if (val != null && !val.isBlank()) {
                return true;
            }
        }

        return false;
    }

    /**
     * Retrieves or generates a persistent random UUID in volume storage for container environments (FPR-12).
     */
    public static String getOrCreatePersistedContainerUuid(String customVolumePath) {
        Path path;
        if (customVolumePath != null && !customVolumePath.isBlank()) {
            path = Path.of(customVolumePath);
        } else {
            String localApp = System.getenv("LOCALAPPDATA");
            Path dir;
            if (localApp != null && !localApp.isBlank()) {
                dir = Path.of(localApp, "symbolon");
            } else {
                String home = System.getProperty("user.home", ".");
                dir = Path.of(home, ".symbolon");
            }
            try {
                Files.createDirectories(dir);
            } catch (IOException ignored) {
            }
            path = dir.resolve("container_instance_uuid.txt");
        }

        try {
            if (Files.exists(path)) {
                String content = Files.readString(path, StandardCharsets.UTF_8).trim();
                if (content.length() >= 32) {
                    return content;
                }
            }
            String newUuid = UUID.randomUUID().toString();
            Files.writeString(path, newUuid, StandardCharsets.UTF_8);
            return newUuid;
        } catch (Exception e) {
            return "uuid-" + UUID.randomUUID();
        }
    }

    /**
     * Gathers local machine fingerprint components matching Symbolon FPR specifications.
     */
    public static Map<String, String> getLocalFingerprintComponents() {
        return getLocalFingerprintComponents(null);
    }

    /**
     * Gathers local machine or container fingerprint components with custom volume path (FPR-10, FPR-12).
     */
    public static Map<String, String> getLocalFingerprintComponents(String customVolumePath) {
        Map<String, String> comps = new HashMap<>();

        if (isContainerOrCloud()) {
            // FPR-12: If container or cloud detected, NEVER use hardware!
            // FPR-13: Log recommendation to use floating license with short TTL
            System.err.println("WARNING: Containerized or cloud environment detected. Hardware node-locking is an anti-pattern in containers. Recommended: floating license with short lease TTL (FPR-13).");
            comps.put("machineId", getOrCreatePersistedContainerUuid(customVolumePath));
            comps.put("isContainer", "true");
            comps.put("os", System.getProperty("os.name", "unknown").trim());
            comps.put("cpu", System.getProperty("os.arch", "unknown").trim());
            return Collections.unmodifiableMap(comps);
        }

        // 1. Hostname
        String hostname = "localhost";
        try {
            hostname = InetAddress.getLocalHost().getHostName();
            if (hostname != null && !hostname.isBlank()) {
                hostname = hostname.trim();
            } else {
                hostname = "localhost";
            }
        } catch (Exception ignored) {
        }
        comps.put("host", hostname);

        // 2. OS platform
        String os = System.getProperty("os.name", "unknown");
        comps.put("os", os.trim());

        // 3. CPU architecture
        String arch = System.getProperty("os.arch", "unknown");
        comps.put("cpu", arch.trim());

        // 4. Machine identifier
        String machineId = System.getenv("SYMBOLON_MACHINE_ID");
        if (machineId == null || machineId.isBlank()) {
            machineId = System.getenv("COMPUTERNAME");
        }
        if (machineId == null || machineId.isBlank()) {
            machineId = System.getenv("HOSTNAME");
        }
        if (machineId == null || machineId.isBlank()) {
            machineId = hostname;
        }
        comps.put("machineId", machineId.trim());

        return Collections.unmodifiableMap(comps);
    }

    /**
     * Calculates the canonical SHA-256 fingerprint representation: "sha256:<hex>".
     */
    public static String computeCanonicalFingerprint(Map<String, String> components) {
        if (components == null || components.isEmpty()) {
            return "sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
        }

        // Sort keys lexicographically
        TreeMap<String, String> sorted = new TreeMap<>();
        for (Map.Entry<String, String> entry : components.entrySet()) {
            sorted.put(entry.getKey().toLowerCase(), entry.getValue() != null ? entry.getValue().trim() : "");
        }

        StringBuilder sb = new StringBuilder();
        boolean first = true;
        for (Map.Entry<String, String> entry : sorted.entrySet()) {
            if (!first) {
                sb.append(';');
            }
            sb.append(entry.getKey()).append('=').append(entry.getValue());
            first = false;
        }

        try {
            MessageDigest digest = MessageDigest.getInstance("SHA-256");
            byte[] hash = digest.digest(sb.toString().getBytes(StandardCharsets.UTF_8));
            return "sha256:" + toHexString(hash);
        } catch (NoSuchAlgorithmException e) {
            throw new IllegalStateException("SHA-256 algorithm not available", e);
        }
    }

    /**
     * Returns canonical SHA-256 fingerprint for the current running environment.
     */
    public static String getLocalFingerprint() {
        return computeCanonicalFingerprint(getLocalFingerprintComponents());
    }

    private static String toHexString(byte[] bytes) {
        StringBuilder sb = new StringBuilder(bytes.length * 2);
        for (byte b : bytes) {
            sb.append(String.format("%02x", b));
        }
        return sb.toString();
    }
}
