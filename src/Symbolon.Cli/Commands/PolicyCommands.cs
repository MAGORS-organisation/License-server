using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Spectre.Console;
using Symbolon.Protocol;

namespace Symbolon.Cli.Commands;

public static class PolicyCommands
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task<int> HandlePolicyAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintPolicyHelp();
            return 0;
        }

        string sub = args[0].ToUpperInvariant();
        if (sub == "RULES")
        {
            return await HandleRulesAsync(args[1..]).ConfigureAwait(false);
        }

        AnsiConsole.MarkupLine($"[red]Neznámy príkaz pre policy:[/] {args[0]}");
        PrintPolicyHelp();
        return 1;
    }

    private static async Task<int> HandleRulesAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintRulesHelp();
            return 0;
        }

        return args[0].ToUpperInvariant() switch
        {
            "GET" => await HandleGetRulesAsync(args[1..]).ConfigureAwait(false),
            "SET" => await HandleSetRulesAsync(args[1..]).ConfigureAwait(false),
            "TEST" or "SIMULATE" => await HandleTestRulesAsync(args[1..]).ConfigureAwait(false),
            _ => UnknownSubcommand(args[0])
        };
    }

    private static async Task<int> HandleGetRulesAsync(string[] args)
    {
        if (args.Length == 0 || args[0].StartsWith('-'))
        {
            AnsiConsole.MarkupLine("[red]Chýba license ID.[/] Použitie: symbolon policy rules get <license-id>");
            return 1;
        }

        string licenseId = args[0];
        string serverEndpoint = GetArg(args, "--server") ?? "http://localhost:8080";
        string? apiKey = GetArg(args, "--key") ?? GetArg(args, "--api-key") ?? Environment.GetEnvironmentVariable("SYMBOLON_ADMIN_KEY");
        bool rawYaml = HasFlag(args, "--yaml");
        bool jsonOutput = HasFlag(args, "--json");

        using var client = CreateClient(serverEndpoint, apiKey);

        try
        {
            var response = await client.GetAsync(new Uri($"/admin/v1/licenses/{Uri.EscapeDataString(licenseId)}/rules", UriKind.Relative)).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]Server vrátil chybu:[/] {response.StatusCode}");
                return 1;
            }

            var ruleSet = await response.Content.ReadFromJsonAsync(SymbolonProtocolJsonContext.Default.PolicyRuleSetDto).ConfigureAwait(false);
            if (ruleSet is null)
            {
                AnsiConsole.MarkupLine("[yellow]Pre zadanú licenciu neboli nájdené žiadne pravidlá.[/]");
                return 0;
            }

            if (rawYaml)
            {
                Console.WriteLine(ruleSet.RawYaml ?? "# Žiadne pravidlá nie sú definované.");
                return 0;
            }

            if (jsonOutput)
            {
                Console.WriteLine(JsonSerializer.Serialize(ruleSet, JsonOptions));
                return 0;
            }

            AnsiConsole.Write(new Rule($"[bold blue]Pravidlá Politiky pre Licenciu {licenseId}[/]").LeftJustified());

            if (ruleSet.Groups.Count > 0)
            {
                var groupTable = new Table().Border(TableBorder.Rounded);
                groupTable.AddColumn("Názov Skupiny");
                groupTable.AddColumn("Členovia (Používatelia)");
                groupTable.AddColumn("Stroje / Hostnamy");

                foreach (var g in ruleSet.Groups)
                {
                    string members = g.Members.Count > 0 ? string.Join(", ", g.Members) : "[grey]-[/]";
                    string hosts = g.Hosts is { Count: > 0 } ? string.Join(", ", g.Hosts) : "[grey]-[/]";
                    groupTable.AddRow($"[bold]{g.Name}[/]", members, hosts);
                }

                AnsiConsole.MarkupLine("\n[bold yellow]Definované Skupiny:[/]");
                AnsiConsole.Write(groupTable);
            }

            if (ruleSet.Rules.Count > 0)
            {
                var rulesTable = new Table().Border(TableBorder.Rounded);
                rulesTable.AddColumn("Typ");
                rulesTable.AddColumn("Hodnota");
                rulesTable.AddColumn("Cieľ (Target)");
                rulesTable.AddColumn("Modul (Feature)");
                rulesTable.AddColumn("Dôvod / Popis");

                foreach (var r in ruleSet.Rules)
                {
                    string typeBadge = r.Type.ToUpperInvariant() switch
                    {
                        "DENY" => "[red bold]DENY[/]",
                        "RESERVE" => "[green bold]RESERVE[/]",
                        "MAX" => "[yellow bold]MAX[/]",
                        "PRIORITY" => "[blue bold]PRIORITY[/]",
                        _ => r.Type
                    };

                    string val = r.Value.HasValue ? r.Value.Value.ToString(CultureInfo.InvariantCulture) : "[grey]-[/]";

                    var targetParts = new List<string>();
                    if (!string.IsNullOrEmpty(r.Group)) targetParts.Add($"group:{r.Group}");
                    if (!string.IsNullOrEmpty(r.User)) targetParts.Add($"user:{r.User}");
                    if (!string.IsNullOrEmpty(r.Subnet)) targetParts.Add($"subnet:{r.Subnet}");
                    if (!string.IsNullOrEmpty(r.Host)) targetParts.Add($"host:{r.Host}");
                    if (r.Hosts is { Count: > 0 }) targetParts.Add($"hosts:[{string.Join(", ", r.Hosts)}]");
                    if (r.Users is { Count: > 0 }) targetParts.Add($"users:[{string.Join(", ", r.Users)}]");
                    if (r.Groups is { Count: > 0 }) targetParts.Add($"groups:[{string.Join(", ", r.Groups)}]");
                    if (r.Subnets is { Count: > 0 }) targetParts.Add($"subnets:[{string.Join(", ", r.Subnets)}]");

                    string targetStr = targetParts.Count > 0 ? string.Join(" ", targetParts) : "[grey]*[/]";
                    string featureStr = !string.IsNullOrEmpty(r.Feature) ? $"[cyan]{r.Feature}[/]" : "[grey]*[/]";
                    string reasonStr = !string.IsNullOrEmpty(r.Reason) ? r.Reason : "[grey]-[/]";

                    rulesTable.AddRow(typeBadge, val, targetStr, featureStr, reasonStr);
                }

                AnsiConsole.MarkupLine("\n[bold yellow]Pravidlá (Vyhodnocované deny ➜ max ➜ reserve ➜ priority):[/]");
                AnsiConsole.Write(rulesTable);
            }
            else
            {
                AnsiConsole.MarkupLine("[yellow]Pre túto licenciu nie sú nakonfigurované žiadne pravidlá.[/]");
            }

            return 0;
        }
        catch (HttpRequestException ex)
        {
            AnsiConsole.MarkupLine($"[red]Chyba spojenia so serverom:[/] {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> HandleSetRulesAsync(string[] args)
    {
        if (args.Length == 0 || args[0].StartsWith('-'))
        {
            AnsiConsole.MarkupLine("[red]Chýba license ID.[/] Použitie: symbolon policy rules set <license-id> --file <options.yaml>");
            return 1;
        }

        string licenseId = args[0];
        string? filePath = GetArg(args, "--file");
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            AnsiConsole.MarkupLine($"[red]Súbor s pravidlami nebol nájdený:[/] {filePath}");
            return 1;
        }

        string serverEndpoint = GetArg(args, "--server") ?? "http://localhost:8080";
        string? apiKey = GetArg(args, "--key") ?? GetArg(args, "--api-key") ?? Environment.GetEnvironmentVariable("SYMBOLON_ADMIN_KEY");

        string fileContent = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
        using var client = CreateClient(serverEndpoint, apiKey);

        try
        {
            var req = new UpdatePolicyRulesRequestDto
            {
                RulesYaml = fileContent
            };

            var response = await client.PutAsJsonAsync(
                new Uri($"/admin/v1/licenses/{Uri.EscapeDataString(licenseId)}/rules", UriKind.Relative),
                req,
                SymbolonProtocolJsonContext.Default.UpdatePolicyRulesRequestDto).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                string err = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                AnsiConsole.MarkupLine($"[red]Chyba pri aktualizácii pravidiel ({response.StatusCode}):[/] {err}");
                return 1;
            }

            var updated = await response.Content.ReadFromJsonAsync(SymbolonProtocolJsonContext.Default.PolicyRuleSetDto).ConfigureAwait(false);
            AnsiConsole.MarkupLine($"[green]✓ Pravidlá pre licenciu {licenseId} boli úspešne uložené a sedadlá rematerializované![/]");
            AnsiConsole.MarkupLine($"[grey]Počet skupín:[/] {updated?.Groups.Count ?? 0}, [grey]počet pravidiel:[/] {updated?.Rules.Count ?? 0}");
            return 0;
        }
        catch (HttpRequestException ex)
        {
            AnsiConsole.MarkupLine($"[red]Chyba spojenia so serverom:[/] {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> HandleTestRulesAsync(string[] args)
    {
        if (args.Length == 0 || args[0].StartsWith('-'))
        {
            AnsiConsole.MarkupLine("[red]Chýba license ID.[/] Použitie: symbolon policy rules test <license-id> [--user <usr>] [--host <hst>] [--ip <ip>] [--feature <feat>]");
            return 1;
        }

        string licenseId = args[0];
        string serverEndpoint = GetArg(args, "--server") ?? "http://localhost:8080";
        string? apiKey = GetArg(args, "--key") ?? GetArg(args, "--api-key") ?? Environment.GetEnvironmentVariable("SYMBOLON_ADMIN_KEY");
        string? user = GetArg(args, "--user");
        string? host = GetArg(args, "--host") ?? GetArg(args, "--machine");
        string? ip = GetArg(args, "--ip");
        string? feature = GetArg(args, "--feature");

        using var client = CreateClient(serverEndpoint, apiKey);

        try
        {
            var req = new SimulateRuleEvaluationRequestDto
            {
                UserId = user,
                MachineId = host,
                HostName = host,
                ClientIp = ip,
                Features = !string.IsNullOrEmpty(feature) ? [feature] : null
            };

            var response = await client.PostAsJsonAsync(
                new Uri($"/admin/v1/licenses/{Uri.EscapeDataString(licenseId)}/rules/simulate", UriKind.Relative),
                req,
                SymbolonProtocolJsonContext.Default.SimulateRuleEvaluationRequestDto).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]Server vrátil chybu:[/] {response.StatusCode}");
                return 1;
            }

            var result = await response.Content.ReadFromJsonAsync(SymbolonProtocolJsonContext.Default.SimulateRuleEvaluationResponseDto).ConfigureAwait(false);
            if (result is null)
            {
                AnsiConsole.MarkupLine("[red]Neplatná odpoveď od servera.[/]");
                return 1;
            }

            AnsiConsole.Write(new Rule($"[bold blue]Výsledok Simulácie Pravidiel pre Licenciu {licenseId}[/]").LeftJustified());

            if (result.Allowed)
            {
                AnsiConsole.MarkupLine("[bold green]STAV: POVOLENÉ (ALLOWED)[/]");
                if (result.ResolvedPriority.HasValue)
                {
                    AnsiConsole.MarkupLine($"[cyan]Odvodená Priorita (Fronta):[/] {result.ResolvedPriority.Value}");
                }
                if (!string.IsNullOrEmpty(result.MatchedReservation))
                {
                    AnsiConsole.MarkupLine($"[green]Vyhradená Rezervácia (Reserved Pool):[/] {result.MatchedReservation}");
                }
                else
                {
                    AnsiConsole.MarkupLine("[grey]Fond: Všeobecný nealokovaný fond (Unreserved)[/]");
                }
            }
            else
            {
                AnsiConsole.MarkupLine($"[bold red]STAV: ZAMIETNUTÉ (DENIED) — {result.DenyType ?? "rule-denied"}[/]");
                AnsiConsole.MarkupLine($"[red]Dôvod:[/] {result.DenyReason ?? "Zamietnuté pravidlom politiky."}");
                if (result.MaxLimit.HasValue)
                {
                    AnsiConsole.MarkupLine($"[yellow]Dosiahnutý limit sedadiel:[/] {result.MaxLimit.Value}");
                }
            }

            return result.Allowed ? 0 : 2;
        }
        catch (HttpRequestException ex)
        {
            AnsiConsole.MarkupLine($"[red]Chyba spojenia so serverom:[/] {ex.Message}");
            return 1;
        }
    }

    private static HttpClient CreateClient(string serverEndpoint, string? apiKey)
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri(serverEndpoint)
        };
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        }
        return client;
    }

    private static string? GetArg(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }

    private static bool HasFlag(string[] args, string flag) =>
        args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));

    private static int UnknownSubcommand(string sub)
    {
        AnsiConsole.MarkupLine($"[red]Neznámy príkaz:[/] {sub}");
        PrintRulesHelp();
        return 1;
    }

    private static void PrintPolicyHelp()
    {
        AnsiConsole.MarkupLine("[bold]Správa Politík a Options Pravidiel (FLT-23, FLT-24, FLT-25)[/]");
        AnsiConsole.MarkupLine("\nPoužitie: [green]symbolon policy <príkaz> [možnosti][/]");
        AnsiConsole.MarkupLine("\nPríkazy:");
        AnsiConsole.MarkupLine("  [yellow]rules[/]       Správa pravidiel pre rezervácie, zákazy a limity");
    }

    private static void PrintRulesHelp()
    {
        AnsiConsole.MarkupLine("[bold]Správa Pravidiel Rezervácií a Zákazov (Options File Directives)[/]");
        AnsiConsole.MarkupLine("\nPoužitie: [green]symbolon policy rules <akcia> <license-id> [možnosti][/]");
        AnsiConsole.MarkupLine("\nAkcie:");
        AnsiConsole.MarkupLine("  [yellow]get[/]         Zobrazenie aktívnych pravidiel (tabuľka, --yaml, --json)");
        AnsiConsole.MarkupLine("  [yellow]set[/]         Nahratie nových pravidiel zo súboru (--file <options.yaml>)");
        AnsiConsole.MarkupLine("  [yellow]test[/]        Simulácia vyhodnotenia pravidiel (--user, --host, --ip, --feature)");
    }
}
