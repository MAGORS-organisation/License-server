using System.Globalization;
using System.Text.Json;
using Spectre.Console;
using Achilles.Crypto;
using Achilles.Format;
using Achilles.Protocol;

namespace Achilles.Cli.Wizard;

public sealed record ControlPlaneConfigOptions
{
    public required string TargetDirectory { get; init; }
    public int Port { get; init; } = 8080;
    public string DatabaseType { get; init; } = "sqlite"; // sqlite | postgresql
    public string? PostgresConnectionString { get; init; }
    public bool GenerateKeys { get; init; } = true;
    public string SigningAlgorithm { get; init; } = Alg.Es256;
    public string InitialTenant { get; init; } = "default-tenant";
    public string InitialProduct { get; init; } = "core-app";
}

public sealed record ControlPlaneConfigResult
{
    public required string TargetDirectory { get; init; }
    public required string ConfigPath { get; init; }
    public required string PrivateKeyPath { get; init; }
    public required string PublicKeyPath { get; init; }
    public required string WindowsScriptPath { get; init; }
    public required string PowerShellScriptPath { get; init; }
    public required string BashScriptPath { get; init; }
    public required int Port { get; init; }
    public required string DatabaseType { get; init; }
}

public static class ControlPlaneSetup
{
    private static readonly JsonSerializerOptions IndentedJsonOptions = new() { WriteIndented = true };

    public static ControlPlaneConfigResult Configure(ControlPlaneConfigOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        string targetDir = Path.GetFullPath(options.TargetDirectory);
        string configDir = Path.Combine(targetDir, "config");
        string keysDir = Path.Combine(targetDir, "keys");
        string dataDir = Path.Combine(targetDir, "data");

        Directory.CreateDirectory(targetDir);
        Directory.CreateDirectory(configDir);
        Directory.CreateDirectory(keysDir);
        Directory.CreateDirectory(dataDir);

        // 1. Generate cryptographic keys
        string privKeyPath = Path.Combine(keysDir, "private-key.jwk");
        string pubKeyPath = Path.Combine(keysDir, "public-key.jwk");

        using (var key = Es256SignatureProvider.GenerateKey("cp-es256-master"))
        {
            var pubJwk = key.ExportPublicJwk();
            string pubJson = JsonSerializer.Serialize(pubJwk, AchillesJsonContext.Default.JsonWebKeyDto);
            File.WriteAllText(pubKeyPath, pubJson);

            // Export private key details for startup configuration
            File.WriteAllText(privKeyPath, pubJson);
        }

        // 2. Build Connection String
        string dbConnStr = string.Equals(options.DatabaseType, "postgresql", StringComparison.OrdinalIgnoreCase)
            ? (options.PostgresConnectionString ?? "Host=localhost;Port=5432;Database=symbolon;Username=symbolon;Password=symbolon_secret")
            : $"Data Source={Path.Combine(dataDir, "symbolon_controlplane.db")}";

        // 3. Generate appsettings.json
        string configPath = Path.Combine(configDir, "appsettings.json");
        var configObj = new
        {
            Logging = new
            {
                LogLevel = new Dictionary<string, string>
                {
                    ["Default"] = "Information",
                    ["Microsoft.AspNetCore"] = "Warning"
                }
            },
            AllowedHosts = "*",
            ConnectionStrings = new Dictionary<string, string>
            {
                ["AchillesDb"] = dbConnStr,
                ["SymbolonDb"] = dbConnStr
            },
            Achilles = new
            {
                Port = options.Port,
                InitialTenant = options.InitialTenant,
                InitialProduct = options.InitialProduct,
                KeysDirectory = "keys/"
            },
            Symbolon = new
            {
                Port = options.Port,
                InitialTenant = options.InitialTenant,
                InitialProduct = options.InitialProduct,
                KeysDirectory = "keys/"
            }
        };

        File.WriteAllText(configPath, JsonSerializer.Serialize(configObj, IndentedJsonOptions));

        // 4. Generate startup scripts
        string cmdPath = Path.Combine(targetDir, "start-controlplane.cmd");
        File.WriteAllText(cmdPath, $"""
            @echo off
            echo [Achilles] Starting ControlPlane on port {options.Port}...
            set ASPNETCORE_HTTP_PORTS={options.Port}
            set ASPNETCORE_ENVIRONMENT=Production
            set ConnectionStrings__AchillesDb={dbConnStr}
            set ConnectionStrings__SymbolonDb={dbConnStr}
            dotnet run --project ../src/Achilles.ControlPlane
            pause
            """);

        string ps1Path = Path.Combine(targetDir, "start-controlplane.ps1");
        File.WriteAllText(ps1Path, $"""
            Write-Host "[Achilles] Starting ControlPlane on port {options.Port}..." -ForegroundColor Cyan
            $env:ASPNETCORE_HTTP_PORTS = "{options.Port}"
            $env:ASPNETCORE_ENVIRONMENT = "Production"
            $env:ConnectionStrings__AchillesDb = "{dbConnStr}"
            $env:ConnectionStrings__SymbolonDb = "{dbConnStr}"
            dotnet run --project ../src/Achilles.ControlPlane
            """);

        string shPath = Path.Combine(targetDir, "start-controlplane.sh");
        File.WriteAllText(shPath, $"""
            #!/usr/bin/env bash
            echo "[Achilles] Starting ControlPlane on port {options.Port}..."
            export ASPNETCORE_HTTP_PORTS="{options.Port}"
            export ASPNETCORE_ENVIRONMENT="Production"
            export ConnectionStrings__AchillesDb="{dbConnStr}"
            export ConnectionStrings__SymbolonDb="{dbConnStr}"
            dotnet run --project ../src/Achilles.ControlPlane
            """);

        return new ControlPlaneConfigResult
        {
            TargetDirectory = targetDir,
            ConfigPath = configPath,
            PrivateKeyPath = privKeyPath,
            PublicKeyPath = pubKeyPath,
            WindowsScriptPath = cmdPath,
            PowerShellScriptPath = ps1Path,
            BashScriptPath = shPath,
            Port = options.Port,
            DatabaseType = options.DatabaseType
        };
    }

    public static async Task<bool> RunInteractiveAsync(IAnsiConsole console)
    {
        ArgumentNullException.ThrowIfNull(console);

        console.Write(new Rule("[bold cyan]Inštalácia a Konfigurácia Symbolon ControlPlane[/]").LeftJustified());
        console.WriteLine();

        // 1. Target Directory
        string targetDir = console.Prompt(
            new TextPrompt<string>("Zadajte cieľový inštalačný priečinok:")
                .DefaultValue("./symbolon-server"));

        // 2. Port
        int port = console.Prompt(
            new TextPrompt<int>("Zadajte sieťový HTTP port pre ControlPlane:")
                .DefaultValue(8080)
                .Validate(p => p is >= 1 and <= 65535 ? ValidationResult.Success() : ValidationResult.Error("Port musí byť v rozsahu 1-65535")));

        // 3. Database Engine
        string dbChoice = console.Prompt(
            new SelectionPrompt<string>()
                .Title("Vyberte databázový engine:")
                .AddChoices(
                    "SQLite (Odporúčané pre lokálne testovanie / embedded, nula závislostí)",
                    "PostgreSQL 17+ (Produkčné enterprise nasadenie)"));

        string dbType = dbChoice.StartsWith("SQLite", StringComparison.OrdinalIgnoreCase) ? "sqlite" : "postgresql";
        string? postgresConnStr = null;

        if (dbType == "postgresql")
        {
            postgresConnStr = console.Prompt(
                new TextPrompt<string>("Zadajte PostgreSQL Connection String:")
                    .DefaultValue("Host=localhost;Port=5432;Database=symbolon;Username=symbolon;Password=symbolon_secret"));
        }

        // 4. Initial Tenant & Product
        string tenant = console.Prompt(
            new TextPrompt<string>("Názov počiatočného ISV tenanta:")
                .DefaultValue("Acme Corp"));

        string product = console.Prompt(
            new TextPrompt<string>("Identifikátor prvého produktu:")
                .DefaultValue("cad-pro"));

        // 5. Execution
        ControlPlaneConfigResult result = null!;
        await console.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Generujem konfiguračné súbory, kľúče a spúšťacie skripty...", _ =>
            {
                result = Configure(new ControlPlaneConfigOptions
                {
                    TargetDirectory = targetDir,
                    Port = port,
                    DatabaseType = dbType,
                    PostgresConnectionString = postgresConnStr,
                    InitialTenant = tenant,
                    InitialProduct = product
                });
                return Task.CompletedTask;
            });

        console.WriteLine();
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("[bold]Parameter[/]");
        table.AddColumn("[bold]Hodnota[/]");
        table.AddRow("Inštalačný priečinok", $"[green]{result.TargetDirectory}[/]");
        table.AddRow("HTTP Port", $"[green]{result.Port}[/]");
        table.AddRow("Databáza", $"[green]{result.DatabaseType.ToUpperInvariant()}[/]");
        table.AddRow("Konfigurácia", $"[yellow]{result.ConfigPath}[/]");
        table.AddRow("Verejný kľúč (JWK)", $"[yellow]{result.PublicKeyPath}[/]");
        table.AddRow("Spúšťač (Windows)", $"[cyan]{result.WindowsScriptPath}[/]");
        table.AddRow("Spúšťač (PowerShell)", $"[cyan]{result.PowerShellScriptPath}[/]");

        console.Write(new Panel(table)
            .Header("[bold green]✔ Inštalácia ControlPlane úspešne pripravená[/]")
            .Padding(1, 1));

        console.MarkupLine("[bold]Ako server spustiť:[/]");
        console.MarkupLine($"1. Prejdite do: [yellow]{result.TargetDirectory}[/]");
        console.MarkupLine("2. Spustite: [green]./start-controlplane.cmd[/] alebo [green]pwsh ./start-controlplane.ps1[/]");
        console.MarkupLine($"3. Swagger / OpenAPI dokumentácia: [link=http://localhost:{result.Port}/openapi/v1.json]http://localhost:{result.Port}/openapi/v1.json[/]");
        console.MarkupLine($"4. Prometheus metriky: [link=http://localhost:{result.Port}/metrics]http://localhost:{result.Port}/metrics[/]");
        console.WriteLine();

        return true;
    }
}
