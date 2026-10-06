package io.symbolon;

import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;

import java.util.Map;

import static org.junit.jupiter.api.Assertions.*;

class FingerprintTest {

    @Test
    @DisplayName("Local fingerprint components should contain host, os, cpu, and machineId")
    void testLocalFingerprintComponents() {
        Map<String, String> comps = Fingerprint.getLocalFingerprintComponents();
        assertNotNull(comps);
        assertTrue(comps.containsKey("host"));
        assertTrue(comps.containsKey("os"));
        assertTrue(comps.containsKey("cpu"));
        assertTrue(comps.containsKey("machineId"));
        assertFalse(comps.get("host").isBlank());
    }

    @Test
    @DisplayName("Canonical fingerprint empty components returns empty hash vector")
    void testEmptyFingerprint() {
        String hash = Fingerprint.computeCanonicalFingerprint(Map.of());
        assertEquals("sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", hash);
    }

    @Test
    @DisplayName("Canonical fingerprint sorts keys and produces deterministic hash")
    void testCanonicalSorting() {
        Map<String, String> comps1 = Map.of(
                "os", "Linux",
                "cpu", "x86_64",
                "host", "node-01"
        );
        Map<String, String> comps2 = Map.of(
                "host", "node-01",
                "cpu", "x86_64",
                "os", "Linux"
        );

        String hash1 = Fingerprint.computeCanonicalFingerprint(comps1);
        String hash2 = Fingerprint.computeCanonicalFingerprint(comps2);

        assertEquals(hash1, hash2);
        assertTrue(hash1.startsWith("sha256:"));
        assertEquals(71, hash1.length()); // "sha256:" (7) + 64 hex chars = 71
    }

    @Test
    @DisplayName("Container UUID persistence generates stable UUID in volume")
    void testContainerUuidPersistence() throws Exception {
        java.nio.file.Path tempFile = java.nio.file.Files.createTempFile("symbolon_test_uuid", ".txt");
        try {
            java.nio.file.Files.deleteIfExists(tempFile);
            String uuid1 = Fingerprint.getOrCreatePersistedContainerUuid(tempFile.toString());
            assertNotNull(uuid1);
            assertTrue(uuid1.length() >= 32);

            String uuid2 = Fingerprint.getOrCreatePersistedContainerUuid(tempFile.toString());
            assertEquals(uuid1, uuid2);
        } finally {
            java.nio.file.Files.deleteIfExists(tempFile);
        }
    }
}
