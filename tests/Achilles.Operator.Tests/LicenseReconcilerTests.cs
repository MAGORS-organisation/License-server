using System.Text;
using FluentAssertions;
using Achilles.Crypto;
using Achilles.Format;
using Achilles.Operator.Reconcilers;
using Achilles.Protocol.K8s;
using Xunit;

namespace Achilles.Operator.Tests;

public sealed class LicenseReconcilerTests
{
    [Fact]
    public void Reconcile_ValidLicense_GeneratesSignedLicenseSecret_And_StatusActive()
    {
        using var signingKey = Es256SignatureProvider.GenerateKey("k8s-operator-key");
        var reconciler = new LicenseReconciler(signingKey);

        var license = new AchillesLicenseCustomResource
        {
            Metadata = new K8sObjectMeta
            {
                Name = "cad-floating-license",
                Namespace = "engineering"
            },
            Spec = new AchillesLicenseSpec
            {
                TenantId = "ten_acme",
                ProductId = "cad-enterprise",
                MaxSeats = 50,
                Features = ["modeling-3d", "rendering", "fea-simulation"],
                TargetSecretName = "cad-license-secret",
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(90)
            }
        };

        var result = reconciler.Reconcile(license);

        result.IsSuccess.Should().BeTrue();
        result.Status.Phase.Should().Be("Active");
        result.Status.LicenseId.Should().StartWith("lic_k8s_");
        result.Status.IssuedKeyHash.Should().HaveLength(64); // SHA-256 hex string
        result.Status.SignatureAlg.Should().Be("ES256");
        result.Status.SecretRef.Should().Be("cad-license-secret");
        result.Status.Conditions.Should().ContainSingle(c => c.Type == "Ready" && c.Status == "True" && c.Reason == "LicenseActive");

        result.SecretManifest.Should().NotBeNull();
        string secretYaml = result.SecretManifest!;
        secretYaml.Should().Contain("kind: Secret");
        secretYaml.Should().Contain("name: cad-license-secret");
        secretYaml.Should().Contain("TENANT_ID: \"ten_acme\"");
        secretYaml.Should().Contain("PRODUCT_ID: \"cad-enterprise\"");
        secretYaml.Should().Contain("MAX_SEATS: \"50\"");
        secretYaml.Should().Contain("license.symlic:");

        // Verify that the signed license embedded in the secret is cryptographically valid
        int symlicIdx = secretYaml.IndexOf("license.symlic: ", StringComparison.Ordinal);
        symlicIdx.Should().BeGreaterThan(0);
        int valStart = symlicIdx + "license.symlic: ".Length;
        int valEnd = secretYaml.IndexOf('\n', valStart);
        string b64 = secretYaml[valStart..valEnd].Trim();

        byte[] rawBytes = Convert.FromBase64String(b64);
        string pemArmored = Encoding.UTF8.GetString(rawBytes);

        using var keyRing = new AchillesKeyRing();
        keyRing.Add(signingKey);
        var verifier = new LicenseDocumentVerifier(keyRing);
        var verifyRes = verifier.Verify(pemArmored);

        verifyRes.IsValid.Should().BeTrue();
        verifyRes.Claims.Should().NotBeNull();
        verifyRes.Claims!.Sub.Should().Be("ten_acme");
        verifyRes.Claims.Symlic.Limits.MaxSeats.Should().Be(50);
        verifyRes.Claims.Symlic.Entitlements.Should().HaveCount(3);
    }

    [Fact]
    public void Reconcile_SuspendedLicense_ReturnsSuspendedStatus_And_NoSecret()
    {
        var reconciler = new LicenseReconciler();

        var license = new AchillesLicenseCustomResource
        {
            Metadata = new K8sObjectMeta
            {
                Name = "suspended-license",
                Namespace = "default"
            },
            Spec = new AchillesLicenseSpec
            {
                TenantId = "ten_default",
                ProductId = "test-product",
                MaxSeats = 5,
                Suspended = true
            },
            Status = new SymbolonLicenseStatus
            {
                LicenseId = "lic_k8s_existing123",
                Phase = "Active"
            }
        };

        var result = reconciler.Reconcile(license);

        result.IsSuccess.Should().BeTrue();
        result.Status.Phase.Should().Be("Suspended");
        result.Status.LicenseId.Should().Be("lic_k8s_existing123");
        result.Status.Conditions.Should().ContainSingle(c => c.Type == "Ready" && c.Status == "False" && c.Reason == "LicenseSuspended");
        result.SecretManifest.Should().BeNull();
    }

    [Fact]
    public void Reconcile_NodeLockedLicense_IncludesBindingClaim()
    {
        using var signingKey = Es256SignatureProvider.GenerateKey("operator-ec-key");
        var reconciler = new LicenseReconciler(signingKey);

        var license = new AchillesLicenseCustomResource
        {
            Metadata = new K8sObjectMeta
            {
                Name = "nodelock-license"
            },
            Spec = new AchillesLicenseSpec
            {
                TenantId = "ten_defense",
                ProductId = "edge-controller",
                MaxSeats = 1,
                NodeLockFingerprints = ["e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"]
            }
        };

        var result = reconciler.Reconcile(license);

        result.IsSuccess.Should().BeTrue();
        result.Status.Phase.Should().Be("Active");

        int symlicIdx = result.SecretManifest!.IndexOf("license.symlic: ", StringComparison.Ordinal);
        int valStart = symlicIdx + "license.symlic: ".Length;
        int valEnd = result.SecretManifest.IndexOf('\n', valStart);
        string b64 = result.SecretManifest[valStart..valEnd].Trim();

        string pem = Encoding.UTF8.GetString(Convert.FromBase64String(b64));

        using var keyRing = new AchillesKeyRing();
        keyRing.Add(signingKey);
        var verifier = new LicenseDocumentVerifier(keyRing);
        var verifyRes = verifier.Verify(pem);

        verifyRes.IsValid.Should().BeTrue();
        verifyRes.Claims!.Symlic.Binding.Should().NotBeNull();
        verifyRes.Claims.Symlic.Binding!.Fingerprint.Should().Be("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
    }

    [Fact]
    public void Reconcile_MissingName_ReturnsFailedStatus()
    {
        var reconciler = new LicenseReconciler();

        var license = new AchillesLicenseCustomResource
        {
            Metadata = new K8sObjectMeta
            {
                Name = ""
            },
            Spec = new AchillesLicenseSpec
            {
                TenantId = "ten_fail",
                ProductId = "prod_fail",
                MaxSeats = 10
            }
        };

        var result = reconciler.Reconcile(license);

        result.IsSuccess.Should().BeFalse();
        result.Status.Phase.Should().Be("Failed");
        result.Status.Conditions.Should().ContainSingle(c => c.Type == "Ready" && c.Status == "False" && c.Reason == "InvalidMetadata");
    }
}
