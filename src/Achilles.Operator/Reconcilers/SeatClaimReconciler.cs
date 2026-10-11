using Achilles.Protocol.K8s;

namespace Achilles.Operator.Reconcilers;

public sealed record SeatClaimReconcileResult(
    bool IsSuccess,
    AchillesSeatClaimStatus Status,
    IReadOnlyList<string> Manifests,
    string? ErrorMessage);

public static class SeatClaimReconciler
{
    public static SeatClaimReconcileResult Reconcile(AchillesSeatClaimCustomResource claim)
    {
        ArgumentNullException.ThrowIfNull(claim);

        if (string.IsNullOrWhiteSpace(claim.Metadata.Name))
        {
            var errStatus = new AchillesSeatClaimStatus
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
            return new SeatClaimReconcileResult(false, errStatus, [], "Resource metadata name is missing.");
        }

        if (string.IsNullOrWhiteSpace(claim.Spec.LicenseRef) || string.IsNullOrWhiteSpace(claim.Spec.MachineId))
        {
            var errStatus = new AchillesSeatClaimStatus
            {
                Phase = "Failed",
                LastReconciledAt = DateTimeOffset.UtcNow,
                Conditions =
                [
                    new K8sCondition
                    {
                        Type = "Ready",
                        Status = "False",
                        Reason = "InvalidSpec",
                        Message = "Both LicenseRef and MachineId are mandatory for a seat claim."
                    }
                ]
            };
            return new SeatClaimReconcileResult(false, errStatus, [], "LicenseRef and MachineId are required.");
        }

        string leaseId = claim.Status?.LeaseId ?? $"lease_claim_{Guid.NewGuid().ToString("N")[..12]}";
        int seatNo = claim.Status?.SeatNo ?? 1;
        DateTimeOffset expiresAt = claim.Status?.ExpiresAt ?? DateTimeOffset.UtcNow.AddMinutes(Math.Max(5, claim.Spec.DurationMinutes));

        // Generate lease secret / configmap for consuming workloads
        string ns = string.IsNullOrWhiteSpace(claim.Metadata.Namespace) ? "default" : claim.Metadata.Namespace;
        string configMapYaml = $"""
            apiVersion: v1
            kind: ConfigMap
            metadata:
              name: {claim.Metadata.Name}-lease
              namespace: {ns}
              labels:
                app.kubernetes.io/managed-by: achilles-operator
                achilles.io/license-ref: {claim.Spec.LicenseRef}
                achilles.io/machine-id: {claim.Spec.MachineId}
            data:
              ACHILLES_LEASE_ID: "{leaseId}"
              ACHILLES_SEAT_NO: "{seatNo}"
              ACHILLES_EXPIRES_AT: "{expiresAt:O}"
            """;

        var boundStatus = new AchillesSeatClaimStatus
        {
            Phase = "Bound",
            LeaseId = leaseId,
            SeatNo = seatNo,
            ExpiresAt = expiresAt,
            LastReconciledAt = DateTimeOffset.UtcNow,
            Conditions =
            [
                new K8sCondition
                {
                    Type = "Bound",
                    Status = "True",
                    Reason = "SeatAllocated",
                    Message = $"Seat #{seatNo} successfully bound under lease {leaseId}."
                }
            ]
        };

        return new SeatClaimReconcileResult(true, boundStatus, [configMapYaml], null);
    }
}
