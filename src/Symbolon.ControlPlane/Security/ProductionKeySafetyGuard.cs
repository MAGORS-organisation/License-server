using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Symbolon.ControlPlane.Security;

/// <summary>
/// Enforces normative security rules from §9.3 of the Symbolon Specification.
/// Rule 4: Plaintext PKCS#8 private keys are strictly forbidden in Production environments.
/// The configuration must reject startup if plaintext storage is used in Production.
/// </summary>
public static class ProductionKeySafetyGuard
{
    private static readonly HashSet<string> InsecurePassphrases = new(StringComparer.OrdinalIgnoreCase)
    {
        "dev",
        "development",
        "test",
        "password",
        "123456",
        "symbolon",
        "admin"
    };

    /// <summary>
    /// Evaluates runtime environment and key storage configuration.
    /// Throws InvalidOperationException if safety invariants are violated.
    /// </summary>
    public static void EnforceSafetyRules(IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        if (!environment.IsProduction())
        {
            // Development / Staging allow dev keys with warning
            return;
        }

        string? keyStorage = configuration["SYMBOLON_KEY_STORAGE"] ??
                             configuration["Security:KeyStorage"] ??
                             configuration["KeyStorage"];

        // In Production, explicit plaintext is strictly prohibited
        if (string.Equals(keyStorage, "plaintext", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(keyStorage, "unencrypted", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(keyStorage, "dev", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "CRITICAL SECURITY VIOLATION [§9.3 Rule 4]: The Symbolon server is running in Production environment with unencrypted plaintext private key storage. " +
                "Production deployment strictly requires Cloud KMS (Azure Key Vault, AWS KMS, GCP KMS), Hardware Security Module (PKCS#11), or AES-256-GCM Encrypted Envelope storage. " +
                "Set SYMBOLON_KEY_STORAGE=envelope | azure-kv | aws-kms | pkcs11. Server startup aborted.");
        }

        // Check for trivial passphrase in production if envelope is used
        string? passphrase = configuration["SYMBOLON_MASTER_KEY_PASSPHRASE"] ??
                             configuration["Security:MasterKeyPassphrase"];

        if (!string.IsNullOrWhiteSpace(passphrase) && InsecurePassphrases.Contains(passphrase.Trim()))
        {
            throw new InvalidOperationException(
                "CRITICAL SECURITY VIOLATION [§9.3 Rule 4]: Trivial or default master key passphrase detected in Production environment. " +
                "Please configure a cryptographically strong, high-entropy passphrase via SYMBOLON_MASTER_KEY_PASSPHRASE. Server startup aborted.");
        }
    }
}
