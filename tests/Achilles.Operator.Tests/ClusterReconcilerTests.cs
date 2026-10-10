using FluentAssertions;
using Achilles.Operator.Reconcilers;
using Achilles.Protocol.K8s;
using Xunit;

namespace Achilles.Operator.Tests;

public sealed class ClusterReconcilerTests
{
    [Fact]
    public void Reconcile_ValidCluster_GeneratesDeployment_Service_Pdb_And_StatusReady()
    {
        var cluster = new AchillesClusterCustomResource
        {
            Metadata = new K8sObjectMeta
            {
                Name = "symbolon-ha-prod",
                Namespace = "licensing"
            },
            Spec = new AchillesClusterSpec
            {
                Replicas = 3,
                PqcEnabled = true,
                Database = new DatabaseSpec
                {
                    Host = "postgres-ha.database.svc",
                    Port = 5432,
                    Name = "symbolon_db",
                    Username = "sym_user",
                    Password = "super_secret_db_password"
                },
                Crypto = new CryptoSpec
                {
                    KeyAlg = "Hybrid",
                    AutoGenerate = true
                }
            }
        };

        var result = ClusterReconciler.Reconcile(cluster);

        result.IsSuccess.Should().BeTrue();
        result.Status.Phase.Should().Be("Ready");
        result.Status.ReadyReplicas.Should().Be(3);
        result.Status.ActiveKid.Should().NotBeNullOrWhiteSpace();
        result.Status.Conditions.Should().ContainSingle(c => c.Type == "Ready" && c.Status == "True");

        result.Manifests.Should().HaveCount(3); // Deployment, Service, PDB

        string deployment = result.Manifests.First(m => m.Contains("kind: Deployment", StringComparison.Ordinal));
        deployment.Should().Contain("replicas: 3");
        deployment.Should().Contain("runAsNonRoot: true");
        deployment.Should().Contain("readOnlyRootFilesystem: true");
        deployment.Should().Contain("podAntiAffinity:");
        deployment.Should().Contain("/health/live");
        deployment.Should().Contain("/health/ready");
        deployment.Should().Contain("Symbolon__PqcEnabled");
        deployment.Should().Contain("true");

        string service = result.Manifests.First(m => m.Contains("kind: Service", StringComparison.Ordinal));
        service.Should().Contain("type: ClusterIP");
        service.Should().Contain("port: 8080");

        string pdb = result.Manifests.First(m => m.Contains("kind: PodDisruptionBudget", StringComparison.Ordinal));
        pdb.Should().Contain("minAvailable: 1");
        pdb.Should().Contain("name: symbolon-ha-prod-pdb");
    }

    [Fact]
    public void Reconcile_WithIngressAndMonitoring_GeneratesIngressAndServiceMonitor()
    {
        var cluster = new AchillesClusterCustomResource
        {
            Metadata = new K8sObjectMeta
            {
                Name = "symbolon-web",
                Namespace = "default"
            },
            Spec = new AchillesClusterSpec
            {
                Replicas = 2,
                Ingress = new IngressSpec
                {
                    Enabled = true,
                    ClassName = "traefik",
                    Host = "license.enterprise.corp",
                    Tls = true,
                    CertManagerIssuer = "letsencrypt-production",
                    Annotations = new Dictionary<string, string>
                    {
                        ["traefik.ingress.kubernetes.io/router.entrypoints"] = "websecure"
                    }
                },
                Monitoring = new MonitoringSpec
                {
                    ServiceMonitor = true,
                    PrometheusScrape = true,
                    MetricsPort = 8080
                }
            }
        };

        var result = ClusterReconciler.Reconcile(cluster);

        result.IsSuccess.Should().BeTrue();
        result.Manifests.Should().HaveCount(5); // Deployment, Service, PDB, Ingress, ServiceMonitor

        string ingress = result.Manifests.First(m => m.Contains("kind: Ingress", StringComparison.Ordinal));
        ingress.Should().Contain("license.enterprise.corp");
        ingress.Should().Contain("ingressClassName: traefik");
        ingress.Should().Contain("cert-manager.io/cluster-issuer: letsencrypt-production");
        ingress.Should().Contain("traefik.ingress.kubernetes.io/router.entrypoints");

        string monitor = result.Manifests.First(m => m.Contains("kind: ServiceMonitor", StringComparison.Ordinal));
        monitor.Should().Contain("name: symbolon-web-monitor");
        monitor.Should().Contain("path: /metrics");
    }

    [Fact]
    public void Reconcile_InvalidReplicas_ReturnsDegradedStatus()
    {
        var cluster = new AchillesClusterCustomResource
        {
            Metadata = new K8sObjectMeta
            {
                Name = "bad-cluster"
            },
            Spec = new AchillesClusterSpec
            {
                Replicas = 0
            }
        };

        var result = ClusterReconciler.Reconcile(cluster);

        result.IsSuccess.Should().BeFalse();
        result.Status.Phase.Should().Be("Degraded");
        result.Status.Conditions.Should().ContainSingle(c => c.Type == "Ready" && c.Status == "False" && c.Reason == "ZeroReplicas");
    }

    [Fact]
    public void Reconcile_MissingName_ReturnsFailedStatus()
    {
        var cluster = new AchillesClusterCustomResource
        {
            Metadata = new K8sObjectMeta
            {
                Name = ""
            },
            Spec = new AchillesClusterSpec
            {
                Replicas = 2
            }
        };

        var result = ClusterReconciler.Reconcile(cluster);

        result.IsSuccess.Should().BeFalse();
        result.Status.Phase.Should().Be("Failed");
        result.Status.Conditions.Should().ContainSingle(c => c.Type == "Ready" && c.Status == "False" && c.Reason == "InvalidMetadata");
    }
}
