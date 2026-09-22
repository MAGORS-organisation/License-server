using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Symbolon.ControlPlane.Security;
using Symbolon.Crypto.Hierarchy;
using Xunit;

namespace Symbolon.ControlPlane.Tests;

public sealed class KmsAndHierarchyApiTests : IClassFixture<ControlPlaneFactory>
{
    private readonly HttpClient _client;
    private const string AdminApiKey = "sym_adm_test_secret_key_12345";

    public KmsAndHierarchyApiTests(ControlPlaneFactory factory)
    {
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Add("X-Api-Key", AdminApiKey);
    }

    [Fact]
    public async Task Kms_Status_Endpoint_Returns_Healthy_Status()
    {
        var response = await _client.GetAsync("/admin/v1/kms/status");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = doc.RootElement;

        root.GetProperty("isHealthy").GetBoolean().Should().BeTrue();
        root.GetProperty("providerType").GetString().Should().NotBeNullOrWhiteSpace();
        root.GetProperty("keysCount").GetInt32().Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task KeyHierarchy_Endpoint_Returns_Valid_3Tier_Chain()
    {
        var response = await _client.GetAsync("/admin/v1/keys/hierarchy");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var chain = await response.Content.ReadFromJsonAsync<KeyHierarchyChain>();
        chain.Should().NotBeNull();
        chain!.RootCertificate.Should().NotBeNull();
        chain.ProductCertificate.Should().NotBeNull();
        chain.LeaseCertificate.Should().NotBeNull();

        chain.RootCertificate.SubjectRole.Should().Be(KeyTierRole.Root);
        chain.ProductCertificate.SubjectRole.Should().Be(KeyTierRole.Product);
        chain.LeaseCertificate!.SubjectRole.Should().Be(KeyTierRole.Lease);
    }

    [Fact]
    public async Task KeyHierarchy_Verify_Endpoint_Verifies_Chain_Integrity()
    {
        var response = await _client.PostAsJsonAsync("/admin/v1/keys/hierarchy/verify", (KeyHierarchyChain?)null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<KeyChainVerificationResult>();
        result.Should().NotBeNull();
        result!.IsValid.Should().BeTrue();
        result.FailureReason.Should().BeNull();
        result.RootKid.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ProductionKeySafetyGuard_Rejects_Plaintext_In_Production()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SYMBOLON_KEY_STORAGE"] = "plaintext"
            })
            .Build();

        var env = new TestHostEnvironment { EnvironmentName = Environments.Production };

        var act = () => ProductionKeySafetyGuard.EnforceSafetyRules(config, env);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*CRITICAL SECURITY VIOLATION [§9.3 Rule 4]*");
    }

    [Fact]
    public void ProductionKeySafetyGuard_Allows_Plaintext_In_Development()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SYMBOLON_KEY_STORAGE"] = "plaintext"
            })
            .Build();

        var env = new TestHostEnvironment { EnvironmentName = Environments.Development };

        var act = () => ProductionKeySafetyGuard.EnforceSafetyRules(config, env);
        act.Should().NotThrow();
    }

    [Fact]
    public void ProductionKeySafetyGuard_Rejects_Trivial_Passphrase_In_Production()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SYMBOLON_KEY_STORAGE"] = "envelope",
                ["SYMBOLON_MASTER_KEY_PASSPHRASE"] = "password"
            })
            .Build();

        var env = new TestHostEnvironment { EnvironmentName = Environments.Production };

        var act = () => ProductionKeySafetyGuard.EnforceSafetyRules(config, env);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Trivial or default master key passphrase detected*");
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Symbolon.ControlPlane";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
