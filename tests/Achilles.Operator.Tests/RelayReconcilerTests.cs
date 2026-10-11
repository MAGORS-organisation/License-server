using FluentAssertions;
using Achilles.Operator.Reconcilers;
using Achilles.Protocol.K8s;
using Xunit;

namespace Achilles.Operator.Tests;

public sealed class RelayReconcilerTests
{
    [Fact]
    public void Reconcile_ValidRelay_ReturnsRunningStatusAndDeploymentService()
    {
        var relay = new AchillesRelayCustomResource
        {
            Metadata = new K8sObjectMeta
            {
                Name = "bratislava-edge-relay",
                Namespace = "licensing-edge"
            },
            Spec = new AchillesRelaySpec
            {
                UpstreamServer = "https://controlplane.achilles.internal:8080",
                LocalSeats = 150,
                EdgeClusterId = "edge-ba-01",
                Replicas = 2
            }
        };

        var result = RelayReconciler.Reconcile(relay);

        result.IsSuccess.Should().BeTrue();
        result.Status.Phase.Should().Be("Running");
        result.Status.UpstreamConnected.Should().BeTrue();
        result.Status.Conditions.Should().ContainSingle(c => c.Type == "Ready" && c.Status == "True");

        result.Manifests.Should().HaveCount(2); // Deployment and Service

        string deployment = result.Manifests[0];
        deployment.Should().Contain("kind: Deployment");
        deployment.Should().Contain("replicas: 2");
        deployment.Should().Contain("UPSTREAM_SERVER_URL");
        deployment.Should().Contain("LOCAL_SEAT_POOL");
        deployment.Should().Contain("150");

        string service = result.Manifests[1];
        service.Should().Contain("kind: Service");
        service.Should().Contain("port: 8081");
    }

    [Fact]
    public void Reconcile_MissingMetadataName_ReturnsFailed()
    {
        var relay = new AchillesRelayCustomResource
        {
            Metadata = new K8sObjectMeta { Name = "" },
            Spec = new AchillesRelaySpec
            {
                UpstreamServer = "https://controlplane:8080"
            }
        };

        var result = RelayReconciler.Reconcile(relay);

        result.IsSuccess.Should().BeFalse();
        result.Status.Phase.Should().Be("Failed");
    }

    [Fact]
    public void Reconcile_MissingUpstreamServer_ReturnsFailed()
    {
        var relay = new AchillesRelayCustomResource
        {
            Metadata = new K8sObjectMeta { Name = "relay-no-upstream" },
            Spec = new AchillesRelaySpec
            {
                UpstreamServer = ""
            }
        };

        var result = RelayReconciler.Reconcile(relay);

        result.IsSuccess.Should().BeFalse();
        result.Status.Phase.Should().Be("Failed");
        result.ErrorMessage.Should().Contain("UpstreamServer");
    }
}
