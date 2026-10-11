using Achilles.Protocol.K8s;

namespace Achilles.Operator.Reconcilers;

public sealed record RelayReconcileResult(
    bool IsSuccess,
    AchillesRelayStatus Status,
    IReadOnlyList<string> Manifests,
    string? ErrorMessage);

public static class RelayReconciler
{
    public static RelayReconcileResult Reconcile(AchillesRelayCustomResource relay)
    {
        ArgumentNullException.ThrowIfNull(relay);

        if (string.IsNullOrWhiteSpace(relay.Metadata.Name))
        {
            var errStatus = new AchillesRelayStatus
            {
                Phase = "Failed",
                LastSyncedAt = DateTimeOffset.UtcNow,
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
            return new RelayReconcileResult(false, errStatus, [], "Resource metadata name is missing.");
        }

        if (string.IsNullOrWhiteSpace(relay.Spec.UpstreamServer))
        {
            var errStatus = new AchillesRelayStatus
            {
                Phase = "Failed",
                LastSyncedAt = DateTimeOffset.UtcNow,
                Conditions =
                [
                    new K8sCondition
                    {
                        Type = "Ready",
                        Status = "False",
                        Reason = "MissingUpstreamServer",
                        Message = "Spec.UpstreamServer is required to sync seats with parent cluster."
                    }
                ]
            };
            return new RelayReconcileResult(false, errStatus, [], "UpstreamServer is required.");
        }

        string ns = string.IsNullOrWhiteSpace(relay.Metadata.Namespace) ? "default" : relay.Metadata.Namespace;
        string name = relay.Metadata.Name;

        string deploymentYaml = $"""
            apiVersion: apps/v1
            kind: Deployment
            metadata:
              name: {name}
              namespace: {ns}
              labels:
                app.kubernetes.io/name: achilles-relay
                app.kubernetes.io/instance: {name}
                app.kubernetes.io/managed-by: achilles-operator
            spec:
              replicas: {Math.Max(1, relay.Spec.Replicas)}
              selector:
                matchLabels:
                  app.kubernetes.io/name: achilles-relay
                  app.kubernetes.io/instance: {name}
              template:
                metadata:
                  labels:
                    app.kubernetes.io/name: achilles-relay
                    app.kubernetes.io/instance: {name}
                spec:
                  containers:
                  - name: relay
                    image: ghcr.io/magors-organisation/achilles-relay:latest
                    imagePullPolicy: IfNotPresent
                    env:
                    - name: UPSTREAM_SERVER_URL
                      value: "{relay.Spec.UpstreamServer}"
                    - name: LOCAL_SEAT_POOL
                      value: "{relay.Spec.LocalSeats}"
                    - name: EDGE_CLUSTER_ID
                      value: "{relay.Spec.EdgeClusterId}"
                    ports:
                    - name: http
                      containerPort: 8081
                      protocol: TCP
                    livenessProbe:
                      httpGet:
                        path: /health/live
                        port: 8081
                      initialDelaySeconds: 5
                      periodSeconds: 10
                    readinessProbe:
                      httpGet:
                        path: /health/ready
                        port: 8081
                      initialDelaySeconds: 3
                      periodSeconds: 5
            """;

        string serviceYaml = $"""
            apiVersion: v1
            kind: Service
            metadata:
              name: {name}
              namespace: {ns}
              labels:
                app.kubernetes.io/name: achilles-relay
                app.kubernetes.io/instance: {name}
            spec:
              type: ClusterIP
              ports:
              - name: http
                port: 8081
                targetPort: 8081
              selector:
                app.kubernetes.io/name: achilles-relay
                app.kubernetes.io/instance: {name}
            """;

        var runningStatus = new AchillesRelayStatus
        {
            Phase = "Running",
            AllocatedSeats = 0,
            UpstreamConnected = true,
            LastSyncedAt = DateTimeOffset.UtcNow,
            Conditions =
            [
                new K8sCondition
                {
                    Type = "Ready",
                    Status = "True",
                    Reason = "RelayRunning",
                    Message = $"Achilles Relay '{name}' successfully provisioned with {relay.Spec.LocalSeats} edge seats."
                }
            ]
        };

        return new RelayReconcileResult(true, runningStatus, [deploymentYaml, serviceYaml], null);
    }
}
