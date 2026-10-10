using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Achilles.ControlPlane.Endpoints;
using Achilles.Protocol.Enforcement;
using Xunit;

namespace Achilles.ControlPlane.Tests;

public class EbpfApiTests : IClassFixture<ControlPlaneFactory>
{
    private readonly HttpClient _client;

    public EbpfApiTests(ControlPlaneFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetEbpfStatus_Returns200_WithDriverAndMetrics()
    {
        var response = await _client.GetAsync(new Uri("/v1/system/ebpf/status", UriKind.Relative));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var status = await response.Content.ReadFromJsonAsync<EbpfEnforcementStatus>();
        status.Should().NotBeNull();
        status!.DriverMode.Should().NotBeNullOrWhiteSpace();
        status.AttachedCgroups.Should().NotBeNull();
    }

    [Fact]
    public async Task AttachAndDetachCgroup_Succeeds()
    {
        string cgroupPath = $"/sys/fs/cgroup/test_{Guid.NewGuid():N}";

        // 1. Attach
        var attachReq = new AttachCgroupRequest(cgroupPath, [443, 8080]);
        var attachRes = await _client.PostAsJsonAsync(new Uri("/v1/system/ebpf/attach", UriKind.Relative), attachReq);
        attachRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. Status reflects attachment
        var statusRes = await _client.GetAsync(new Uri("/v1/system/ebpf/status", UriKind.Relative));
        var status = await statusRes.Content.ReadFromJsonAsync<EbpfEnforcementStatus>();
        status!.AttachedCgroups.Should().Contain(cgroupPath);

        // 3. Detach
        var detachReq = new DetachCgroupRequest(cgroupPath);
        var detachRes = await _client.PostAsJsonAsync(new Uri("/v1/system/ebpf/detach", UriKind.Relative), detachReq);
        detachRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Status reflects detachment
        var postDetachRes = await _client.GetAsync(new Uri("/v1/system/ebpf/status", UriKind.Relative));
        var postStatus = await postDetachRes.Content.ReadFromJsonAsync<EbpfEnforcementStatus>();
        postStatus!.AttachedCgroups.Should().NotContain(cgroupPath);
    }

    [Fact]
    public async Task GetEbpfViolations_Returns200_List()
    {
        var response = await _client.GetAsync(new Uri("/v1/system/ebpf/violations?limit=10", UriKind.Relative));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var violations = await response.Content.ReadFromJsonAsync<List<EbpfViolationEvent>>();
        violations.Should().NotBeNull();
    }
}
