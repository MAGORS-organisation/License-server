using System.Text.Json;
using Spectre.Console;

namespace Symbolon.Cli.Wizard;

public sealed record RelayConfigOptions
{
    public required string TargetDirectory { get; init; }
    public int Port { get; init; } = 8081;
    public string? UpstreamControlPlaneUrl { get; init; }
    public string SqliteDatabasePath { get; init; } = "data/symbolon-relay.db";
}

public sealed record RelayConfigResult
{
    public required string TargetDirectory { get; init; }
    public required string ConfigPath { get; init; }
    public required string WindowsScriptPath { get; init; }
    public required string PowerShellScriptPath { get; init; }
    public required string BashScriptPath { get; init; }
    public required string SystemdServicePath { get; init; }
    public required int Port { get; init; }
}

public static class RelaySetup
{
    private static readonly JsonSerializerOptions IndentedJsonOptions = new() { WriteIndented = true };

    public static RelayConfigResult Configure(RelayConfigOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        string targetDir = Path.GetFullPath(options.TargetDirectory);
        string configDir = Path.Combine(targetDir, "config");
        string dataDir = Path.Combine(targetDir, "data");

        Directory.CreateDirectory(targetDir);
        Directory.CreateDirectory(configDir);
        Directory.CreateDirectory(dataDir);

        string dbConnStr = $"Data Source={Path.Combine(targetDir, options.SqliteDatabasePath)}";

        // 1. Generate appsettings.json
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
            Database = new
            {
                ConnectionString = dbConnStr
            },
            Relay = new
            {
                Port = options.Port,
                UpstreamControlPlaneUrl = options.UpstreamControlPlaneUrl
            }
        };

        File.WriteAllText(configPath, JsonSerializer.Serialize(configObj, IndentedJsonOptions));

        // 2. Generate startup scripts
        string cmdPath = Path.Combine(targetDir, "start-relay.cmd");
        File.WriteAllText(cmdPath, $"""
            @echo off
            echo [Symbolon] Starting On-Premise Relay on port {options.Port}...
            set ASPNETCORE_HTTP_PORTS={options.Port}
            set ASPNETCORE_ENVIRONMENT=Production
            set Database__ConnectionString={dbConnStr}
            dotnet run --project ../src/Symbolon.Relay
            pause
            """);

        string ps1Path = Path.Combine(targetDir, "start-relay.ps1");
        File.WriteAllText(ps1Path, $"""
            Write-Host "[Symbolon] Starting On-Premise Relay on port {options.Port}..." -ForegroundColor Cyan
            $env:ASPNETCORE_HTTP_PORTS = "{options.Port}"
            $env:ASPNETCORE_ENVIRONMENT = "Production"
            $env:Database__ConnectionString = "{dbConnStr}"
            dotnet run --project ../src/Symbolon.Relay
            """);

        string shPath = Path.Combine(targetDir, "start-relay.sh");
        File.WriteAllText(shPath, $"""
            #!/usr/bin/env bash
            echo "[Symbolon] Starting On-Premise Relay on port {options.Port}..."
            export ASPNETCORE_HTTP_PORTS="{options.Port}"
            export ASPNETCORE_ENVIRONMENT="Production"
            export Database__ConnectionString="{dbConnStr}"
            dotnet run --project ../src/Symbolon.Relay
            """);

        // 3. Generate systemd service unit
        string servicePath = Path.Combine(targetDir, "symbolon-relay.service");
        File.WriteAllText(servicePath, $"""
            [Unit]
            Description=Symbolon On-Premise License Relay
            After=network.target

            [Service]
            Type=simple
            User=symbolon
            WorkingDirectory={targetDir}
            ExecStart=/usr/bin/dotnet run --project {Path.Combine(targetDir, "../src/Symbolon.Relay")}
            Restart=always
            RestartSec=5
            Environment=ASPNETCORE_HTTP_PORTS={options.Port}
            Environment=ASPNETCORE_ENVIRONMENT=Production
            Environment=Database__ConnectionString={dbConnStr}

            [Install]
            WantedBy=multi-user.target
            """);

        return new RelayConfigResult
        {
            TargetDirectory = targetDir,
            ConfigPath = configPath,
            WindowsScriptPath = cmdPath,
            PowerShellScriptPath = ps1Path,
            BashScriptPath = shPath,
            SystemdServicePath = servicePath,
            Port = options.Port
        };
    }

    public static async Task<bool> RunInteractiveAsync(IAnsiConsole console)
    {
        ArgumentNullException.ThrowIfNull(console);

        console.Write(new Rule("[bold cyan]Inštalácia a Konfigurácia Symbolon Relay[/]").LeftJustified());
        console.WriteLine();

        // 1. Target Directory
        string targetDir = console.Prompt(
            new TextPrompt<string>("Zadajte inštalačný priečinok pre Relay:")
                .DefaultValue("./symbolon-relay"));

        // 2. Port
        int port = console.Prompt(
            new TextPrompt<int>("Zadajte sieťový HTTP port pre Relay:")
                .DefaultValue(8081)
                .Validate(p => p is >= 1 and <= 65535 ? ValidationResult.Success() : ValidationResult.Error("Port musí byť v rozsahu 1-65535")));

        // 3. Upstream ControlPlane
        string upstream = console.Prompt(
            new TextPrompt<string>("URL nadradeného ControlPlane servera (alebo stlačte Enter):")
                .AllowEmpty());

        // 4. Execution
        RelayConfigResult result = null!;
        await console.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Pripravujem konfiguráciu Relay a skripty...", _ =>
            {
                result = Configure(new RelayConfigOptions
                {
                    TargetDirectory = targetDir,
                    Port = port,
                    UpstreamControlPlaneUrl = string.IsNullOrWhiteSpace(upstream) ? null : upstream
                });
                return Task.CompletedTask;
            });

        console.WriteLine();
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("[bold]Parameter[/]");
        table.AddColumn("[bold]Hodnota[/]");
        table.AddRow("Inštalačný priečinok", $"[green]{result.TargetDirectory}[/]");
        table.AddRow("HTTP Port", $"[green]{result.Port}[/]");
        table.AddRow("Konfigurácia", $"[yellow]{result.ConfigPath}[/]");
        table.AddRow("Spúšťač (Windows)", $"[cyan]{result.WindowsScriptPath}[/]");
        table.AddRow("Spúšťač (PowerShell)", $"[cyan]{result.PowerShellScriptPath}[/]");
        table.AddRow("Linux Služba (Systemd)", $"[cyan]{result.SystemdServicePath}[/]");

        console.Write(new Panel(table)
            .Header("[bold green]✔ On-Premise Relay úspešne nakonfigurovaný[/]")
            .Padding(1, 1));

        console.MarkupLine("[bold]Ako Relay spustiť:[/]");
        console.MarkupLine($"1. Prejdite do: [yellow]{result.TargetDirectory}[/]");
        console.MarkupLine("2. Spustite: [green]./start-relay.cmd[/] alebo [green]pwsh ./start-relay.ps1[/]");
        console.MarkupLine($"3. Health check: [link=http://localhost:{result.Port}/health]http://localhost:{result.Port}/health[/]");
        console.WriteLine();

        return true;
    }
}
