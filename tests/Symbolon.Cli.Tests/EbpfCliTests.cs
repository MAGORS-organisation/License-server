using FluentAssertions;
using Xunit;

namespace Symbolon.Cli.Tests;

public sealed class EbpfCliTests
{
    [Fact]
    public async Task Ebpf_Status_Command_Succeeds()
    {
        int exitCode = await Program.Main(["ebpf", "status", "--cgroup", "/sys/fs/cgroup/workload-1"]);
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task Ebpf_Attach_Command_Succeeds()
    {
        int exitCode = await Program.Main([
            "ebpf", "attach",
            "--cgroup", "/sys/fs/cgroup/docker/container_123",
            "--ports", "443,8080,8443"
        ]);
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task Ebpf_Violations_Command_Succeeds()
    {
        int exitCode = await Program.Main(["ebpf", "violations"]);
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task Ebpf_Help_Command_Succeeds()
    {
        int exitCode = await Program.Main(["ebpf", "--help"]);
        exitCode.Should().Be(0);
    }
}
