using Symbolon.Crypto;

namespace Symbolon.Domain.Pqc;

/// <summary>
/// Quantum Vulnerability & Readiness Scanner.
/// Audits active cryptographic keys, license documents, and transport profiles.
/// Computes PQC Readiness Index (0..100%) and verifies CNSA 2.0 and EU NIS 2 compliance.
/// </summary>
public static class PqcReadinessScanner
{
    public static PqcReadinessReport Scan(
        IReadOnlyList<JsonWebKeyDto> keys,
        IReadOnlyList<(string Id, string Customer, string? Alg, DateTimeOffset? ExpiresAt)> licenses,
        string activeProfile = PqcProfiles.HybridV1)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(licenses);

        var keyAudits = new List<PqcKeyAuditItem>();
        int pqcKeys = 0;
        int classicalKeys = 0;

        foreach (var k in keys)
        {
            bool isPqc = Alg.IsPostQuantum(k.Alg);
            bool isCnsa = PqcProfileValidator.IsCnsa2Compliant(k.Alg);
            var category = isPqc
                ? PqcAlgorithmCategory.PostQuantum
                : (string.Equals(activeProfile, PqcProfiles.HybridV1, StringComparison.OrdinalIgnoreCase) ? PqcAlgorithmCategory.Hybrid : PqcAlgorithmCategory.Classical);

            if (isPqc) pqcKeys++;
            else classicalKeys++;

            string recommendation = isPqc
                ? (isCnsa ? "Plne v súlade s FIPS 203/204 a CNSA 2.0. Nevyžaduje akciu." : "Post-kvantový algoritmus, odporúča sa overiť CNSA 2.0 profil.")
                : "Klasický kryptografický kľúč (zraniteľný voči Shorovmu algoritmu). Naplánujte rotáciu na ML-DSA-65 alebo ML-KEM-768.";

            keyAudits.Add(new PqcKeyAuditItem
            {
                KeyId = k.Kid,
                Algorithm = k.Alg,
                Category = category,
                KeyUsage = k.Use ?? "sig",
                IsQuantumSafe = isPqc,
                IsCnsa2Compliant = isCnsa,
                Recommendation = recommendation
            });
        }

        var licenseAudits = new List<PqcLicenseAuditItem>();
        int pqcLicenses = 0;
        int atRiskLicenses = 0;

        foreach (var (id, customer, alg, expiresAt) in licenses)
        {
            string signatureAlg = alg ?? Alg.Es256;
            bool isPqc = Alg.IsPostQuantum(signatureAlg);

            PqcRiskLevel riskLevel;
            string recommendation;

            if (isPqc)
            {
                pqcLicenses++;
                riskLevel = PqcRiskLevel.None;
                recommendation = "Licencia podpísaná post-kvantovým algoritmom (ML-DSA-65). Bezpečná voči kvantovým počítačom.";
            }
            else
            {
                atRiskLicenses++;
                // Check if perpetual or long-lived (expires after 2030 or null)
                bool isLongLived = !expiresAt.HasValue || expiresAt.Value.Year >= 2030;
                if (isLongLived)
                {
                    riskLevel = PqcRiskLevel.High;
                    recommendation = "Dlhodobá / trvalá licencia s klasickým podpisom. Vysoké riziko Harvest-Now-Decrypt-Later (HNDL). Prepodpíšte cez ML-DSA-65.";
                }
                else
                {
                    riskLevel = PqcRiskLevel.Low;
                    recommendation = "Krátkodobá licencia vyprší pred očakávaným príchodom Q-Day (~2030). Nízke bezprostredné riziko.";
                }
            }

            licenseAudits.Add(new PqcLicenseAuditItem
            {
                LicenseId = id,
                Customer = customer,
                SignatureAlgorithm = signatureAlg,
                IsQuantumSafe = isPqc,
                RiskLevel = riskLevel,
                ExpiresAt = expiresAt,
                Recommendation = recommendation
            });
        }

        // Calculate weighted readiness score
        // Keys: 60% weight, Licenses: 30% weight, Profile: 10% weight
        double keyScore = keys.Count > 0 ? ((double)pqcKeys / keys.Count) * 60.0 : 30.0;
        double licScore = licenses.Count > 0 ? ((double)pqcLicenses / licenses.Count) * 30.0 : 15.0;
        double profileScore = string.Equals(activeProfile, PqcProfiles.PqcStrict, StringComparison.OrdinalIgnoreCase) ? 10.0 : 5.0;

        double totalScore = Math.Round(Math.Clamp(keyScore + licScore + profileScore, 0.0, 100.0), 1);

        bool isCnsaReady = string.Equals(activeProfile, PqcProfiles.PqcStrict, StringComparison.OrdinalIgnoreCase)
            && classicalKeys == 0
            && keyAudits.All(k => k.IsCnsa2Compliant);

        bool isNis2Ready = totalScore >= 70.0;

        var actionItems = new List<string>();
        if (classicalKeys > 0)
        {
            actionItems.Add($"Vygenerujte nové ML-DSA-65 a ML-KEM-768 kľúče pre vyradenie {classicalKeys} klasických kľúčov.");
        }
        if (atRiskLicenses > 0)
        {
            int highRisk = licenseAudits.Count(l => l.RiskLevel == PqcRiskLevel.High);
            if (highRisk > 0)
            {
                actionItems.Add($"Prepodpíšte {highRisk} dlhodobých licencií s vysokým rizikom kvantového falšovania.");
            }
        }
        if (!string.Equals(activeProfile, PqcProfiles.PqcStrict, StringComparison.OrdinalIgnoreCase))
        {
            actionItems.Add("Po rotácii všetkých klientskych SDK prepnite profil servera na 'pqc-strict' (Zero Classical Cryptography).");
        }

        string summary = totalScore switch
        {
            >= 95.0 => "EXCELENTNÁ: Infraštruktúra je 100% pripravená na post-kvantovú éru (PQC Strict / FIPS 203 & 204).",
            >= 70.0 => "DOBRÁ: Infraštruktúra používa hybridný režim (hybrid-v1). Odporúča sa dokončiť migráciu na čisté PQC.",
            >= 40.0 => "ČIASTOČNÁ: Detegované zraniteľné klasické komponenty. Odporúča sa začať kľúčovú rotáciu podľa NIS 2.",
            _ => "KRITICKÁ: Celá infraštruktúra stojí na klasických algoritmoch zraniteľných voči kvantovým počítačom."
        };

        return new PqcReadinessReport
        {
            ScannedAt = DateTimeOffset.UtcNow,
            ActiveProfile = activeProfile,
            ReadinessScorePercent = totalScore,
            TotalKeysScanned = keys.Count,
            QuantumSafeKeys = pqcKeys,
            ClassicalKeys = classicalKeys,
            TotalLicensesScanned = licenses.Count,
            QuantumSafeLicenses = pqcLicenses,
            AtRiskLicenses = atRiskLicenses,
            IsCnsa2Ready = isCnsaReady,
            IsNis2Ready = isNis2Ready,
            KeyAudits = keyAudits,
            LicenseAudits = licenseAudits,
            ActionItems = actionItems,
            Summary = summary
        };
    }
}
