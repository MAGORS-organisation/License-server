using Symbolon.Crypto;
using Symbolon.Crypto.Hierarchy;
using Symbolon.Crypto.Kms;

namespace Symbolon.ControlPlane.Security;

/// <summary>
/// Manages KMS provider lifecycle and active 3-tier key hierarchy certificates for the Control Plane.
/// </summary>
public sealed class KmsHierarchyManager : IDisposable
{
    private readonly IKmsProvider _kmsProvider;
    private readonly KeyHierarchyChain _currentChain;
    private readonly JsonWebKeyDto _rootAnchor;

    public IKmsProvider KmsProvider => _kmsProvider;
    public KeyHierarchyChain CurrentChain => _currentChain;
    public JsonWebKeyDto RootAnchor => _rootAnchor;

    public KmsHierarchyManager(IKmsProvider? kmsProvider = null)
    {
        // 1. Initialize Root Key (Tier 1)
        using var rootKey = Es256SignatureProvider.GenerateKey("sym-root-master-2026");
        _rootAnchor = rootKey.ExportPublicJwk();

        // 2. Initialize Product Key (Tier 2)
        var productKey = Es256SignatureProvider.GenerateKey("prd-acme-2026-09-es");
        var productJwk = productKey.ExportPublicJwk();

        // 3. Initialize Lease Key (Tier 3)
        var leaseKey = Es256SignatureProvider.GenerateKey("lse-cluster-node-01");
        var leaseJwk = leaseKey.ExportPublicJwk();

        // 4. Issue 3-tier Certificates per §9.3
        var now = DateTimeOffset.UtcNow;
        var rootCert = KeyHierarchyEngine.IssueCertificate(
            issuerKey: rootKey,
            issuerRole: KeyTierRole.Root,
            subjectPublicKey: _rootAnchor,
            subjectRole: KeyTierRole.Root,
            validFrom: now.AddDays(-1),
            validUntil: now.AddYears(10));

        var productCert = KeyHierarchyEngine.IssueCertificate(
            issuerKey: rootKey,
            issuerRole: KeyTierRole.Root,
            subjectPublicKey: productJwk,
            subjectRole: KeyTierRole.Product,
            validFrom: now.AddDays(-1),
            validUntil: now.AddYears(2));

        var leaseCert = KeyHierarchyEngine.IssueCertificate(
            issuerKey: productKey,
            issuerRole: KeyTierRole.Product,
            subjectPublicKey: leaseJwk,
            subjectRole: KeyTierRole.Lease,
            validFrom: now.AddHours(-1),
            validUntil: now.AddDays(30));

        _currentChain = new KeyHierarchyChain(rootCert, productCert, leaseCert);

        // 5. Initialize KMS Provider
        if (kmsProvider is not null)
        {
            _kmsProvider = kmsProvider;
        }
        else
        {
            // Default to Encrypted Envelope KMS Provider
            var envelopeProvider = new EncryptedEnvelopeKmsProvider(
                passphrase: "symbolon_default_envelope_master_key_2026!",
                keyLocationPrefix: "envelope://controlplane");

            // Encrypt and add Product key to envelope store
            byte[] pkcs8Bytes = productKey.ExportPrivateKeyBytes();
            var envelope = EncryptedEnvelopeKeyStore.Encrypt(
                pkcs8Bytes: pkcs8Bytes,
                passphrase: "symbolon_default_envelope_master_key_2026!",
                kid: productKey.Kid,
                alg: productKey.Alg,
                kty: "EC");

            envelopeProvider.AddKey(envelope, productJwk);
            _kmsProvider = envelopeProvider;
        }

        // Clean up temporary instances
        productKey.Dispose();
        leaseKey.Dispose();
    }

    public void Dispose()
    {
        _kmsProvider.Dispose();
    }
}
