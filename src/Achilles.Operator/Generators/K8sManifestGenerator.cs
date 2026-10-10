using System.Globalization;
using System.Text;
using Achilles.Protocol.K8s;

namespace Achilles.Operator.Generators;

public static class K8sManifestGenerator
{
    public static string GenerateDeploymentYaml(AchillesClusterCustomResource cluster)
    {
        ArgumentNullException.ThrowIfNull(cluster);

        string name = cluster.Metadata.Name;
        string ns = cluster.Metadata.Namespace ?? "default";
        int replicas = cluster.Spec.Replicas > 0 ? cluster.Spec.Replicas : 2;
        var img = cluster.Spec.Image ?? new ImageSpec();
        var db = cluster.Spec.Database ?? new DatabaseSpec();
        var crypto = cluster.Spec.Crypto ?? new CryptoSpec();

        string image = $"{img.Repository}:{img.Tag}";
        string pullPolicy = img.PullPolicy;

        var sb = new StringBuilder();
        sb.AppendLine("apiVersion: apps/v1");
        sb.AppendLine("kind: Deployment");
        sb.AppendLine("metadata:");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  name: {name}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  namespace: {ns}");
        sb.AppendLine("  labels:");
        sb.AppendLine("    app.kubernetes.io/name: symbolon-controlplane");
        sb.AppendLine(CultureInfo.InvariantCulture, $"    app.kubernetes.io/instance: {name}");
        sb.AppendLine("spec:");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  replicas: {replicas}");
        sb.AppendLine("  selector:");
        sb.AppendLine("    matchLabels:");
        sb.AppendLine(CultureInfo.InvariantCulture, $"      app.kubernetes.io/instance: {name}");
        sb.AppendLine("  template:");
        sb.AppendLine("    metadata:");
        sb.AppendLine("      labels:");
        sb.AppendLine("        app.kubernetes.io/name: symbolon-controlplane");
        sb.AppendLine(CultureInfo.InvariantCulture, $"        app.kubernetes.io/instance: {name}");
        sb.AppendLine("      annotations:");
        sb.AppendLine("        prometheus.io/scrape: \"true\"");
        sb.AppendLine("        prometheus.io/port: \"8080\"");
        sb.AppendLine("        prometheus.io/path: \"/metrics\"");
        sb.AppendLine("    spec:");
        sb.AppendLine("      securityContext:");
        sb.AppendLine("        runAsNonRoot: true");
        sb.AppendLine("        runAsUser: 1654");
        sb.AppendLine("        fsGroup: 1654");
        sb.AppendLine("      affinity:");
        sb.AppendLine("        podAntiAffinity:");
        sb.AppendLine("          preferredDuringSchedulingIgnoredDuringExecution:");
        sb.AppendLine("            - weight: 100");
        sb.AppendLine("              podAffinityTerm:");
        sb.AppendLine("                labelSelector:");
        sb.AppendLine("                  matchLabels:");
        sb.AppendLine(CultureInfo.InvariantCulture, $"                    app.kubernetes.io/instance: {name}");
        sb.AppendLine("                topologyKey: kubernetes.io/hostname");
        sb.AppendLine("      containers:");
        sb.AppendLine(CultureInfo.InvariantCulture, $"        - name: controlplane");
        sb.AppendLine(CultureInfo.InvariantCulture, $"          image: {image}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"          imagePullPolicy: {pullPolicy}");
        sb.AppendLine("          securityContext:");
        sb.AppendLine("            readOnlyRootFilesystem: true");
        sb.AppendLine("            allowPrivilegeEscalation: false");
        sb.AppendLine("            capabilities:");
        sb.AppendLine("              drop:");
        sb.AppendLine("                - ALL");
        sb.AppendLine("          ports:");
        sb.AppendLine("            - containerPort: 8080");
        sb.AppendLine("              name: http");
        sb.AppendLine("          env:");
        sb.AppendLine("            - name: ASPNETCORE_HTTP_PORTS");
        sb.AppendLine("              value: \"8080\"");
        sb.AppendLine("            - name: ASPNETCORE_ENVIRONMENT");
        sb.AppendLine("              value: \"Production\"");
        sb.AppendLine(CultureInfo.InvariantCulture, $"            - name: Symbolon__PqcEnabled");
        sb.AppendLine(CultureInfo.InvariantCulture, $"              value: \"{(cluster.Spec.PqcEnabled ? "true" : "false")}\"");
        sb.AppendLine(CultureInfo.InvariantCulture, $"            - name: Symbolon__Crypto__KeyAlg");
        sb.AppendLine(CultureInfo.InvariantCulture, $"              value: \"{crypto.KeyAlg}\"");
        sb.AppendLine("            - name: ConnectionStrings__SymbolonDb");
        sb.AppendLine(CultureInfo.InvariantCulture, $"              value: \"Host={db.Host};Port={db.Port};Database={db.Name};Username={db.Username};Password={db.Password ?? "secret"}\"");
        sb.AppendLine("          livenessProbe:");
        sb.AppendLine("            httpGet:");
        sb.AppendLine("              path: /health/live");
        sb.AppendLine("              port: 8080");
        sb.AppendLine("            initialDelaySeconds: 10");
        sb.AppendLine("            periodSeconds: 10");
        sb.AppendLine("            timeoutSeconds: 3");
        sb.AppendLine("          readinessProbe:");
        sb.AppendLine("            httpGet:");
        sb.AppendLine("              path: /health/ready");
        sb.AppendLine("              port: 8080");
        sb.AppendLine("            initialDelaySeconds: 5");
        sb.AppendLine("            periodSeconds: 5");
        sb.AppendLine("            timeoutSeconds: 3");
        sb.AppendLine("          resources:");
        sb.AppendLine("            requests:");
        sb.AppendLine("              cpu: 100m");
        sb.AppendLine("              memory: 128Mi");
        sb.AppendLine("            limits:");
        sb.AppendLine("              cpu: 1000m");
        sb.AppendLine("              memory: 512Mi");

        return sb.ToString();
    }

    public static string GenerateServiceYaml(AchillesClusterCustomResource cluster)
    {
        ArgumentNullException.ThrowIfNull(cluster);

        string name = cluster.Metadata.Name;
        string ns = cluster.Metadata.Namespace ?? "default";

        var sb = new StringBuilder();
        sb.AppendLine("apiVersion: v1");
        sb.AppendLine("kind: Service");
        sb.AppendLine("metadata:");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  name: {name}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  namespace: {ns}");
        sb.AppendLine("  labels:");
        sb.AppendLine("    app.kubernetes.io/name: symbolon-controlplane");
        sb.AppendLine(CultureInfo.InvariantCulture, $"    app.kubernetes.io/instance: {name}");
        sb.AppendLine("spec:");
        sb.AppendLine("  type: ClusterIP");
        sb.AppendLine("  ports:");
        sb.AppendLine("    - port: 8080");
        sb.AppendLine("      targetPort: 8080");
        sb.AppendLine("      name: http");
        sb.AppendLine("  selector:");
        sb.AppendLine(CultureInfo.InvariantCulture, $"    app.kubernetes.io/instance: {name}");

        return sb.ToString();
    }

    public static string? GenerateIngressYaml(AchillesClusterCustomResource cluster)
    {
        ArgumentNullException.ThrowIfNull(cluster);

        var ing = cluster.Spec.Ingress;
        if (ing == null || !ing.Enabled || string.IsNullOrWhiteSpace(ing.Host))
        {
            return null;
        }

        string name = cluster.Metadata.Name;
        string ns = cluster.Metadata.Namespace ?? "default";
        string host = ing.Host;
        string certIssuer = ing.CertManagerIssuer;

        var sb = new StringBuilder();
        sb.AppendLine("apiVersion: networking.k8s.io/v1");
        sb.AppendLine("kind: Ingress");
        sb.AppendLine("metadata:");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  name: {name}-ingress");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  namespace: {ns}");
        sb.AppendLine("  annotations:");
        sb.AppendLine(CultureInfo.InvariantCulture, $"    cert-manager.io/cluster-issuer: {certIssuer}");
        if (ing.Annotations != null)
        {
            foreach (var (k, v) in ing.Annotations)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"    {k}: \"{v}\"");
            }
        }
        sb.AppendLine("spec:");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  ingressClassName: {ing.ClassName}");
        if (ing.Tls)
        {
            sb.AppendLine("  tls:");
            sb.AppendLine(CultureInfo.InvariantCulture, $"    - hosts:");
            sb.AppendLine(CultureInfo.InvariantCulture, $"        - {host}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"      secretName: {name}-tls");
        }
        sb.AppendLine("  rules:");
        sb.AppendLine(CultureInfo.InvariantCulture, $"    - host: {host}");
        sb.AppendLine("      http:");
        sb.AppendLine("        paths:");
        sb.AppendLine("          - path: /");
        sb.AppendLine("            pathType: Prefix");
        sb.AppendLine("            backend:");
        sb.AppendLine("              service:");
        sb.AppendLine(CultureInfo.InvariantCulture, $"                name: {name}");
        sb.AppendLine("                port:");
        sb.AppendLine("                  number: 8080");

        return sb.ToString();
    }

    public static string GeneratePdbYaml(AchillesClusterCustomResource cluster)
    {
        ArgumentNullException.ThrowIfNull(cluster);

        string name = cluster.Metadata.Name;
        string ns = cluster.Metadata.Namespace ?? "default";

        var sb = new StringBuilder();
        sb.AppendLine("apiVersion: policy/v1");
        sb.AppendLine("kind: PodDisruptionBudget");
        sb.AppendLine("metadata:");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  name: {name}-pdb");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  namespace: {ns}");
        sb.AppendLine("spec:");
        sb.AppendLine("  minAvailable: 1");
        sb.AppendLine("  selector:");
        sb.AppendLine("    matchLabels:");
        sb.AppendLine(CultureInfo.InvariantCulture, $"      app.kubernetes.io/instance: {name}");

        return sb.ToString();
    }

    public static string GenerateServiceMonitorYaml(AchillesClusterCustomResource cluster)
    {
        ArgumentNullException.ThrowIfNull(cluster);

        string name = cluster.Metadata.Name;
        string ns = cluster.Metadata.Namespace ?? "default";

        var sb = new StringBuilder();
        sb.AppendLine("apiVersion: monitoring.coreos.com/v1");
        sb.AppendLine("kind: ServiceMonitor");
        sb.AppendLine("metadata:");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  name: {name}-monitor");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  namespace: {ns}");
        sb.AppendLine("  labels:");
        sb.AppendLine("    release: prometheus");
        sb.AppendLine("spec:");
        sb.AppendLine("  selector:");
        sb.AppendLine("    matchLabels:");
        sb.AppendLine(CultureInfo.InvariantCulture, $"      app.kubernetes.io/instance: {name}");
        sb.AppendLine("  endpoints:");
        sb.AppendLine("    - port: http");
        sb.AppendLine("      path: /metrics");
        sb.AppendLine("      interval: 15s");

        return sb.ToString();
    }

    public static string GenerateLicenseSecretYaml(
        AchillesLicenseCustomResource license,
        string signedLicenseArmored)
    {
        ArgumentNullException.ThrowIfNull(license);
        ArgumentNullException.ThrowIfNull(signedLicenseArmored);

        string secretName = !string.IsNullOrWhiteSpace(license.Spec.TargetSecretName)
            ? license.Spec.TargetSecretName
            : $"{license.Metadata.Name}-secret";
        string ns = license.Metadata.Namespace ?? "default";

        byte[] armoredBytes = Encoding.UTF8.GetBytes(signedLicenseArmored);
        string armoredB64 = Convert.ToBase64String(armoredBytes);

        var sb = new StringBuilder();
        sb.AppendLine("apiVersion: v1");
        sb.AppendLine("kind: Secret");
        sb.AppendLine("metadata:");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  name: {secretName}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  namespace: {ns}");
        sb.AppendLine("  labels:");
        sb.AppendLine("    app.kubernetes.io/managed-by: symbolon-operator");
        sb.AppendLine(CultureInfo.InvariantCulture, $"    licensing.symbolon.io/license: {license.Metadata.Name}");
        sb.AppendLine("type: Opaque");
        sb.AppendLine("data:");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  license.symlic: {armoredB64}");
        sb.AppendLine("stringData:");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  TENANT_ID: \"{license.Spec.TenantId}\"");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  PRODUCT_ID: \"{license.Spec.ProductId}\"");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  MAX_SEATS: \"{license.Spec.MaxSeats}\"");
        if (license.Spec.ExpiresAt.HasValue)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"  EXPIRES_AT: \"{license.Spec.ExpiresAt.Value:O}\"");
        }

        return sb.ToString();
    }
}
