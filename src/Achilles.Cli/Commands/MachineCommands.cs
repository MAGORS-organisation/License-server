using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Spectre.Console;
using Achilles.Client;
using Achilles.Protocol;

namespace Achilles.Cli.Commands;

public static class MachineCommands
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task<int> HandleMachineAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintMachineHelp();
            return 0;
        }

        return args[0].ToUpperInvariant() switch
        {
            "FINGERPRINT" or "FP" or "INFO" => HandleFingerprint(args[1..]),
            "ACTIVATE" => await HandleActivateAsync(args[1..]).ConfigureAwait(false),
            "DEACTIVATE" => await HandleDeactivateAsync(args[1..]).ConfigureAwait(false),
            "TEST-MATCH" or "VERIFY-MATCH" or "MATCH" => HandleTestMatch(args[1..]),
            "LIST" => await HandleListAsync(args[1..]).ConfigureAwait(false),
            _ => UnknownSubcommand(args[0])
        };
    }

    private static int HandleFingerprint(string[] args)
    {
        string? licenseKey = null;
        bool jsonOutput = false;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] is "--license" or "-l" && i + 1 < args.Length)
            {
                licenseKey = args[++i];
            }
            else if (args[i] is "--json")
            {
                jsonOutput = true;
            }
        }

        bool isContainer = DeviceFingerprint.IsContainerOrCloud();
        var components = DeviceFingerprint.Collect(licenseKey);
        string hash = FingerprintHelper.ComputeHash(components);

        if (jsonOutput)
        {
            var dto = new
            {
                isContainer,
                summaryHash = hash,
                components
            };
            Console.WriteLine(JsonSerializer.Serialize(dto, JsonOptions));
            return 0;
        }

        AnsiConsole.MarkupLine("[bold blue]🔍 Symbolon Device Fingerprint Inspector (FPR-1 – FPR-14)[/]");
        AnsiConsole.WriteLine();

        if (isContainer)
        {
            AnsiConsole.MarkupLine("[yellow]⚠ UPOZORNENIE: Detegované kontajnerové / cloudové prostredie (FPR-10, FPR-11)![/]");
            AnsiConsole.MarkupLine("[grey]  Hardvérový scan bol vynechaný. Použité perzistentné UUID zväzku (FPR-12).[/]");
            AnsiConsole.MarkupLine("[grey]  Odporúčanie: Pre kontajnery použite floating licenciu s krátkym TTL namiesto node-locku (FPR-13).[/]");
            AnsiConsole.WriteLine();
        }

        var table = new Table();
        table.Border(TableBorder.Rounded);
        table.AddColumn("[bold]Komponent (Kód)[/]");
        table.AddColumn("[bold]Hodnota[/]");
        table.AddColumn("[bold]Normatívny Stav[/]");

        foreach (var (k, v) in components.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            string status = k switch
            {
                FingerprintComponentKeys.MachineId => "[green]Machine ID (FPR-1)[/]",
                FingerprintComponentKeys.Cpu => "[cyan]CPU ID (FPR-1)[/]",
                FingerprintComponentKeys.Board => "[blue]Motherboard (FPR-1)[/]",
                FingerprintComponentKeys.Disk => "[magenta]System Disk (FPR-1)[/]",
                FingerprintComponentKeys.Mac => "[yellow]Physical MAC (FPR-1)[/]",
                FingerprintComponentKeys.Host => licenseKey != null ? "[green]Pseudonymized (FPR-2)[/]" : "[grey]Raw Host (No Salt)[/]",
                _ => "[grey]Metadata[/]"
            };

            table.AddRow($"[bold]{Markup.Escape(k)}[/]", Markup.Escape(v), status);
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[bold]Súhrnný Hash (FPR-4):[/] [green]{hash}[/]");
        return 0;
    }

    private static async Task<int> HandleActivateAsync(string[] args)
    {
        if (args.Length == 0 || args[0].StartsWith('-'))
        {
            AnsiConsole.MarkupLine("[red]Chyba:[/] Chýba licenčný kľúč pre aktiváciu.");
            AnsiConsole.MarkupLine("Použitie: [yellow]symbolon machine activate <licenseKey> [--server <url>] [--machine-id <id>][/]");
            return 1;
        }

        string licenseKey = args[0];
        string serverUrl = "http://localhost:8080";
        string? machineId = null;

        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] is "--server" or "-s" && i + 1 < args.Length)
            {
                serverUrl = args[++i].TrimEnd('/');
            }
            else if (args[i] is "--machine-id" or "-m" && i + 1 < args.Length)
            {
                machineId = args[++i];
            }
        }

        var components = DeviceFingerprint.Collect(licenseKey).ToDictionary(kv => kv.Key, kv => kv.Value);

        using var http = new HttpClient { BaseAddress = new Uri(serverUrl) };
        var requestDto = new ActivationRequestDto
        {
            LicenseKey = licenseKey,
            FingerprintComponents = components,
            MachineId = machineId ?? Environment.MachineName
        };

        AnsiConsole.MarkupLine($"[grey]Odosielam požiadavku na aktiváciu uzla na[/] [blue]{serverUrl}/v1/activations[/]...");

        var response = await http.PostAsJsonAsync("/v1/activations", requestDto, AchillesProtocolJsonContext.Default.ActivationRequestDto).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            string err = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            AnsiConsole.MarkupLine($"[red]Zlyhanie aktivácie stroja (HTTP {(int)response.StatusCode}):[/]");
            AnsiConsole.WriteLine(err);
            return 1;
        }

        var act = await response.Content.ReadFromJsonAsync(AchillesProtocolJsonContext.Default.ActivationResponseDto).ConfigureAwait(false);
        if (act is null)
        {
            AnsiConsole.MarkupLine("[red]Chyba:[/] Prázdna odpoveď zo servera.");
            return 1;
        }

        AnsiConsole.MarkupLine("[bold green]✔ Stroj úspešne aktivovaný pre node-lock licenciu (FPR-15)![/]");
        var panel = new Panel(
            $"[bold]Activation ID:[/] [cyan]{act.ActivationId}[/]\n" +
            $"[bold]Licencia:[/]      [yellow]{act.LicenseId}[/]\n" +
            $"[bold]Fingerprint:[/]   [green]{act.Fingerprint}[/]\n" +
            $"[bold]Stav:[/]          [green]{act.State}[/]\n" +
            $"[bold]Aktivované:[/]    {act.ActivatedAt:yyyy-MM-dd HH:mm:ss} UTC"
        )
        {
            Header = new PanelHeader("[bold white]Node-Lock Machine Activation[/]"),
            Border = BoxBorder.Rounded
        };
        AnsiConsole.Write(panel);
        return 0;
    }

    private static async Task<int> HandleDeactivateAsync(string[] args)
    {
        if (args.Length == 0 || args[0].StartsWith('-'))
        {
            AnsiConsole.MarkupLine("[red]Chyba:[/] Chýba activationId stroja pre deaktiváciu.");
            AnsiConsole.MarkupLine("Použitie: [yellow]symbolon machine deactivate <activationId> [--server <url>][/]");
            return 1;
        }

        string activationId = args[0];
        string serverUrl = "http://localhost:8080";

        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] is "--server" or "-s" && i + 1 < args.Length)
            {
                serverUrl = args[++i].TrimEnd('/');
            }
        }

        using var http = new HttpClient { BaseAddress = new Uri(serverUrl) };
        var response = await http.DeleteAsync(new Uri($"/v1/activations/{Uri.EscapeDataString(activationId)}", UriKind.Relative)).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            string err = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            AnsiConsole.MarkupLine($"[red]Zlyhanie deaktivácie stroja (HTTP {(int)response.StatusCode}):[/]");
            AnsiConsole.WriteLine(err);
            return 1;
        }

        AnsiConsole.MarkupLine($"[bold green]✔ Stroj '{activationId}' úspešne deaktivovaný (FPR-15).[/]");
        return 0;
    }

    private static int HandleTestMatch(string[] args)
    {
        string? storedFile = null;
        string? currentFile = null;
        string strategy = MatchingStrategies.MatchMost;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] is "--stored" or "-s" && i + 1 < args.Length)
            {
                storedFile = args[++i];
            }
            else if (args[i] is "--current" or "-c" && i + 1 < args.Length)
            {
                currentFile = args[++i];
            }
            else if (args[i] is "--strategy" && i + 1 < args.Length)
            {
                strategy = args[++i];
            }
        }

        if (string.IsNullOrWhiteSpace(storedFile) || string.IsNullOrWhiteSpace(currentFile))
        {
            AnsiConsole.MarkupLine("[red]Chyba:[/] Vyžadujú sa oba súbory: --stored <file.json> a --current <file.json>.");
            return 1;
        }

        if (!File.Exists(storedFile) || !File.Exists(currentFile))
        {
            AnsiConsole.MarkupLine("[red]Chyba:[/] Jeden zo súborov neexistuje.");
            return 1;
        }

        var stored = ParseComponentsFile(storedFile);
        var current = ParseComponentsFile(currentFile);

        if (stored is null || current is null)
        {
            AnsiConsole.MarkupLine("[red]Chyba:[/] Neplatný JSON formát fingerprintu v jednom zo súborov.");
            return 1;
        }

        var eval = FingerprintMatchingEngine.EvaluateMatch(stored, current, strategy);

        AnsiConsole.MarkupLine("[bold blue]🔍 Simulátor zhodnosti fingerprintov (FPR-5 – FPR-9)[/]");
        AnsiConsole.MarkupLine($"Použitá stratégia: [yellow]{eval.StrategyUsed}[/]");
        AnsiConsole.MarkupLine($"Spoločných komponentov: [cyan]{eval.CommonComponentsCount}[/]");
        AnsiConsole.MarkupLine($"Zhodných komponentov:   [green]{eval.MatchedComponentsCount}[/] ([bold]{eval.MatchRatio:P1}[/])");
        AnsiConsole.WriteLine();

        if (eval.MatchedKeys.Count > 0)
        {
            AnsiConsole.MarkupLine($"[green]Zhodné kľúče:[/] {string.Join(", ", eval.MatchedKeys)}");
        }
        if (eval.MismatchedKeys.Count > 0)
        {
            AnsiConsole.MarkupLine($"[red]Nezhodné kľúče:[/] {string.Join(", ", eval.MismatchedKeys)}");
        }

        AnsiConsole.WriteLine();
        if (eval.IsMatch)
        {
            AnsiConsole.MarkupLine("[bold green]✔ ZHODA SCHVÁLENÁ: Stroj spĺňa podmienky viazania![/]");
            return 0;
        }
        else
        {
            AnsiConsole.MarkupLine($"[bold red]✘ ZHODA ZAMIETNUTÁ: {eval.FailureReason}[/]");
            return 2;
        }
    }

    private static async Task<int> HandleListAsync(string[] args)
    {
        if (args.Length == 0 || args[0].StartsWith('-'))
        {
            AnsiConsole.MarkupLine("[red]Chyba:[/] Chýba licenseId pre zoznam aktivovaných strojov.");
            AnsiConsole.MarkupLine("Použitie: [yellow]symbolon machine list <licenseId> [--server <url>][/]");
            return 1;
        }

        string licenseId = args[0];
        string serverUrl = "http://localhost:8080";

        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] is "--server" or "-s" && i + 1 < args.Length)
            {
                serverUrl = args[++i].TrimEnd('/');
            }
        }

        using var http = new HttpClient { BaseAddress = new Uri(serverUrl) };
        var response = await http.GetAsync(new Uri($"/admin/v1/licenses/{Uri.EscapeDataString(licenseId)}/activations", UriKind.Relative)).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            string err = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            AnsiConsole.MarkupLine($"[red]Zlyhanie načítania aktivácií (HTTP {(int)response.StatusCode}):[/] {err}");
            return 1;
        }

        string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        var table = new Table();
        table.Border(TableBorder.Rounded);
        table.AddColumn("[bold]Activation ID[/]");
        table.AddColumn("[bold]Fingerprint[/]");
        table.AddColumn("[bold]Stav[/]");
        table.AddColumn("[bold]Prvá aktivácia[/]");
        table.AddColumn("[bold]Posledný heartbeat[/]");

        foreach (var el in doc.RootElement.EnumerateArray())
        {
            string id = el.GetProperty("id").GetString() ?? "";
            string fp = el.GetProperty("fingerprint").GetString() ?? "";
            string state = el.GetProperty("state").GetString() ?? "";
            string firstSeen = el.GetProperty("firstSeen").GetString() ?? "";
            string lastHb = el.GetProperty("lastHeartbeat").GetString() ?? "";

            string stateMarkup = state == "active" ? "[green]active[/]" : "[grey]deactivated[/]";
            table.AddRow(id, fp.Length > 20 ? fp[..20] + "..." : fp, stateMarkup, firstSeen, lastHb);
        }

        AnsiConsole.MarkupLine($"[bold blue]📋 Aktivované stroje pre licenciu '{licenseId}':[/]");
        AnsiConsole.Write(table);
        return 0;
    }

    private static Dictionary<string, string>? ParseComponentsFile(string filePath)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(filePath));
            JsonElement root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("components", out var compElem) && compElem.ValueKind == JsonValueKind.Object)
            {
                root = compElem;
            }

            if (root.ValueKind != JsonValueKind.Object) return null;

            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in root.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    dict[prop.Name] = prop.Value.GetString() ?? string.Empty;
                }
                else if (prop.Value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
                {
                    dict[prop.Name] = prop.Value.ToString();
                }
            }
            return dict;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void PrintMachineHelp()
    {
        AnsiConsole.MarkupLine("[bold]Symbolon Machine & Node-Lock CLI (FPR-1 – FPR-19, §8)[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("Použitie: [yellow]symbolon machine <príkaz> [[možnosti]][/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("Príkazy:");
        AnsiConsole.MarkupLine("  [cyan]fingerprint, fp[/]     Zobrazí hardvérové komponenty stroja, kontajnerový status a súhrnný hash");
        AnsiConsole.MarkupLine("  [cyan]activate[/]            Aktivuje lokálny uzol/stroj pre node-locked licenciu (FPR-15)");
        AnsiConsole.MarkupLine("  [cyan]deactivate[/]          Deaktivuje stroj a uvoľní node-lock slot (FPR-15)");
        AnsiConsole.MarkupLine("  [cyan]test-match, match[/]   Simuluje fuzzy matching dvoch fingerprintov podľa stratégie (FPR-5 – FPR-9)");
        AnsiConsole.MarkupLine("  [cyan]list[/]                Vypíše zoznam registrovaných strojov pre licenciu");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("Možnosti:");
        AnsiConsole.MarkupLine("  --license, -l <key>   Licenčný kľúč pre pseudonymizáciu hostu (FPR-2)");
        AnsiConsole.MarkupLine("  --server, -s <url>    URL licenčného servera (predvolené http://localhost:8080)");
        AnsiConsole.MarkupLine("  --strategy <strat>    Stratégia: match-any, match-two, match-most (predvolené), match-all");
    }

    private static int UnknownSubcommand(string cmd)
    {
        AnsiConsole.MarkupLine($"[red]Neznámy podpríkaz pre machine:[/] {cmd}");
        PrintMachineHelp();
        return 1;
    }
}
