using System.Globalization;
using System.Text;
using Spectre.Console;
using Achilles.Operator.Generators;
using Achilles.Protocol.K8s;

namespace Achilles.Cli.Commands;

public static class K8sCommands
{
    public static Task<int> HandleK8sAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintK8sHelp();
            return Task.FromResult(0);
        }

        return args[0].ToUpperInvariant() switch
        {
            "CRD" => Task.FromResult(HandleCrd(args[1..])),
            "EXPORT-LICENSE" => Task.FromResult(HandleExportLicense(args[1..])),
            "GENERATE-CLUSTER" => Task.FromResult(HandleGenerateCluster(args[1..])),
            _ => Task.FromResult(UnknownSubcommand(args[0]))
        };
    }

    private static int HandleCrd(string[] args)
    {
        string? outDir = GetArg(args, "--out-dir");

        string clusterCrd = GetClusterCrdYaml();
        string licenseCrd = GetLicenseCrdYaml();

        if (!string.IsNullOrWhiteSpace(outDir))
        {
            Directory.CreateDirectory(outDir);
            string clusterPath = Path.Combine(outDir, "licensing.symbolon.io_symbolonclusters.yaml");
            string licensePath = Path.Combine(outDir, "licensing.symbolon.io_symbolonlicenses.yaml");

            File.WriteAllText(clusterPath, clusterCrd);
            File.WriteAllText(licensePath, licenseCrd);

            AnsiConsole.MarkupLine($"[green]✓ CRD definície úspešne vyexportované do priečinka:[/] [bold cyan]{outDir}[/]");
            AnsiConsole.MarkupLine($"  - {clusterPath}");
            AnsiConsole.MarkupLine($"  - {licensePath}");
        }
        else
        {
            AnsiConsole.MarkupLine("[bold cyan]=== SYMBOLON KUBERNETES CUSTOM RESOURCE DEFINITIONS (CRDS) ===[/]");
            AnsiConsole.WriteLine(clusterCrd);
            AnsiConsole.WriteLine("---");
            AnsiConsole.WriteLine(licenseCrd);
        }

        return 0;
    }

    private static int HandleExportLicense(string[] args)
    {
        string tenant = GetArg(args, "--tenant") ?? "ten_default";
        string product = GetArg(args, "--product") ?? "default-product";
        int seats = int.TryParse(GetArg(args, "--seats"), CultureInfo.InvariantCulture, out int s) ? s : 10;
        string? name = GetArg(args, "--name") ?? $"{product}-license";
        string? secretName = GetArg(args, "--secret-name") ?? $"{name}-secret";
        string? outFile = GetArg(args, "--out");

        var licenseCr = new AchillesLicenseCustomResource
        {
            Metadata = new K8sObjectMeta
            {
                Name = name,
                Namespace = "default"
            },
            Spec = new AchillesLicenseSpec
            {
                TenantId = tenant,
                ProductId = product,
                MaxSeats = seats,
                TargetSecretName = secretName
            }
        };

        var sb = new StringBuilder();
        sb.AppendLine("apiVersion: licensing.symbolon.io/v1alpha1");
        sb.AppendLine("kind: SymbolonLicense");
        sb.AppendLine("metadata:");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  name: {licenseCr.Metadata.Name}");
        sb.AppendLine("  namespace: default");
        sb.AppendLine("spec:");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  tenantId: \"{licenseCr.Spec.TenantId}\"");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  productId: \"{licenseCr.Spec.ProductId}\"");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  maxSeats: {licenseCr.Spec.MaxSeats}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  targetSecretName: \"{licenseCr.Spec.TargetSecretName}\"");

        string yaml = sb.ToString();

        if (!string.IsNullOrWhiteSpace(outFile))
        {
            File.WriteAllText(outFile, yaml);
            AnsiConsole.MarkupLine($"[green]✓ GitOps deklaratívna licencia vyexportovaná do:[/] [bold cyan]{outFile}[/]");
        }
        else
        {
            AnsiConsole.MarkupLine("[bold cyan]=== GITOPS DECLARATIVE SYMBOLON LICENSE CR ===[/]");
            AnsiConsole.WriteLine(yaml);
        }

        return 0;
    }

    private static int HandleGenerateCluster(string[] args)
    {
        string name = GetArg(args, "--name") ?? "symbolon-prod";
        int replicas = int.TryParse(GetArg(args, "--replicas"), CultureInfo.InvariantCulture, out int r) ? r : 3;
        string dbHost = GetArg(args, "--db-host") ?? "postgres-ha.database.svc.cluster.local";
        bool pqc = args.Contains("--pqc");
        string? outFile = GetArg(args, "--out");

        var sb = new StringBuilder();
        sb.AppendLine("apiVersion: licensing.symbolon.io/v1alpha1");
        sb.AppendLine("kind: SymbolonCluster");
        sb.AppendLine("metadata:");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  name: {name}");
        sb.AppendLine("  namespace: symbolon-system");
        sb.AppendLine("spec:");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  replicas: {replicas}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  pqcEnabled: {(pqc ? "true" : "false")}");
        sb.AppendLine("  database:");
        sb.AppendLine(CultureInfo.InvariantCulture, $"    host: \"{dbHost}\"");
        sb.AppendLine("    port: 5432");
        sb.AppendLine("    name: \"symbolon\"");
        sb.AppendLine("    username: \"symbolon\"");
        sb.AppendLine("    existingSecret: \"symbolon-db-credentials\"");
        sb.AppendLine("  crypto:");
        sb.AppendLine("    keyAlg: \"Hybrid\"");
        sb.AppendLine("    autoGenerate: true");
        sb.AppendLine("  ingress:");
        sb.AppendLine("    enabled: true");
        sb.AppendLine("    className: \"nginx\"");
        sb.AppendLine(CultureInfo.InvariantCulture, $"    host: \"{name}.licensing.enterprise.corp\"");
        sb.AppendLine("    tls: true");
        sb.AppendLine("    certManagerIssuer: \"letsencrypt-prod\"");
        sb.AppendLine("  monitoring:");
        sb.AppendLine("    serviceMonitor: true");
        sb.AppendLine("    prometheusScrape: true");

        string yaml = sb.ToString();

        if (!string.IsNullOrWhiteSpace(outFile))
        {
            File.WriteAllText(outFile, yaml);
            AnsiConsole.MarkupLine($"[green]✓ GitOps SymbolonCluster manifest vygenerovaný do:[/] [bold cyan]{outFile}[/]");
        }
        else
        {
            AnsiConsole.MarkupLine("[bold cyan]=== GITOPS DECLARATIVE SYMBOLON CLUSTER CR ===[/]");
            AnsiConsole.WriteLine(yaml);
        }

        return 0;
    }

    private static string GetClusterCrdYaml()
    {
        return """
        apiVersion: apiextensions.k8s.io/v1
        kind: CustomResourceDefinition
        metadata:
          name: symbolonclusters.licensing.symbolon.io
        spec:
          group: licensing.symbolon.io
          names:
            kind: SymbolonCluster
            listKind: SymbolonClusterList
            plural: symbolonclusters
            singular: symboloncluster
            shortNames: [sc, symc]
          scope: Namespaced
          versions:
            - name: v1alpha1
              served: true
              storage: true
        """;
    }

    private static string GetLicenseCrdYaml()
    {
        return """
        apiVersion: apiextensions.k8s.io/v1
        kind: CustomResourceDefinition
        metadata:
          name: symbolonlicenses.licensing.symbolon.io
        spec:
          group: licensing.symbolon.io
          names:
            kind: SymbolonLicense
            listKind: SymbolonLicenseList
            plural: symbolonlicenses
            singular: symbolonlicense
            shortNames: [slic, symlic]
          scope: Namespaced
          versions:
            - name: v1alpha1
              served: true
              storage: true
        """;
    }

    private static string? GetArg(string[] args, string flag)
    {
        int idx = Array.IndexOf(args, flag);
        return idx >= 0 && idx < args.Length - 1 ? args[idx + 1] : null;
    }

    private static int UnknownSubcommand(string sub)
    {
        AnsiConsole.MarkupLine($"[red]Neznámy K8s podpríkaz: '{sub}'. Použite 'symbolon k8s --help'.[/]");
        return 1;
    }

    private static void PrintK8sHelp()
    {
        AnsiConsole.MarkupLine("[bold cyan]Použitie:[/] symbolon k8s <podpríkaz> [prepínače]");
        AnsiConsole.MarkupLine("\n[bold yellow]Podpríkazy:[/]");
        AnsiConsole.MarkupLine("  [green]crd[/]                 Export kompletných OpenAPI v3 CRD definícií pre Kubernetes");
        AnsiConsole.MarkupLine("  [green]export-license[/]      Vygeneruje deklaratívny SymbolonLicense manifest pre GitOps");
        AnsiConsole.MarkupLine("  [green]generate-cluster[/]    Vygeneruje deklaratívny HA SymbolonCluster manifest");
        AnsiConsole.MarkupLine("\n[bold yellow]Príklady:[/]");
        AnsiConsole.MarkupLine("  symbolon k8s crd --out-dir ./crds");
        AnsiConsole.MarkupLine("  symbolon k8s export-license --tenant ten_corp --product cad-pro --seats 50 --out license.yaml");
        AnsiConsole.MarkupLine("  symbolon k8s generate-cluster --name symbolon-prod --replicas 3 --pqc --out cluster.yaml");
    }
}
