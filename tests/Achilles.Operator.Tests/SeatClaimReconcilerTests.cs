using FluentAssertions;
using Achilles.Operator.Reconcilers;
using Achilles.Protocol.K8s;
using Xunit;

namespace Achilles.Operator.Tests;

public sealed class SeatClaimReconcilerTests
{
    [Fact]
    public void Reconcile_ValidSeatClaim_ReturnsBoundStatusAndConfigMap()
    {
        var claim = new AchillesSeatClaimCustomResource
        {
            Metadata = new K8sObjectMeta
            {
                Name = "cad-workstation-01",
                Namespace = "engineering"
            },
            Spec = new AchillesSeatClaimSpec
            {
                LicenseRef = "lic_prod_catia",
                MachineId = "WS-ENG-4412",
                UserId = "janko.hrasko",
                Department = "AERODYNAMICS",
                Features = ["solid_modeler", "cfd_solver"],
                DurationMinutes = 120
            }
        };

        var result = SeatClaimReconciler.Reconcile(claim);

        result.IsSuccess.Should().BeTrue();
        result.Status.Phase.Should().Be("Bound");
        result.Status.LeaseId.Should().StartWith("lease_claim_");
        result.Status.SeatNo.Should().Be(1);
        result.Status.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);
        result.Status.Conditions.Should().ContainSingle(c => c.Type == "Bound" && c.Status == "True");

        result.Manifests.Should().ContainSingle();
        string manifest = result.Manifests[0];
        manifest.Should().Contain("kind: ConfigMap");
        manifest.Should().Contain("cad-workstation-01-lease");
        manifest.Should().Contain("ACHILLES_LEASE_ID");
        manifest.Should().Contain("ACHILLES_SEAT_NO");
    }

    [Fact]
    public void Reconcile_MissingMetadataName_ReturnsFailed()
    {
        var claim = new AchillesSeatClaimCustomResource
        {
            Metadata = new K8sObjectMeta { Name = "" },
            Spec = new AchillesSeatClaimSpec
            {
                LicenseRef = "lic_1",
                MachineId = "pc-1"
            }
        };

        var result = SeatClaimReconciler.Reconcile(claim);

        result.IsSuccess.Should().BeFalse();
        result.Status.Phase.Should().Be("Failed");
        result.Status.Conditions.Should().ContainSingle(c => c.Type == "Ready" && c.Status == "False");
    }

    [Fact]
    public void Reconcile_MissingLicenseRefOrMachineId_ReturnsFailed()
    {
        var claim = new AchillesSeatClaimCustomResource
        {
            Metadata = new K8sObjectMeta { Name = "claim-without-license" },
            Spec = new AchillesSeatClaimSpec
            {
                LicenseRef = "",
                MachineId = "pc-1"
            }
        };

        var result = SeatClaimReconciler.Reconcile(claim);

        result.IsSuccess.Should().BeFalse();
        result.Status.Phase.Should().Be("Failed");
    }
}
