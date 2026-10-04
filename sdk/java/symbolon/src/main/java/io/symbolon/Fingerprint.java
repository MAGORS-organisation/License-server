package io.symbolon;

import java.net.InetAddress;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.util.Collections;
import java.util.HashMap;
import java.util.Map;
import java.util.TreeMap;

/**
 * Utility for gathering canonical hardware fingerprint components (FPR-1 to FPR-4).
 */
public final class Fingerprint {

    private Fingerprint() {
    }

    /**
     * Gathers local machine fingerprint components matching Symbolon FPR specifications.
     */
    public static Map<String, String> getLocalFingerprintComponents() {
        Map<String, String> comps = new HashMap<>();

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
