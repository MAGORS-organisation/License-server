using FluentAssertions;
using Achilles.Crypto.Hierarchy;
using Xunit;

namespace Achilles.Crypto.Tests;

public sealed class KeyHierarchyTests
{
    [Fact]
    public void KeyHierarchy_FullChain_VerifiesSuccessfully()
    {
        using var rootKey = Es256SignatureProvider.GenerateKey("root-authority-2026");
        var rootJwk = rootKey.ExportPublicJwk();

        using var productKey = Es256SignatureProvider.GenerateKey("prd-symbolon-core-2026");
        var productJwk = productKey.ExportPublicJwk();

        using var leaseKey = Es256SignatureProvider.GenerateKey("lse-onprem-relay-01");
        var leaseJwk = leaseKey.ExportPublicJwk();

        var now = DateTimeOffset.UtcNow;

        // Tier 1: Root self-signed certificate
        var rootCert = KeyHierarchyEngine.IssueCertificate(
            issuerKey: rootKey,
            issuerRole: KeyTierRole.Root,
            subjectPublicKey: rootJwk,
            subjectRole: KeyTierRole.Root,
            validFrom: now.AddDays(-1),
            validUntil: now.AddYears(10));

        // Tier 2: Product certificate signed by Root
        var productCert = KeyHierarchyEngine.IssueCertificate(
            issuerKey: rootKey,
            issuerRole: KeyTierRole.Root,
            subjectPublicKey: productJwk,
            subjectRole: KeyTierRole.Product,
            validFrom: now.AddDays(-1),
            validUntil: now.AddYears(2));

        // Tier 3: Lease certificate signed by Product
        var leaseCert = KeyHierarchyEngine.IssueCertificate(
            issuerKey: productKey,
            issuerRole: KeyTierRole.Product,
            subjectPublicKey: leaseJwk,
            subjectRole: KeyTierRole.Lease,
            validFrom: now.AddHours(-1),
            validUntil: now.AddDays(30));

        var chain = new KeyHierarchyChain(rootCert, productCert, leaseCert);

        // Verify chain against Root anchor
        var result = KeyHierarchyEngine.VerifyChain(chain, rootJwk, now);

        result.IsValid.Should().BeTrue();
        result.FailureReason.Should().BeNull();
        result.RootKid.Should().Be("root-authority-2026");
        result.ProductKid.Should().Be("prd-symbolon-core-2026");
        result.LeaseKid.Should().Be("lse-onprem-relay-01");
    }

    [Fact]
    public void KeyHierarchy_TamperedProductCertificate_FailsVerification()
    {
        using var rootKey = Es256SignatureProvider.GenerateKey("root-2026");
        var rootJwk = rootKey.ExportPublicJwk();

        using var productKey = Es256SignatureProvider.GenerateKey("prd-2026");
        var productJwk = productKey.ExportPublicJwk();

        var now = DateTimeOffset.UtcNow;

        var rootCert = KeyHierarchyEngine.IssueCertificate(
            rootKey, KeyTierRole.Root, rootJwk, KeyTierRole.Root, now.AddDays(-1), now.AddYears(10));

        var productCert = KeyHierarchyEngine.IssueCertificate(
            rootKey, KeyTierRole.Root, productJwk, KeyTierRole.Product, now.AddDays(-1), now.AddYears(2));

        // Tamper with subject Kid without updating signature
        var tamperedProductCert = productCert with { SubjectKid = "prd-rogue-2026" };

        var chain = new KeyHierarchyChain(rootCert, tamperedProductCert, null);
        var result = KeyHierarchyEngine.VerifyChain(chain, rootJwk, now);

        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Contain("invalid");
    }

    [Fact]
    public void KeyHierarchy_ExpiredProductCertificate_FailsVerification()
    {
        using var rootKey = Es256SignatureProvider.GenerateKey("root-2026");
        var rootJwk = rootKey.ExportPublicJwk();

        using var productKey = Es256SignatureProvider.GenerateKey("prd-2026");
        var productJwk = productKey.ExportPublicJwk();

        var now = DateTimeOffset.UtcNow;

        var rootCert = KeyHierarchyEngine.IssueCertificate(
            rootKey, KeyTierRole.Root, rootJwk, KeyTierRole.Root, now.AddYears(-5), now.AddYears(10));

        // Product certificate expired yesterday
        var productCert = KeyHierarchyEngine.IssueCertificate(
            rootKey, KeyTierRole.Root, productJwk, KeyTierRole.Product, now.AddYears(-2), now.AddDays(-1));

        var chain = new KeyHierarchyChain(rootCert, productCert, null);
        var result = KeyHierarchyEngine.VerifyChain(chain, rootJwk, now);

        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Contain("expired");
    }

    [Fact]
    public void KeyHierarchy_RoleViolation_ThrowsInvalidOperationException()
    {
        using var leaseKey = Es256SignatureProvider.GenerateKey("lease-key");
        var someJwk = leaseKey.ExportPublicJwk();
        var now = DateTimeOffset.UtcNow;

        // Lease keys cannot issue subordinate certificates
        var act = () => KeyHierarchyEngine.IssueCertificate(
            issuerKey: leaseKey,
            issuerRole: KeyTierRole.Lease,
            subjectPublicKey: someJwk,
            subjectRole: KeyTierRole.Lease,
            validFrom: now,
            validUntil: now.AddDays(1));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Lease keys are leaf authorities*");
    }
}
