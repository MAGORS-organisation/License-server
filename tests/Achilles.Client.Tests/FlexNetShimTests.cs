using System.Threading.Tasks;
using FluentAssertions;
using Achilles.Client.Native;
using Xunit;

namespace Achilles.Client.Tests;

public sealed class FlexNetShimTests
{
    [Fact]
    public async Task FlexNetShim_CompleteLifecycle_OperatesCorrectly()
    {
        // 1. Initialize FlexNet Job
        int initCode = FlexNetShim.LcInit(out var job, "AUTOCAD-PRO");
        initCode.Should().Be(FlexNetCodes.LM_NOERROR);
        job.Should().NotBeNull();
        job!.VendorId.Should().Be("AUTOCAD-PRO");

        // 2. Checkout feature
        int coCode = await FlexNetShim.LcCheckoutAsync(job, "3D_MODELING_SUITE", version: "2026.1", numLicenses: 1);
        coCode.Should().Be(FlexNetCodes.LM_NOERROR);
        job.Feature.Should().Be("3D_MODELING_SUITE");
        FlexNetShim.LcErrString(job).Should().Contain("No error");

        // 3. Heartbeat
        int hbCode = FlexNetShim.LcHeartbeat(job);
        hbCode.Should().Be(FlexNetCodes.LM_NOERROR);

        // 4. Checkin / Return feature
        int ciCode = FlexNetShim.LcCheckin(job, "3D_MODELING_SUITE");
        ciCode.Should().Be(FlexNetCodes.LM_NOERROR);
        job.Feature.Should().BeNull();

        // 5. Free Job
        FlexNetShim.LcFreeJob(job);
    }

    [Fact]
    public void FlexNetShim_InvalidParams_ReturnsExpectedErrors()
    {
        int errInit = FlexNetShim.LcInit(out var job, string.Empty);
        errInit.Should().Be(FlexNetCodes.LM_BADPARAM);
        job.Should().BeNull();

        var dummyJob = new FlexNetJob("CAD")
        {
            LastErrorCode = FlexNetCodes.LM_MAXUSERS
        };

        string msg = FlexNetShim.LcErrString(dummyJob);
        msg.Should().Contain("Capacity exhausted");
    }
}
