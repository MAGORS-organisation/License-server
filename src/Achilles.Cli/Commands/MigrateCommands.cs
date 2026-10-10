using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Spectre.Console;
using Achilles.Domain.Migration;

namespace Achilles.Cli.Commands;

public static class MigrateCommands
{
    private static readonly JsonSerializerOptions IndentedJsonOptions = new() { WriteIndented = true };

    public static async Task<int> HandleMigrateAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintMigrateHelp();
            return 0;
        }

        return args[0].ToUpperInvariant() switch
        {
            "FLEXNET" or "FLEXLM" => await HandleFlexNetAsync(args[1..]).ConfigureAwait(false),
            "OPT" or "OPTIONS" => HandleOptions(args[1..]),
            "LOG" or "ANALYZE-LOG" => HandleLogAnalysis(args[1..]),
            "KEYGEN" => await HandleKeygenAsync(args[1..]).ConfigureAwait(false),
            _ => UnknownSubcommand(args[0])
        };
    }

    private static async Task<int> HandleFlexNetAsync(string[] args)
    {
        string? licensePath = GetArg(args, "--license") ?? GetArg(args, "-l");
        string? optPath = GetArg(args, "--opt") ?? GetArg(args, "-o");
        string? outDir = GetArg(args, "--out");
        string serverEndpoint = GetArg(args, "--server") ?? "http://localhost:8080";
        bool apply = HasFlag(args, "--apply");

        if (string.IsNullOrWhiteSpace(licensePath))
        {
            AnsiConsole.MarkupLine("[red]Chyba: Chýba povinný argument --license <cesta_k_license.dat>.[/]");
            return 1;
        }

        if (!File.Exists(licensePath))
        {
            AnsiConsole.MarkupLine($"[red]Chyba: Súbor licencie '{licensePath}' neexistuje.[/]");
            return 1;
        }

        string licContent = await File.ReadAllTextAsync(licensePath).ConfigureAwait(false);
        var parsed = FlexNetLicenseParser.Parse(licContent);
        var converted = FlexNetLicenseParser.ConvertToSymbolonPlans(parsed);

        AnsiConsole.MarkupLine("[bold blue]=== Symbolon FlexNet License Transpiler ===[/]");

        if (parsed.Servers.Count > 0)
        {
            var srvTable = new Table().Border(TableBorder.Rounded);
            srvTable.AddColumn("Hostiteľ (Hostname)");
            srvTable.AddColumn("HostID (MAC/UUID)");
            srvTable.AddColumn("TCP Port");
            foreach (var s in parsed.Servers)
            {
                srvTable.AddRow(s.Hostname, s.HostId, s.Port?.ToString(CultureInfo.InvariantCulture) ?? "27000");
            }
            AnsiConsole.Write(srvTable);
        }

        var featTable = new Table().Border(TableBorder.Rounded);
        featTable.AddColumn("Produkt / Funkcia");
        featTable.AddColumn("Verzia");
        featTable.AddColumn("Sedadlá");
        featTable.AddColumn("Platnosť");
        featTable.AddColumn("HostID");

        foreach (var p in converted.ConvertedPlans)
        {
            string exp = p.IsPermanent ? "[green]Trvalá (Permanent)[/]" : (p.ExpiresAt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "—");
            string seats = p.TotalSeats >= 999999 ? "[cyan]Neobmedzené[/]" : p.TotalSeats.ToString(CultureInfo.InvariantCulture);
            featTable.AddRow($"[bold]{p.Code}[/]", p.Version, seats, exp, p.NodeLockHostId ?? "—");
        }
        AnsiConsole.Write(featTable);

        if (converted.Recommendations.Count > 0)
        {
            AnsiConsole.MarkupLine("[bold yellow]Odporúčania pre modernizáciu:[/]");
            foreach (var r in converted.Recommendations)
            {
                AnsiConsole.MarkupLine($"  [yellow]•[/] {r}");
            }
        }

        // Options file handling if supplied
        if (!string.IsNullOrWhiteSpace(optPath) && File.Exists(optPath))
        {
            string optContent = await File.ReadAllTextAsync(optPath).ConfigureAwait(false);
            var optReport = FlexNetOptionsTranspiler.Transpile(optContent);
            AnsiConsole.MarkupLine($"\n[bold green]✓ options.opt úspešne spracovaný: {optReport.TotalRulesParsed} pravidiel.[/]");

            if (outDir is not null)
            {
                Directory.CreateDirectory(outDir);
                string outPolicyFile = Path.Combine(outDir, "symbolon-policy.json");
                await File.WriteAllTextAsync(outPolicyFile, JsonSerializer.Serialize(optReport.Policy, IndentedJsonOptions)).ConfigureAwait(false);
                AnsiConsole.MarkupLine($"[green]✓ Vygenerovaná politika uložená do:[/] {outPolicyFile}");
            }
        }

        if (outDir is not null)
        {
            Directory.CreateDirectory(outDir);
            string outPlanFile = Path.Combine(outDir, "symbolon-converted-plans.json");
            await File.WriteAllTextAsync(outPlanFile, JsonSerializer.Serialize(converted.ConvertedPlans, IndentedJsonOptions)).ConfigureAwait(false);
            AnsiConsole.MarkupLine($"[green]✓ Návrh produktov a licencií uložený do:[/] {outPlanFile}");
        }

        if (apply)
        {
            AnsiConsole.MarkupLine($"\n[cyan]Odosielam na Symbolon Server ({serverEndpoint})...[/]");
            using var client = CreateClient(serverEndpoint);
            var body = new { content = licContent, apply = true };

            try
            {
                var res = await client.PostAsJsonAsync(new Uri("/admin/v1/migrate/flexnet/license", UriKind.Relative), body).ConfigureAwait(false);
                if (res.IsSuccessStatusCode)
                {
                    AnsiConsole.MarkupLine("[green]✓ Všetky licencie boli úspešne naimportované do Symbolon servera![/]");
                }
                else
                {
                    string err = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                    AnsiConsole.MarkupLine($"[red]Chyba importu na server:[/] {err}");
                    return 1;
                }
            }
            catch (HttpRequestException ex)
            {
                AnsiConsole.MarkupLine($"[red]Chyba spojenia so serverom:[/] {ex.Message}");
                return 1;
            }
        }

        return 0;
    }

    private static int HandleOptions(string[] args)
    {
        string? inPath = GetArg(args, "--in") ?? GetArg(args, "-i");
        string? outPath = GetArg(args, "--out") ?? GetArg(args, "-o");

        if (string.IsNullOrWhiteSpace(inPath))
        {
            AnsiConsole.MarkupLine("[red]Chyba: Chýba povinný argument --in <cesta_k_options.opt>.[/]");
            return 1;
        }

        if (!File.Exists(inPath))
        {
            AnsiConsole.MarkupLine($"[red]Chyba: Súbor opcií '{inPath}' neexistuje.[/]");
            return 1;
        }

        string optContent = File.ReadAllText(inPath);
        var report = FlexNetOptionsTranspiler.Transpile(optContent);

        AnsiConsole.MarkupLine("[bold blue]=== Symbolon FlexNet options.opt Transpiler ===[/]");
        AnsiConsole.MarkupLine($"Pravidiel: [bold green]{report.TotalRulesParsed}[/] | Smernice: [cyan]{string.Join(", ", report.RecognizedDirectives)}[/]");

        if (report.Policy.UserGroups.Count > 0)
        {
            var grpTable = new Table().Border(TableBorder.Rounded);
            grpTable.AddColumn("Skupina");
            grpTable.AddColumn("Členovia");
            foreach (var g in report.Policy.UserGroups)
            {
                grpTable.AddRow($"[bold]{g.Name}[/]", string.Join(", ", g.Members));
            }
            AnsiConsole.Write(grpTable);
        }

        if (report.Policy.SeatAllocations.Count > 0)
        {
            var allocTable = new Table().Border(TableBorder.Rounded);
            allocTable.AddColumn("Funkcia");
            allocTable.AddColumn("Typ");
            allocTable.AddColumn("Cieľ");
            allocTable.AddColumn("Sedadlá");
            foreach (var a in report.Policy.SeatAllocations)
            {
                string badge = a.AllocationType == "Reservation" ? "[green]Garantovaná Rezervácia[/]" : "[amber]Maximálny Strop (Limit)[/]";
                allocTable.AddRow(a.FeatureCode, badge, $"{a.TargetType}: {a.TargetName}", a.Seats.ToString(CultureInfo.InvariantCulture));
            }
            AnsiConsole.Write(allocTable);
        }

        string json = JsonSerializer.Serialize(report.Policy, IndentedJsonOptions);

        if (!string.IsNullOrWhiteSpace(outPath))
        {
            File.WriteAllText(outPath, json);
            AnsiConsole.MarkupLine($"[green]✓ Transpilovaná JSON politika uložená do:[/] {outPath}");
        }
        else
        {
            AnsiConsole.WriteLine();
            AnsiConsole.WriteLine(json);
        }

        return 0;
    }

    private static int HandleLogAnalysis(string[] args)
    {
        string? logPath = GetArg(args, "--file") ?? GetArg(args, "-f");

        if (string.IsNullOrWhiteSpace(logPath))
        {
            AnsiConsole.MarkupLine("[red]Chyba: Chýba povinný argument --file <cesta_k_lmgrd.log>.[/]");
            return 1;
        }

        if (!File.Exists(logPath))
        {
            AnsiConsole.MarkupLine($"[red]Chyba: Súbor logu '{logPath}' neexistuje.[/]");
            return 1;
        }

        string logContent = File.ReadAllText(logPath);
        var result = FlexNetLogAnalyzer.Analyze(logContent);

        AnsiConsole.MarkupLine("[bold blue]=== Symbolon lmgrd.log Analýza & Right-Sizing ===[/]");
        AnsiConsole.MarkupLine($"Udalostí: [bold green]{result.TotalEventsCount}[/] | Špička: [bold cyan]{result.OverallPeakConcurrency} sedadiel[/] | Odmietnutia (Denials): [bold red]{result.TotalDenialsAcrossAllFeatures}[/]");

        var statsTable = new Table().Border(TableBorder.Rounded);
        statsTable.AddColumn("Funkcia");
        statsTable.AddColumn("Špička");
        statsTable.AddColumn("Checkouts");
        statsTable.AddColumn("Odmietnutia");
        statsTable.AddColumn("Odporúčané Sedadlá");
        statsTable.AddColumn("Token Buffer");

        foreach (var f in result.FeatureStatistics)
        {
            string denBadge = f.TotalDenials > 0 ? $"[red]{f.TotalDenials}[/]" : "[green]0[/]";
            statsTable.AddRow(
                $"[bold]{f.FeatureCode}[/]",
                $"{f.PeakConcurrency} sedadiel",
                f.TotalCheckouts.ToString(CultureInfo.InvariantCulture),
                denBadge,
                $"[bold green]{f.RecommendedSeats}[/]",
                $"[yellow]+{f.RecommendedOverdraftBuffer}[/]");
        }
        AnsiConsole.Write(statsTable);

        if (result.KeyInsights.Count > 0)
        {
            AnsiConsole.MarkupLine("\n[bold cyan]Kľúčové Zistenia a Odporúčania pre Kapacitu:[/]");
            foreach (var insight in result.KeyInsights)
            {
                AnsiConsole.MarkupLine($"  [cyan]•[/] {insight}");
            }
        }

        return 0;
    }

    private static async Task<int> HandleKeygenAsync(string[] args)
    {
        string? jsonPath = GetArg(args, "--in") ?? GetArg(args, "-i");
        string serverEndpoint = GetArg(args, "--server") ?? "http://localhost:8080";
        bool apply = HasFlag(args, "--apply");

        if (string.IsNullOrWhiteSpace(jsonPath))
        {
            AnsiConsole.MarkupLine("[red]Chyba: Chýba povinný argument --in <cesta_k_keygen.json>.[/]");
            return 1;
        }

        if (!File.Exists(jsonPath))
        {
            AnsiConsole.MarkupLine($"[red]Chyba: Súbor '{jsonPath}' neexistuje.[/]");
            return 1;
        }

        string jsonContent = await File.ReadAllTextAsync(jsonPath).ConfigureAwait(false);
        var summary = KeygenImporter.Parse(jsonContent);

        AnsiConsole.MarkupLine("[bold blue]=== Symbolon Keygen.sh Importer ===[/]");
        AnsiConsole.MarkupLine($"Načítaných záznamov: [green]{summary.TotalRecordsRead}[/] (Politiky: {summary.Policies.Count}, Licencie: {summary.Licenses.Count}, Používatelia: {summary.Users.Count})");

        var licTable = new Table().Border(TableBorder.Rounded);
        licTable.AddColumn("Licenčný Kľúč");
        licTable.AddColumn("Názov");
        licTable.AddColumn("Sedadlá");
        licTable.AddColumn("Stav");
        licTable.AddColumn("Expirácia");

        foreach (var l in summary.Licenses)
        {
            string exp = l.ExpiresAt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "Trvalá";
            string st = l.Status == "ACTIVE" ? "[green]Aktívna[/]" : $"[red]{l.Status}[/]";
            licTable.AddRow(l.Key, l.Name, l.MaxSeats.ToString(CultureInfo.InvariantCulture), st, exp);
        }
        AnsiConsole.Write(licTable);

        if (apply)
        {
            AnsiConsole.MarkupLine($"\n[cyan]Importujem na Symbolon server ({serverEndpoint})...[/]");
            using var client = CreateClient(serverEndpoint);
            var body = new { content = jsonContent, apply = true };

            try
            {
                var res = await client.PostAsJsonAsync(new Uri("/admin/v1/migrate/keygen", UriKind.Relative), body).ConfigureAwait(false);
                if (res.IsSuccessStatusCode)
                {
                    AnsiConsole.MarkupLine("[green]✓ Keygen licencie boli úspešne importované do databázy![/]");
                }
                else
                {
                    string err = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                    AnsiConsole.MarkupLine($"[red]Chyba importu na server:[/] {err}");
                    return 1;
                }
            }
            catch (HttpRequestException ex)
            {
                AnsiConsole.MarkupLine($"[red]Chyba spojenia so serverom:[/] {ex.Message}");
                return 1;
            }
        }

        return 0;
    }

    private static HttpClient CreateClient(string baseUrl)
    {
        var client = new HttpClient { BaseAddress = new Uri(baseUrl) };
        string? apiKey = Environment.GetEnvironmentVariable("SYMBOLON_API_KEY");
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        }
        return client;
    }

    private static void PrintMigrateHelp()
    {
        AnsiConsole.MarkupLine("[bold cyan]Symbolon Podnikový Migračný Nástroj (FlexNet / Keygen)[/]");
        AnsiConsole.MarkupLine("Použitie: [green]symbolon migrate <príkaz> [[prepínače]][/]\n");
        AnsiConsole.MarkupLine("[bold]Dostupné príkazy:[/]");
        AnsiConsole.MarkupLine("  [green]flexnet[/]       Prevod license.dat a options.opt do formátu Symbolon");
        AnsiConsole.MarkupLine("  [green]options[/]       Transpilácia súboru options.opt na Symbolon JSON politiku");
        AnsiConsole.MarkupLine("  [green]log[/]           Hĺbková analýza lmgrd.log, špičiek a výpočet optimálnej kapacity");
        AnsiConsole.MarkupLine("  [green]keygen[/]        Dávkový import licencií a politík z Keygen.sh JSON exportu\n");
        AnsiConsole.MarkupLine("[bold]Príklady:[/]");
        AnsiConsole.MarkupLine("  symbolon migrate flexnet --license license.dat --opt options.opt --apply");
        AnsiConsole.MarkupLine("  symbolon migrate options --in options.opt --out policy.json");
        AnsiConsole.MarkupLine("  symbolon migrate log --file /var/log/lmgrd.log");
        AnsiConsole.MarkupLine("  symbolon migrate keygen --in export.json --apply\n");
    }

    private static string? GetArg(string[] args, string flag)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }
        return null;
    }

    private static bool HasFlag(string[] args, string flag)
    {
        return args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));
    }

    private static int UnknownSubcommand(string sub)
    {
        AnsiConsole.MarkupLine($"[red]Neznámy podpríkaz migrácie:[/] {sub}");
        PrintMigrateHelp();
        return 1;
    }
}
