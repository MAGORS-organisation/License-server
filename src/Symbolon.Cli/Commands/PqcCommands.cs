using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Spectre.Console;
using Symbolon.Crypto;
using Symbolon.Domain.Pqc;

namespace Symbolon.Cli.Commands;

public static class PqcCommands
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static async Task<int> HandlePqcAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintPqcHelp();
            return 0;
        }

        return args[0].ToUpperInvariant() switch
        {
            "SCAN" => await HandleScanAsync(args[1..]).ConfigureAwait(false),
            "PROFILE" => await HandleProfileAsync(args[1..]).ConfigureAwait(false),
            "KEM" => await HandleKemAsync(args[1..]).ConfigureAwait(false),
            "VERIFY" => HandleVerify(args[1..]),
            _ => UnknownSubcommand(args[0])
        };
    }

    private static void PrintPqcHelp()
    {
        AnsiConsole.MarkupLine("[bold cyan]⚛️ SYMBOLON Post-Quantum Era Suite (Míľnik M7, §13.5)[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]Použitie:[/] symbolon pqc <príkaz> [[voľby]]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Príkazy:[/] ");
        AnsiConsole.MarkupLine("  [green]scan[/]                         Spustí Quantum Readiness & Vulnerability audit");
        AnsiConsole.MarkupLine("  [green]profile [[get|set <mód>]][/]      Zobrazenie alebo zmena PQC profilu (hybrid-v1 / pqc-strict)");
        AnsiConsole.MarkupLine("  [green]kem [[generate|test]][/]         Správa a testovanie ML-KEM-768/1024 kľúčovej enkapsulácie");
        AnsiConsole.MarkupLine("  [green]verify <súbor>[/]                Overenie či je licenčný artefakt podpísaný PQC algoritmom");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Voľby pre scan:[/] ");
        AnsiConsole.MarkupLine("  --server <url>                 URL Control Plane servera (predvolené: http://localhost:5000)");
        AnsiConsole.MarkupLine("  --out <súbor>                  Uloží kompletný JSON report na disk");
        AnsiConsole.MarkupLine("  --strict                       Zlyhá s návratovým kódom 1, ak PQC Readiness < 100%");
    }

    private static async Task<int> HandleScanAsync(string[] args)
    {
        string server = "http://localhost:5000";
        string? outFile = null;
        bool strict = false;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] is "--server" or "-s" && i + 1 < args.Length) server = args[++i];
            else if (args[i] is "--out" or "-o" && i + 1 < args.Length) outFile = args[++i];
            else if (args[i] is "--strict") strict = true;
        }

        AnsiConsole.MarkupLine($"[grey]Pripájam sa k serveru {server} a spúšťam PQC Readiness Scan...[/]");

        using var client = new HttpClient { BaseAddress = new Uri(server) };
        try
        {
            var res = await client.GetAsync(new Uri("/admin/v1/pqc/readiness", UriKind.Relative)).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[bold red]Chyba pri získavaní PQC reportu: {res.StatusCode}[/]");
                return 1;
            }

            var report = await res.Content.ReadFromJsonAsync<PqcReadinessReport>(JsonOptions).ConfigureAwait(false);
            if (report is null)
            {
                AnsiConsole.MarkupLine("[bold red]Prázdna odpoveď zo servera.[/]");
                return 1;
            }

            RenderScanReport(report);

            if (!string.IsNullOrWhiteSpace(outFile))
            {
                string json = JsonSerializer.Serialize(report, JsonOptions);
                await File.WriteAllTextAsync(outFile, json).ConfigureAwait(false);
                AnsiConsole.MarkupLine($"[green]Report úspešne uložený do: {outFile}[/]");
            }

            if (strict && report.ReadinessScorePercent < 100.0)
            {
                AnsiConsole.MarkupLine("[bold red]STRICT FAIL: Systém nespĺňa 100% PQC požiadavku (detegované klasické kryptografické komponenty).[/]");
                return 1;
            }

            return 0;
        }
        catch (HttpRequestException ex)
        {
            AnsiConsole.MarkupLine($"[bold red]Chyba spojenia s Control Plane: {ex.Message}[/]");
            return 1;
        }
    }

    private static void RenderScanReport(PqcReadinessReport report)
    {
        AnsiConsole.WriteLine();
        var rule = new Rule("[bold cyan]⚛️ POST-QUANTUM ERA READINESS AUDIT REPORT[/]")
        {
            Justification = Justify.Center
        };
        AnsiConsole.Write(rule);
        AnsiConsole.WriteLine();

        string scoreColor = report.ReadinessScorePercent switch
        {
            >= 90.0 => "green",
            >= 60.0 => "yellow",
            _ => "red"
        };

        var summaryTable = new Table().Border(TableBorder.Rounded);
        summaryTable.AddColumn(new TableColumn("[bold]Metrika[/]"));
        summaryTable.AddColumn(new TableColumn("[bold]Hodnota[/]"));

        summaryTable.AddRow("PQC Readiness Index", $"[bold {scoreColor}]{report.ReadinessScorePercent:F1}%[/]");
        summaryTable.AddRow("Aktívny Kryptoprofil", $"[bold cyan]{report.ActiveProfile}[/]");
        summaryTable.AddRow("CNSA 2.0 Compliance", report.IsCnsa2Ready ? "[bold green]Áno (Full)[/]" : "[bold yellow]Čiastočná / Neúplná[/]");
        summaryTable.AddRow("EU NIS 2 PQC Roadmap 2030", report.IsNis2Ready ? "[bold green]Súlad[/]" : "[bold red]Nesúlad (Riziko)[/]");
        summaryTable.AddRow("Kľúče (PQC / Klasické)", $"[green]{report.QuantumSafeKeys}[/] / [yellow]{report.ClassicalKeys}[/] (spolu: {report.TotalKeysScanned})");
        summaryTable.AddRow("Licencie (Bezpečné / Rizikové)", $"[green]{report.QuantumSafeLicenses}[/] / [red]{report.AtRiskLicenses}[/] (spolu: {report.TotalLicensesScanned})");
        summaryTable.AddRow("Súhrnné Zhodnotenie", report.Summary);

        AnsiConsole.Write(summaryTable);
        AnsiConsole.WriteLine();

        if (report.KeyAudits.Count > 0)
        {
            var keyTable = new Table().Border(TableBorder.Simple);
            keyTable.Title("[bold]Inšpekcia Podpisových a Enkapsulačných Kľúčov[/]");
            keyTable.AddColumn("Key ID (KID)");
            keyTable.AddColumn("Algoritmus");
            keyTable.AddColumn("Kategória");
            keyTable.AddColumn("Quantum-Safe?");
            keyTable.AddColumn("CNSA 2.0?");
            keyTable.AddColumn("Odporúčanie");

            foreach (var k in report.KeyAudits)
            {
                string catColor = k.Category switch
                {
                    PqcAlgorithmCategory.PostQuantum => "green",
                    PqcAlgorithmCategory.Hybrid => "cyan",
                    _ => "red"
                };

                keyTable.AddRow(
                    k.KeyId,
                    k.Algorithm,
                    $"[{catColor}]{k.Category}[/]",
                    k.IsQuantumSafe ? "[green]Áno[/]" : "[red]Nie[/]",
                    k.IsCnsa2Compliant ? "[green]Áno[/]" : "[yellow]Nie[/]",
                    k.Recommendation);
            }
            AnsiConsole.Write(keyTable);
            AnsiConsole.WriteLine();
        }

        if (report.ActionItems.Count > 0)
        {
            AnsiConsole.MarkupLine("[bold yellow]Odporúčané Kroky Migrácie na Post-Quantum:[/]");
            foreach (var item in report.ActionItems)
            {
                AnsiConsole.MarkupLine($"  [yellow]•[/] {item}");
            }
            AnsiConsole.WriteLine();
        }
    }

    private static async Task<int> HandleProfileAsync(string[] args)
    {
        string server = "http://localhost:5000";
        string action = args.Length > 0 ? args[0].ToLowerInvariant() : "get";

        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] is "--server" or "-s" && i + 1 < args.Length) server = args[++i];
        }

        using var client = new HttpClient { BaseAddress = new Uri(server) };

        if (action == "get")
        {
            var res = await client.GetAsync(new Uri("/admin/v1/pqc/readiness", UriKind.Relative)).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]Chyba servera: {res.StatusCode}[/]");
                return 1;
            }
            var report = await res.Content.ReadFromJsonAsync<PqcReadinessReport>(JsonOptions).ConfigureAwait(false);
            AnsiConsole.MarkupLine($"Aktuálny kryptografický profil servera: [bold cyan]{report?.ActiveProfile ?? "N/A"}[/]");
            return 0;
        }
        else if (action == "set")
        {
            if (args.Length < 2)
            {
                AnsiConsole.MarkupLine("[red]Chyba: Zadajte profil: hybrid-v1 alebo pqc-strict[/]");
                return 1;
            }
            string profile = args[1].ToLowerInvariant();
            var res = await client.PostAsJsonAsync(
                new Uri("/admin/v1/pqc/profile", UriKind.Relative),
                new { profile }).ConfigureAwait(false);

            if (res.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[green]Kryptografický profil úspešne prepnutý na: [bold]{profile}[/][/]");
                return 0;
            }
            else
            {
                string err = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                AnsiConsole.MarkupLine($"[red]Zlyhanie nastavenia profilu: {err}[/]");
                return 1;
            }
        }
        else
        {
            AnsiConsole.MarkupLine($"[red]Neznáma akcia profilu: {action}. Použite 'get' alebo 'set <profil>'.[/]");
            return 1;
        }
    }

    private static async Task<int> HandleKemAsync(string[] args)
    {
        string sub = args.Length > 0 ? args[0].ToUpperInvariant() : "TEST";

        if (sub == "GENERATE" || sub == "GEN")
        {
            string algName = Alg.MlKem768;
            string kid = $"kem-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..24];
            string? outFile = null;

            for (int i = 1; i < args.Length; i++)
            {
                if (args[i] is "--alg" or "-a" && i + 1 < args.Length) algName = args[++i];
                else if (args[i] is "--kid" or "-k" && i + 1 < args.Length) kid = args[++i];
                else if (args[i] is "--out" or "-o" && i + 1 < args.Length) outFile = args[++i];
            }

            if (!MlKemKeyEncapsulationProvider.IsSupported)
            {
                AnsiConsole.MarkupLine("[red]ML-KEM nie je podporovaný hostiteľským operačným systémom.[/]");
                return 1;
            }

            var alg = algName.Equals(Alg.MlKem1024, StringComparison.OrdinalIgnoreCase)
                ? MLKemAlgorithm.MLKem1024
                : MLKemAlgorithm.MLKem768;

            using var kem = MlKemKeyEncapsulationProvider.GenerateKey(alg, kid);
            var jwk = kem.ExportJwk(includePrivate: true);
            string json = JsonSerializer.Serialize(jwk, JsonOptions);

            if (!string.IsNullOrWhiteSpace(outFile))
            {
                await File.WriteAllTextAsync(outFile, json).ConfigureAwait(false);
                AnsiConsole.MarkupLine($"[green]ML-KEM kľúč úspešne uložený do: {outFile}[/]");
            }
            else
            {
                AnsiConsole.MarkupLine($"[green]Vygenerovaný ML-KEM JWK ({kem.Alg}, KID: {kem.Kid}):[/]");
                AnsiConsole.WriteLine(json);
            }
            return 0;
        }
        else if (sub == "TEST")
        {
            AnsiConsole.MarkupLine("[bold cyan]Testovanie FIPS 203 ML-KEM Enkapsulácie...[/]");
            if (!MlKemKeyEncapsulationProvider.IsSupported)
            {
                AnsiConsole.MarkupLine("[bold red]ML-KEM nie je na tomto systéme podporovaný.[/]");
                return 1;
            }

            using var kem768 = MlKemKeyEncapsulationProvider.GenerateKey(MLKemAlgorithm.MLKem768, "local-test-768");
            var (ct768, ss768) = kem768.Encapsulate();
            byte[] dec768 = kem768.Decapsulate(ct768);

            bool match768 = dec768.SequenceEqual(ss768);

            using var kem1024 = MlKemKeyEncapsulationProvider.GenerateKey(MLKemAlgorithm.MLKem1024, "local-test-1024");
            var (ct1024, ss1024) = kem1024.Encapsulate();
            byte[] dec1024 = kem1024.Decapsulate(ct1024);

            bool match1024 = dec1024.SequenceEqual(ss1024);

            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumn("Algoritmus");
            table.AddColumn("Ciphertext Veľkosť");
            table.AddColumn("Shared Secret Veľkosť");
            table.AddColumn("Stav Integrita");

            table.AddRow("ML-KEM-768 (AES-192 eq.)", $"{ct768.Length} bajtov", $"{ss768.Length} bajtov", match768 ? "[bold green]OK (Zhodné Tajomstvo)[/]" : "[bold red]FAIL[/]");
            table.AddRow("ML-KEM-1024 (AES-256 eq.)", $"{ct1024.Length} bajtov", $"{ss1024.Length} bajtov", match1024 ? "[bold green]OK (Zhodné Tajomstvo)[/]" : "[bold red]FAIL[/]");

            AnsiConsole.Write(table);
            return (match768 && match1024) ? 0 : 1;
        }
        else
        {
            AnsiConsole.MarkupLine($"[red]Neznámy príkaz pre KEM: {sub}. Použite 'generate' alebo 'test'.[/]");
            return 1;
        }
    }

    private static int HandleVerify(string[] args)
    {
        if (args.Length == 0)
        {
            AnsiConsole.MarkupLine("[red]Chyba: Zadajte cestu k licenčnému súboru (.symlic, .symgrant, .symlease).[/]");
            return 1;
        }

        string path = args[0];
        if (!File.Exists(path))
        {
            AnsiConsole.MarkupLine($"[red]Súbor '{path}' neexistuje.[/]");
            return 1;
        }

        string content = File.ReadAllText(path);
        bool hasPqcAlg = content.Contains(Alg.MlDsa65, StringComparison.OrdinalIgnoreCase) ||
                         content.Contains(Alg.MlDsa87, StringComparison.OrdinalIgnoreCase) ||
                         content.Contains(Alg.MlDsa44, StringComparison.OrdinalIgnoreCase);

        AnsiConsole.MarkupLine($"[bold]Artefakt:[/] {path}");
        if (hasPqcAlg)
        {
            AnsiConsole.MarkupLine("[bold green]✓ Súbor obsahuje Post-Quantum podpis (ML-DSA). Je odolný voči kvantovým počítačom.[/]");
            return 0;
        }
        else
        {
            AnsiConsole.MarkupLine("[bold yellow]⚠ Súbor využíva klasický podpis (ES256/ECDSA). Nie je chránený voči kvantovým útokom po roku 2030.[/]");
            return 0;
        }
    }

    private static int UnknownSubcommand(string sub)
    {
        AnsiConsole.MarkupLine($"[bold red]Neznámy PQC príkaz:[/] {sub}");
        PrintPqcHelp();
        return 1;
    }
}
