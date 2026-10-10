using System.Buffers.Text;
using System.Security.Cryptography;

namespace Achilles.Crypto.Hierarchy;

/// <summary>
/// Cryptographic engine for issuing and validating 3-tier key hierarchy certificates (Root ➜ Product ➜ Lease).
/// Conforms to §9.3 key hierarchy rules.
/// </summary>
public static class KeyHierarchyEngine
{
    /// <summary>
    /// Issues a signed delegation certificate from a parent key authority to a subordinate key.
    /// </summary>
    public static SignedKeyCertificate IssueCertificate(
        ISignatureProvider issuerKey,
        KeyTierRole issuerRole,
        JsonWebKeyDto subjectPublicKey,
        KeyTierRole subjectRole,
        DateTimeOffset validFrom,
        DateTimeOffset validUntil)
    {
        ArgumentNullException.ThrowIfNull(issuerKey);
        ArgumentNullException.ThrowIfNull(subjectPublicKey);

        if (!issuerKey.CanSign)
        {
            throw new InvalidOperationException($"Issuer key '{issuerKey.Kid}' cannot sign certificates (private key not available).");
        }

        if (validUntil <= validFrom)
        {
            throw new ArgumentException("Certificate expiration must be greater than validFrom timestamp.", nameof(validUntil));
        }

        // Validate delegation permissions
        if (issuerRole == KeyTierRole.Root && subjectRole != KeyTierRole.Product && subjectRole != KeyTierRole.Root)
        {
            throw new InvalidOperationException("Root keys can only issue Product certificates or self-signed Root anchors.");
        }
        if (issuerRole == KeyTierRole.Product && subjectRole != KeyTierRole.Lease)
        {
            throw new InvalidOperationException("Product keys can only issue Lease certificates.");
        }
        if (issuerRole == KeyTierRole.Lease)
        {
            throw new InvalidOperationException("Lease keys are leaf authorities and cannot delegate or issue subordinate certificates.");
        }

        var protoCert = new SignedKeyCertificate(
            SubjectKid: subjectPublicKey.Kid,
            SubjectRole: subjectRole,
            IssuerKid: issuerKey.Kid,
            IssuerRole: issuerRole,
            PublicKey: subjectPublicKey,
            ValidFrom: validFrom,
            ValidUntil: validUntil,
            Signature: string.Empty);

        byte[] signingInput = protoCert.GetSigningInput();
        byte[] sigBytes = new byte[issuerKey.SignatureSize];
        issuerKey.Sign(signingInput, sigBytes);

        string sigBase64Url = Base64Url.EncodeToString(sigBytes);

        return protoCert with { Signature = sigBase64Url };
    }

    /// <summary>
    /// Verifies a single signed key certificate against the issuer's public key provider.
    /// </summary>
    public static bool VerifyCertificate(SignedKeyCertificate certificate, ISignatureProvider issuerPublicKey)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        ArgumentNullException.ThrowIfNull(issuerPublicKey);

        if (!string.Equals(certificate.IssuerKid, issuerPublicKey.Kid, StringComparison.Ordinal))
        {
            return false;
        }

        byte[] signingInput = certificate.GetSigningInput();
        byte[] sigBytes;
        try
        {
            sigBytes = Base64Url.DecodeFromChars(certificate.Signature);
        }
        catch (FormatException)
        {
            return false;
        }

        return issuerPublicKey.Verify(signingInput, sigBytes);
    }

    /// <summary>
    /// Fully verifies an entire 3-tier key hierarchy chain (Root ➜ Product ➜ Lease) against a trusted Root anchor.
    /// </summary>
    public static KeyChainVerificationResult VerifyChain(
        KeyHierarchyChain chain,
        JsonWebKeyDto rootTrustAnchor,
        DateTimeOffset? validationTime = null)
    {
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentNullException.ThrowIfNull(rootTrustAnchor);

        DateTimeOffset now = validationTime ?? DateTimeOffset.UtcNow;

        // 1. Verify Root Anchor
        if (!string.Equals(chain.RootCertificate.SubjectKid, rootTrustAnchor.Kid, StringComparison.Ordinal))
        {
            return new KeyChainVerificationResult(
                IsValid: false,
                FailureReason: $"Chain root '{chain.RootCertificate.SubjectKid}' does not match trusted anchor '{rootTrustAnchor.Kid}'.",
                RootKid: chain.RootCertificate.SubjectKid,
                ProductKid: chain.ProductCertificate.SubjectKid,
                LeaseKid: chain.LeaseCertificate?.SubjectKid,
                CheckedAt: now);
        }

        using var rootProvider = CreateSignatureVerifier(chain.RootCertificate.PublicKey);

        // Verify Root self-signature
        if (!VerifyCertificate(chain.RootCertificate, rootProvider))
        {
            return new KeyChainVerificationResult(
                IsValid: false,
                FailureReason: "Root anchor certificate signature verification failed.",
                RootKid: chain.RootCertificate.SubjectKid,
                ProductKid: chain.ProductCertificate.SubjectKid,
                LeaseKid: chain.LeaseCertificate?.SubjectKid,
                CheckedAt: now);
        }

        // 2. Verify Product Certificate
        if (chain.ProductCertificate.IssuerRole != KeyTierRole.Root ||
            !string.Equals(chain.ProductCertificate.IssuerKid, chain.RootCertificate.SubjectKid, StringComparison.Ordinal))
        {
            return new KeyChainVerificationResult(
                IsValid: false,
                FailureReason: "Product certificate is not issued by the trusted Root authority.",
                RootKid: chain.RootCertificate.SubjectKid,
                ProductKid: chain.ProductCertificate.SubjectKid,
                LeaseKid: chain.LeaseCertificate?.SubjectKid,
                CheckedAt: now);
        }

        if (now < chain.ProductCertificate.ValidFrom || now > chain.ProductCertificate.ValidUntil)
        {
            return new KeyChainVerificationResult(
                IsValid: false,
                FailureReason: $"Product certificate '{chain.ProductCertificate.SubjectKid}' has expired or is not yet valid (valid: {chain.ProductCertificate.ValidFrom:s} to {chain.ProductCertificate.ValidUntil:s}).",
                RootKid: chain.RootCertificate.SubjectKid,
                ProductKid: chain.ProductCertificate.SubjectKid,
                LeaseKid: chain.LeaseCertificate?.SubjectKid,
                CheckedAt: now);
        }

        if (!VerifyCertificate(chain.ProductCertificate, rootProvider))
        {
            return new KeyChainVerificationResult(
                IsValid: false,
                FailureReason: "Product certificate signature is invalid (failed cryptographic verification against Root).",
                RootKid: chain.RootCertificate.SubjectKid,
                ProductKid: chain.ProductCertificate.SubjectKid,
                LeaseKid: chain.LeaseCertificate?.SubjectKid,
                CheckedAt: now);
        }

        // 3. Verify Lease Certificate (if present)
        if (chain.LeaseCertificate is not null)
        {
            if (chain.LeaseCertificate.IssuerRole != KeyTierRole.Product ||
                !string.Equals(chain.LeaseCertificate.IssuerKid, chain.ProductCertificate.SubjectKid, StringComparison.Ordinal))
            {
                return new KeyChainVerificationResult(
                    IsValid: false,
                    FailureReason: "Lease certificate is not issued by the active Product authority.",
                    RootKid: chain.RootCertificate.SubjectKid,
                    ProductKid: chain.ProductCertificate.SubjectKid,
                    LeaseKid: chain.LeaseCertificate.SubjectKid,
                    CheckedAt: now);
            }

            if (now < chain.LeaseCertificate.ValidFrom || now > chain.LeaseCertificate.ValidUntil)
            {
                return new KeyChainVerificationResult(
                    IsValid: false,
                    FailureReason: $"Lease certificate '{chain.LeaseCertificate.SubjectKid}' has expired (valid: {chain.LeaseCertificate.ValidFrom:s} to {chain.LeaseCertificate.ValidUntil:s}).",
                    RootKid: chain.RootCertificate.SubjectKid,
                    ProductKid: chain.ProductCertificate.SubjectKid,
                    LeaseKid: chain.LeaseCertificate.SubjectKid,
                    CheckedAt: now);
            }

            using var productProvider = CreateSignatureVerifier(chain.ProductCertificate.PublicKey);
            if (!VerifyCertificate(chain.LeaseCertificate, productProvider))
            {
                return new KeyChainVerificationResult(
                    IsValid: false,
                    FailureReason: "Lease certificate signature is invalid (failed cryptographic verification against Product key).",
                    RootKid: chain.RootCertificate.SubjectKid,
                    ProductKid: chain.ProductCertificate.SubjectKid,
                    LeaseKid: chain.LeaseCertificate.SubjectKid,
                    CheckedAt: now);
            }
        }

        return new KeyChainVerificationResult(
            IsValid: true,
            FailureReason: null,
            RootKid: chain.RootCertificate.SubjectKid,
            ProductKid: chain.ProductCertificate.SubjectKid,
            LeaseKid: chain.LeaseCertificate?.SubjectKid,
            CheckedAt: now);
    }

    /// <summary>
    /// Helper to instantiate a public signature verifier from a JWK.
    /// </summary>
    public static ISignatureProvider CreateSignatureVerifier(JsonWebKeyDto jwk)
    {
        ArgumentNullException.ThrowIfNull(jwk);

        if (string.Equals(jwk.Kty, "EC", StringComparison.OrdinalIgnoreCase) && jwk.X is not null && jwk.Y is not null)
        {
            byte[] x = Base64Url.DecodeFromChars(jwk.X);
            byte[] y = Base64Url.DecodeFromChars(jwk.Y);
            return Es256SignatureProvider.ImportPublic(x, y, jwk.Kid);
        }
        else if ((string.Equals(jwk.Kty, "AKP", StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(jwk.Alg, "ML-DSA-65", StringComparison.OrdinalIgnoreCase)) &&
                 jwk.Pub is not null && MlDsaSignatureProvider.IsSupported)
        {
            return MlDsaSignatureProvider.ImportJwk(jwk);
        }

        throw new NotSupportedException($"Cannot create signature verifier for JWK: kty='{jwk.Kty}', alg='{jwk.Alg}'.");
    }
}
