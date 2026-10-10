using System.Security.Cryptography;
using System.Text;
using Achilles.Crypto;
using Achilles.Format;
using Achilles.Operator.Generators;
using Achilles.Protocol.K8s;

namespace Achilles.Operator.Reconcilers;

public sealed record LicenseReconcileResult(
    bool IsSuccess,
    SymbolonLicenseStatus Status,
    string? SecretManifest,
    string? ErrorMessage);

public sealed class LicenseReconciler
{
    private readonly ISignatureProvider _signatureProvider;

    public LicenseReconciler(ISignatureProvider? signatureProvider = null)
    {
        _signatureProvider = signatureProvider ?? Es256SignatureProvider.GenerateKey("operator-key");
    }

    public LicenseReconcileResult Reconcile(AchillesLicenseCustomResource license)
    {
        ArgumentNullException.ThrowIfNull(license);

        if (string.IsNullOrWhiteSpace(license.Metadata.Name))
        {
            var errStatus = new SymbolonLicenseStatus
            {
                Phase = "Failed",
                LastReconciledAt = DateTimeOffset.UtcNow,
                Conditions =
                [
                    new K8sCondition
                    {
                        Type = "Ready",
                        Status = "False",
                        Reason = "InvalidMetadata",
                        Message = "Resource metadata name is missing."
                    }
                ]
            };
            return new LicenseReconcileResult(false, errStatus, null, "Resource metadata name is missing.");
        }

        if (license.Spec.Suspended)
        {
            var suspendedStatus = new SymbolonLicenseStatus
            {
                Phase = "Suspended",
                LicenseId = license.Status?.LicenseId,
                IssuedKeyHash = license.Status?.IssuedKeyHash,
                SignatureAlg = license.Status?.SignatureAlg,
                SecretRef = license.Status?.SecretRef,
                LastReconciledAt = DateTimeOffset.UtcNow,
                Conditions =
                [
                    new K8sCondition
                    {
                        Type = "Ready",
                        Status = "False",
                        Reason = "LicenseSuspended",
                        Message = "License specification marked as suspended."
                    }
                ]
            };
            return new LicenseReconcileResult(true, suspendedStatus, null, null);
        }

        string licenseId = license.Status?.LicenseId ?? $"lic_k8s_{Guid.NewGuid().ToString("N")[..12]}";
        string targetSecret = !string.IsNullOrWhiteSpace(license.Spec.TargetSecretName)
            ? license.Spec.TargetSecretName
            : $"{license.Metadata.Name}-secret";

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        long exp = license.Spec.ExpiresAt.HasValue
            ? license.Spec.ExpiresAt.Value.ToUnixTimeSeconds()
            : now + (365 * 86400);

        BindingClaim? bindingClaim = null;
        if (license.Spec.NodeLockFingerprints != null && license.Spec.NodeLockFingerprints.Count > 0)
        {
            bindingClaim = new BindingClaim
            {
                Fingerprint = license.Spec.NodeLockFingerprints[0],
                Matching = "exact"
            };
        }

        var claims = new LicenseClaims
        {
            Iss = "https://Achilles.Operator.internal",
            Sub = license.Spec.TenantId,
            Aud = "symbolon-client",
            Jti = licenseId,
            Iat = now,
            Exp = exp,
            Symlic = new SymlicClaims
            {
                V = 1,
                Profile = "standard",
                RequiredAlgs = [_signatureProvider.Alg],
                License = new LicenseMetadata
                {
                    Key = licenseId,
                    Model = "floating",
                    State = "active",
                    Customer = new CustomerMetadata
                    {
                        Ref = license.Spec.TenantId,
                        Name = license.Spec.TenantId
                    },
                    ExpiresAt = license.Spec.ExpiresAt?.ToString("O", System.Globalization.CultureInfo.InvariantCulture)
                },
                Limits = new LicenseLimits
                {
                    MaxSeats = license.Spec.MaxSeats,
                    SeatUnit = "process",
                    OverageStrategy = "deny"
                },
                Entitlements = license.Spec.Features?.Select(f => new EntitlementClaim
                {
                    Code = f,
                    Value = 1m
                }).ToList() ?? [],
                Binding = bindingClaim
            }
        };

        var signer = new LicenseDocumentSigner([_signatureProvider]);
        string signedArmored = signer.Sign(claims);

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(signedArmored));
        string hashHex = Convert.ToHexStringLower(hash);

        string secretManifest = K8sManifestGenerator.GenerateLicenseSecretYaml(license, signedArmored);

        var activeStatus = new SymbolonLicenseStatus
        {
            Phase = "Active",
            LicenseId = licenseId,
            IssuedKeyHash = hashHex,
            SignatureAlg = _signatureProvider.Alg,
            SecretRef = targetSecret,
            LastReconciledAt = DateTimeOffset.UtcNow,
            Conditions =
            [
                new K8sCondition
                {
                    Type = "Ready",
                    Status = "True",
                    Reason = "LicenseActive",
                    Message = $"Successfully issued and signed license {licenseId} into secret {targetSecret}."
                }
            ]
        };

        return new LicenseReconcileResult(true, activeStatus, secretManifest, null);
    }
}
