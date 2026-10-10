using System.IO;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Symbolon.Domain.Tests.Observability;

public sealed class GrafanaAndAlertmanagerTests
{
    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "Symbolon.slnx")) || Directory.Exists(Path.Combine(dir, "deploy")))
            {
                return dir;
            }
            dir = Directory.GetParent(dir)?.FullName;
        }
        return Path.GetFullPath("../../../../..");
    }

    [Fact]
    public void ConcurrencyAIOps_Dashboard_IsValidJson_AndContainsExpectedPanels()
    {
        string root = FindRepoRoot();
        string path = Path.Combine(root, "deploy", "grafana", "dashboards", "symbolon-concurrency-aiops.json");
        File.Exists(path).Should().BeTrue("Dashboard file must exist");

        string json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        var rootElement = doc.RootElement;

        rootElement.GetProperty("title").GetString().Should().Contain("Concurrency");
        rootElement.GetProperty("uid").GetString().Should().Be("symbolon-concurrency-aiops");

        var panels = rootElement.GetProperty("panels");
        panels.GetArrayLength().Should().BeGreaterThanOrEqualTo(4);

        // Verify key PromQL targets exist
        json.Should().Contain("symbolon_concurrency_forecast_seats");
        json.Should().Contain("symbolon_time_to_exhaustion_minutes");
        json.Should().Contain("symbolon_active_seats");
    }

    [Fact]
    public void PqcHsmHealth_Dashboard_IsValidJson_AndContainsExpectedPanels()
    {
        string root = FindRepoRoot();
        string path = Path.Combine(root, "deploy", "grafana", "dashboards", "symbolon-pqc-hsm-health.json");
        File.Exists(path).Should().BeTrue("Dashboard file must exist");

        string json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        var rootElement = doc.RootElement;

        rootElement.GetProperty("title").GetString().Should().Contain("Post-Quantum");
        rootElement.GetProperty("uid").GetString().Should().Be("symbolon-pqc-hsm-health");

        json.Should().Contain("symbolon_pqc_signature_ratio");
        json.Should().Contain("symbolon_hsm_healthy");
        json.Should().Contain("symbolon_hsm_entropy_quality_ratio");
    }

    [Fact]
    public void MerkleEbpfSecurity_Dashboard_IsValidJson_AndContainsExpectedPanels()
    {
        string root = FindRepoRoot();
        string path = Path.Combine(root, "deploy", "grafana", "dashboards", "symbolon-merkle-ebpf-security.json");
        File.Exists(path).Should().BeTrue("Dashboard file must exist");

        string json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        var rootElement = doc.RootElement;

        rootElement.GetProperty("title").GetString().Should().Contain("Merkle");
        rootElement.GetProperty("uid").GetString().Should().Be("symbolon-merkle-ebpf-security");

        json.Should().Contain("symbolon_merkle_tree_size");
        json.Should().Contain("symbolon_ebpf_socket_connections_intercepted_total");
    }

    [Fact]
    public void AlertmanagerPack_AndAlertRules_AreConfiguredProperly()
    {
        string root = FindRepoRoot();
        string alertmanagerPath = Path.Combine(root, "deploy", "alertmanager", "alertmanager.yml");
        File.Exists(alertmanagerPath).Should().BeTrue("Alertmanager config must exist");

        string amYaml = File.ReadAllText(alertmanagerPath);
        amYaml.Should().Contain("slack-licensing");
        amYaml.Should().Contain("pagerduty-critical");
        amYaml.Should().Contain("slack-security-soc");
        amYaml.Should().Contain("inhibit_rules");

        string alertsPath = Path.Combine(root, "deploy", "alertmanager", "symbolon-alerts.yml");
        File.Exists(alertsPath).Should().BeTrue("Prometheus alerts rule pack must exist");

        string rulesYaml = File.ReadAllText(alertsPath);
        rulesYaml.Should().Contain("SymbolonSeatSaturationCritical");
        rulesYaml.Should().Contain("SymbolonAIOpsPredictedExhaustion");
        rulesYaml.Should().Contain("SymbolonMerkleDriftAlert");
        rulesYaml.Should().Contain("SymbolonHsmHardwareDegradation");
        rulesYaml.Should().Contain("SymbolonEbpfEnforcementSpike");
    }
}
