using Achilles.Operator.Generators;
using Achilles.Protocol.K8s;

namespace Achilles.Operator.Reconcilers;

public sealed record ClusterReconcileResult(
    bool IsSuccess,
    SymbolonClusterStatus Status,
    IReadOnlyList<string> Manifests,
    string? ErrorMessage);

public static class ClusterReconciler
{
    public static ClusterReconcileResult Reconcile(AchillesClusterCustomResource cluster)
    {
        ArgumentNullException.ThrowIfNull(cluster);

        if (string.IsNullOrWhiteSpace(cluster.Metadata.Name))
        {
            var errStatus = new SymbolonClusterStatus
            {
                Phase = "Failed",
                ReadyReplicas = 0,
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
            return new ClusterReconcileResult(false, errStatus, [], "Resource metadata name is missing.");
        }

        if (cluster.Spec.Replicas < 1)
        {
            var degradedStatus = new SymbolonClusterStatus
            {
                Phase = "Degraded",
                ReadyReplicas = 0,
                LastReconciledAt = DateTimeOffset.UtcNow,
                Conditions =
                [
                    new K8sCondition
                    {
                        Type = "Ready",
                        Status = "False",
                        Reason = "ZeroReplicas",
                        Message = "Spec replicas must be at least 1."
                    }
                ]
            };
            return new ClusterReconcileResult(false, degradedStatus, [], "Spec replicas must be at least 1.");
        }

        var manifests = new List<string>
        {
            K8sManifestGenerator.GenerateDeploymentYaml(cluster),
            K8sManifestGenerator.GenerateServiceYaml(cluster),
            K8sManifestGenerator.GeneratePdbYaml(cluster)
        };

        string? ingressYaml = K8sManifestGenerator.GenerateIngressYaml(cluster);
        if (!string.IsNullOrWhiteSpace(ingressYaml))
        {
            manifests.Add(ingressYaml);
        }

        if (cluster.Spec.Monitoring?.ServiceMonitor == true)
        {
            manifests.Add(K8sManifestGenerator.GenerateServiceMonitorYaml(cluster));
        }

        string activeKid = cluster.Status?.ActiveKid ?? $"kid_{Guid.NewGuid().ToString("N")[..8]}";

        var successStatus = new SymbolonClusterStatus
        {
            Phase = "Ready",
            ReadyReplicas = cluster.Spec.Replicas,
            ActiveKid = activeKid,
            ObservedGeneration = cluster.Metadata.Generation ?? 1,
            LastReconciledAt = DateTimeOffset.UtcNow,
            Conditions =
            [
                new K8sCondition
                {
                    Type = "Ready",
                    Status = "True",
                    Reason = "ClusterReconciled",
                    Message = $"Successfully reconciled {manifests.Count} cluster child resources."
                }
            ]
        };

        return new ClusterReconcileResult(true, successStatus, manifests, null);
    }
}
