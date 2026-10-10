using System.Text.Json.Serialization;

namespace Achilles.Protocol.K8s;

public sealed record K8sObjectMeta
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("namespace")]
    public string? Namespace { get; init; }

    [JsonPropertyName("labels")]
    public IReadOnlyDictionary<string, string>? Labels { get; init; }

    [JsonPropertyName("annotations")]
    public IReadOnlyDictionary<string, string>? Annotations { get; init; }

    [JsonPropertyName("creationTimestamp")]
    public DateTimeOffset? CreationTimestamp { get; init; }

    [JsonPropertyName("resourceVersion")]
    public string? ResourceVersion { get; init; }

    [JsonPropertyName("generation")]
    public long? Generation { get; init; }
}

public sealed record K8sCondition
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; } // "True", "False", "Unknown"

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("lastTransitionTime")]
    public DateTimeOffset LastTransitionTime { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record ImageSpec
{
    [JsonPropertyName("repository")]
    public string Repository { get; init; } = "ghcr.io/magors-organisation/symbolon-controlplane";

    [JsonPropertyName("tag")]
    public string Tag { get; init; } = "latest";

    [JsonPropertyName("pullPolicy")]
    public string PullPolicy { get; init; } = "IfNotPresent";
}

public sealed record DatabaseSpec
{
    [JsonPropertyName("host")]
    public string Host { get; init; } = "postgres-service";

    [JsonPropertyName("port")]
    public int Port { get; init; } = 5432;

    [JsonPropertyName("name")]
    public string Name { get; init; } = "symbolon";

    [JsonPropertyName("username")]
    public string Username { get; init; } = "symbolon";

    [JsonPropertyName("existingSecret")]
    public string? ExistingSecret { get; init; }

    [JsonPropertyName("password")]
    public string? Password { get; init; }
}

public sealed record CryptoSpec
{
    [JsonPropertyName("keyAlg")]
    public string KeyAlg { get; init; } = "Hybrid"; // ES256, ML-DSA-65, Hybrid

    [JsonPropertyName("existingSecret")]
    public string? ExistingSecret { get; init; }

    [JsonPropertyName("autoGenerate")]
    public bool AutoGenerate { get; init; } = true;
}

public sealed record GeoPeerSpec
{
    [JsonPropertyName("region")]
    public required string Region { get; init; }

    [JsonPropertyName("endpoint")]
    public required string Endpoint { get; init; }
}

public sealed record GeoReplicationSpec
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }

    [JsonPropertyName("clusterId")]
    public string ClusterId { get; init; } = "eu-central-1";

    [JsonPropertyName("seatRangeStart")]
    public int SeatRangeStart { get; init; } = 1;

    [JsonPropertyName("seatRangeEnd")]
    public int SeatRangeEnd { get; init; } = 100;

    [JsonPropertyName("peers")]
    public IReadOnlyList<GeoPeerSpec>? Peers { get; init; }
}

public sealed record IngressSpec
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }

    [JsonPropertyName("className")]
    public string ClassName { get; init; } = "nginx";

    [JsonPropertyName("host")]
    public string? Host { get; init; }

    [JsonPropertyName("tls")]
    public bool Tls { get; init; } = true;

    [JsonPropertyName("certManagerIssuer")]
    public string CertManagerIssuer { get; init; } = "letsencrypt-prod";

    [JsonPropertyName("annotations")]
    public IReadOnlyDictionary<string, string>? Annotations { get; init; }
}

public sealed record MonitoringSpec
{
    [JsonPropertyName("serviceMonitor")]
    public bool ServiceMonitor { get; init; } = true;

    [JsonPropertyName("prometheusScrape")]
    public bool PrometheusScrape { get; init; } = true;

    [JsonPropertyName("metricsPort")]
    public int MetricsPort { get; init; } = 8080;
}

public sealed record AchillesClusterSpec
{
    [JsonPropertyName("replicas")]
    public int Replicas { get; init; } = 2;

    [JsonPropertyName("image")]
    public ImageSpec? Image { get; init; }

    [JsonPropertyName("pqcEnabled")]
    public bool PqcEnabled { get; init; } = true;

    [JsonPropertyName("database")]
    public DatabaseSpec? Database { get; init; }

    [JsonPropertyName("crypto")]
    public CryptoSpec? Crypto { get; init; }

    [JsonPropertyName("geoReplication")]
    public GeoReplicationSpec? GeoReplication { get; init; }

    [JsonPropertyName("ingress")]
    public IngressSpec? Ingress { get; init; }

    [JsonPropertyName("monitoring")]
    public MonitoringSpec? Monitoring { get; init; }
}

public sealed record SymbolonClusterStatus
{
    [JsonPropertyName("phase")]
    public string Phase { get; init; } = "Pending";

    [JsonPropertyName("readyReplicas")]
    public int ReadyReplicas { get; init; }

    [JsonPropertyName("activeKid")]
    public string? ActiveKid { get; init; }

    [JsonPropertyName("observedGeneration")]
    public long? ObservedGeneration { get; init; }

    [JsonPropertyName("lastReconciledAt")]
    public DateTimeOffset? LastReconciledAt { get; init; }

    [JsonPropertyName("conditions")]
    public IReadOnlyList<K8sCondition> Conditions { get; init; } = [];
}

public sealed record AchillesClusterCustomResource
{
    [JsonPropertyName("apiVersion")]
    public string ApiVersion { get; init; } = "licensing.symbolon.io/v1alpha1";

    [JsonPropertyName("kind")]
    public string Kind { get; init; } = "SymbolonCluster";

    [JsonPropertyName("metadata")]
    public K8sObjectMeta Metadata { get; init; } = new();

    [JsonPropertyName("spec")]
    public AchillesClusterSpec Spec { get; init; } = new();

    [JsonPropertyName("status")]
    public SymbolonClusterStatus? Status { get; init; }
}

public sealed record ClusterRefSpec
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "symbolon-cluster";

    [JsonPropertyName("namespace")]
    public string? Namespace { get; init; }
}

public sealed record AchillesLicenseSpec
{
    [JsonPropertyName("clusterRef")]
    public ClusterRefSpec? ClusterRef { get; init; }

    [JsonPropertyName("tenantId")]
    public required string TenantId { get; init; }

    [JsonPropertyName("productId")]
    public required string ProductId { get; init; }

    [JsonPropertyName("features")]
    public IReadOnlyList<string>? Features { get; init; }

    [JsonPropertyName("maxSeats")]
    public int MaxSeats { get; init; } = 10;

    [JsonPropertyName("expiresAt")]
    public DateTimeOffset? ExpiresAt { get; init; }

    [JsonPropertyName("nodeLockFingerprints")]
    public IReadOnlyList<string>? NodeLockFingerprints { get; init; }

    [JsonPropertyName("targetSecretName")]
    public string? TargetSecretName { get; init; }

    [JsonPropertyName("suspended")]
    public bool Suspended { get; init; }
}

public sealed record SymbolonLicenseStatus
{
    [JsonPropertyName("phase")]
    public string Phase { get; init; } = "Pending"; // Pending, Active, Suspended, Revoked, Expired, Failed

    [JsonPropertyName("licenseId")]
    public string? LicenseId { get; init; }

    [JsonPropertyName("issuedKeyHash")]
    public string? IssuedKeyHash { get; init; }

    [JsonPropertyName("signatureAlg")]
    public string? SignatureAlg { get; init; }

    [JsonPropertyName("secretRef")]
    public string? SecretRef { get; init; }

    [JsonPropertyName("lastReconciledAt")]
    public DateTimeOffset? LastReconciledAt { get; init; }

    [JsonPropertyName("conditions")]
    public IReadOnlyList<K8sCondition> Conditions { get; init; } = [];
}

public sealed record AchillesLicenseCustomResource
{
    [JsonPropertyName("apiVersion")]
    public string ApiVersion { get; init; } = "licensing.symbolon.io/v1alpha1";

    [JsonPropertyName("kind")]
    public string Kind { get; init; } = "SymbolonLicense";

    [JsonPropertyName("metadata")]
    public K8sObjectMeta Metadata { get; init; } = new();

    [JsonPropertyName("spec")]
    public AchillesLicenseSpec Spec { get; init; } = null!;

    [JsonPropertyName("status")]
    public SymbolonLicenseStatus? Status { get; init; }
}
