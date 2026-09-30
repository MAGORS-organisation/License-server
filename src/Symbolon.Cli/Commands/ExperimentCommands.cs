using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Spectre.Console;
using Symbolon.Domain.Experiments;

namespace Symbolon.Cli.Commands;

public static class ExperimentCommands
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static async Task<int> HandleExperimentAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintExperimentHelp();
            return 0;
        }

        return args[0].ToUpperInvariant() switch
        {
            "LIST" or "LS" => await HandleListAsync(args[1..]).ConfigureAwait(false),
            "GET" or "SHOW" => await HandleGetAsync(args[1..]).ConfigureAwait(false),
            "CREATE" or "NEW" => await HandleCreateAsync(args[1..]).ConfigureAwait(false),
            "START" => await HandleStartAsync(args[1..]).ConfigureAwait(false),
            "PAUSE" or "STOP" => await HandlePauseAsync(args[1..]).ConfigureAwait(false),
            "PROMOTE" => await HandlePromoteAsync(args[1..]).ConfigureAwait(false),
            "ROLLBACK" => await HandleRollbackAsync(args[1..]).ConfigureAwait(false),
            "REPORT" or "STATS" => await HandleReportAsync(args[1..]).ConfigureAwait(false),
            "SIMULATE" or "SIM" => await HandleSimulateAsync(args[1..]).ConfigureAwait(false),
            _ => UnknownSubcommand(args[0])
        };
    }

    private static void PrintExperimentHelp()
    {
        AnsiConsole.MarkupLine("[bold cyan]SYMBOLON A/B Testing & Experimentation Engine (AB-1 .. AB-15)[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]Použitie:[/] symbolon experiments <príkaz> [[voľby]]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Príkazy:[/] ");
        AnsiConsole.MarkupLine("  [green]list[/]                         Zoznam všetkých experimentov");
        AnsiConsole.MarkupLine("  [green]get <id>[/]                     Detail experimentu");
        AnsiConsole.MarkupLine("  [green]create[/]                       Vytvorí nový experiment");
        AnsiConsole.MarkupLine("  [green]start <id>[/]                   Spustí experiment do stavu Active");
        AnsiConsole.MarkupLine("  [green]pause <id>[/]                   Pozastaví experiment");
        AnsiConsole.MarkupLine("  [green]promote <id> --variant <v>[/]    Promuje vyhrávajúci variant na 100%");
        AnsiConsole.MarkupLine("  [green]rollback <id>[/]                Rollbackuje experiment (Circuit Breaker)");
        AnsiConsole.MarkupLine("  [green]report <id>[/]                  Zobrazí štatistický report (Z-score, CI, p-value)");
        AnsiConsole.MarkupLine("  [green]simulate <id>[/]                Simuluje bucketing pre N klientov");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Voľby:[/] ");
        AnsiConsole.MarkupLine("  [blue]--server <url>[/]             URL servera (predvolené: http://localhost:8080)");
        AnsiConsole.MarkupLine("  [blue]--api-key <key>[/]            Admin API kľúč");
        AnsiConsole.MarkupLine("  [blue]--tenant <tenantId>[/]        ID nájomcu");
    }

    private static async Task<int> HandleListAsync(string[] args)
    {
        string server = GetArg(args, "--server") ?? "http://localhost:8080";
        string? apiKey = GetArg(args, "--api-key");
        string? tenant = GetArg(args, "--tenant");

        using var client = CreateHttpClient(server, apiKey, tenant);
        var response = await client.GetAsync(new Uri("/admin/v1/experiments", UriKind.Relative)).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            AnsiConsole.MarkupLine($"[red]Chyba pri načítaní experimentov:[/] {response.StatusCode}");
            return 1;
        }

        var experiments = await response.Content.ReadFromJsonAsync<List<ExperimentItemDto>>(JsonOptions).ConfigureAwait(false);
        if (experiments is null || experiments.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]Žiadne experimenty neboli nájdené.[/]");
            return 0;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("[bold]ID[/]");
        table.AddColumn("[bold]Názov[/]");
        table.AddColumn("[bold]Stav[/]");
        table.AddColumn("[bold]Alokácia[/]");
        table.AddColumn("[bold]Varianty[/]");
        table.AddColumn("[bold]Vytvorené[/]");

        foreach (var exp in experiments)
        {
            string statusColor = exp.Status switch
            {
                "Active" => "green",
                "Draft" => "yellow",
                "Paused" => "blue",
                "Completed" => "cyan",
                "RolledBack" => "red",
                _ => "grey"
            };

            table.AddRow(
                $"[bold]{exp.Id}[/]",
                exp.Name,
                $"[{statusColor}]{exp.Status}[/]",
                $"{exp.TrafficAllocation}%",
                exp.Variants.Count.ToString(CultureInfo.InvariantCulture),
                exp.CreatedAt.ToString("g", CultureInfo.InvariantCulture));
        }

        AnsiConsole.Write(table);
        return 0;
    }

    private static async Task<int> HandleGetAsync(string[] args)
    {
        if (args.Length == 0 || args[0].StartsWith('-'))
        {
            AnsiConsole.MarkupLine("[red]Chýba ID experimentu.[/] Použitie: symbolon experiments get <id>");
            return 1;
        }

        string id = args[0];
        string server = GetArg(args, "--server") ?? "http://localhost:8080";
        string? apiKey = GetArg(args, "--api-key");

        using var client = CreateHttpClient(server, apiKey);
        var response = await client.GetAsync(new Uri($"/admin/v1/experiments/{Uri.EscapeDataString(id)}", UriKind.Relative)).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            AnsiConsole.MarkupLine($"[red]Experiment '{id}' nebol nájdený:[/] {response.StatusCode}");
            return 1;
        }

        var exp = await response.Content.ReadFromJsonAsync<ExperimentItemDto>(JsonOptions).ConfigureAwait(false);
        if (exp is null) return 1;

        string descText = exp.Description ?? "-";
        string promotedText = exp.PromotedVariantId ?? "-";
        string startedText = exp.StartedAt?.ToString("g", CultureInfo.InvariantCulture) ?? "-";

        var panel = new Panel(
            $"[bold]Názov:[/] {exp.Name}\n" +
            $"[bold]Popis:[/] {descText}\n" +
            $"[bold]Stav:[/] {exp.Status}\n" +
            $"[bold]Salt:[/] {exp.Salt}\n" +
            $"[bold]Alokácia prevádzky:[/] {exp.TrafficAllocation}%\n" +
            $"[bold]Počet variantov:[/] {exp.Variants.Count}\n" +
            $"[bold]Promovaný variant:[/] {promotedText}\n" +
            $"[bold]Vytvorené:[/] {exp.CreatedAt:g}\n" +
            $"[bold]Spustené:[/] {startedText}")
        {
            Header = new PanelHeader($"[bold cyan]Experiment: {exp.Id}[/]"),
            Border = BoxBorder.Rounded
        };

        AnsiConsole.Write(panel);
        return 0;
    }

    private static async Task<int> HandleCreateAsync(string[] args)
    {
        string? id = GetArg(args, "--id");
        string? name = GetArg(args, "--name");
        string? desc = GetArg(args, "--desc") ?? GetArg(args, "--description");
        string server = GetArg(args, "--server") ?? "http://localhost:8080";
        string? apiKey = GetArg(args, "--api-key");
        string? tenant = GetArg(args, "--tenant");
        int allocation = 100;

        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
        {
            AnsiConsole.MarkupLine("[red]Chýbajú povinné parametre.[/] Použitie: symbolon experiments create --id <id> --name <name>");
            return 1;
        }

        if (int.TryParse(GetArg(args, "--allocation"), CultureInfo.InvariantCulture, out int allocVal))
        {
            allocation = allocVal;
        }

        var payload = new
        {
            Id = id,
            Name = name,
            Description = desc,
            TrafficAllocation = allocation,
            Variants = new[]
            {
                new { VariantId = "ctrl", Name = "Control", Weight = 50, IsControl = true },
                new { VariantId = "treat", Name = "Treatment", Weight = 50, IsControl = false }
            }
        };

        using var client = CreateHttpClient(server, apiKey, tenant);
        var response = await client.PostAsJsonAsync(new Uri("/admin/v1/experiments", UriKind.Relative), payload).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            AnsiConsole.MarkupLine($"[red]Chyba pri vytváraní experimentu:[/] {response.StatusCode}");
            return 1;
        }

        AnsiConsole.MarkupLine($"[green]Experiment '{id}' bol úspešne vytvorený v stave Draft.[/]");
        return 0;
    }

    private static async Task<int> HandleStartAsync(string[] args)
    {
        if (args.Length == 0 || args[0].StartsWith('-'))
        {
            AnsiConsole.MarkupLine("[red]Chýba ID experimentu.[/] Použitie: symbolon experiments start <id>");
            return 1;
        }

        string id = args[0];
        string server = GetArg(args, "--server") ?? "http://localhost:8080";
        string? apiKey = GetArg(args, "--api-key");

        using var client = CreateHttpClient(server, apiKey);
        var response = await client.PostAsync(new Uri($"/admin/v1/experiments/{Uri.EscapeDataString(id)}/start", UriKind.Relative), null).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            AnsiConsole.MarkupLine($"[red]Chyba pri spúšťaní experimentu:[/] {response.StatusCode}");
            return 1;
        }

        AnsiConsole.MarkupLine($"[green]Experiment '{id}' bol úspešne spustený (Active).[/]");
        return 0;
    }

    private static async Task<int> HandlePauseAsync(string[] args)
    {
        if (args.Length == 0 || args[0].StartsWith('-'))
        {
            AnsiConsole.MarkupLine("[red]Chýba ID experimentu.[/] Použitie: symbolon experiments pause <id>");
            return 1;
        }

        string id = args[0];
        string server = GetArg(args, "--server") ?? "http://localhost:8080";
        string? apiKey = GetArg(args, "--api-key");

        using var client = CreateHttpClient(server, apiKey);
        var response = await client.PostAsync(new Uri($"/admin/v1/experiments/{Uri.EscapeDataString(id)}/pause", UriKind.Relative), null).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            AnsiConsole.MarkupLine($"[red]Chyba pri pozastavovaní experimentu:[/] {response.StatusCode}");
            return 1;
        }

        AnsiConsole.MarkupLine($"[yellow]Experiment '{id}' bol pozastavený (Paused).[/]");
        return 0;
    }

    private static async Task<int> HandlePromoteAsync(string[] args)
    {
        if (args.Length == 0 || args[0].StartsWith('-'))
        {
            AnsiConsole.MarkupLine("[red]Chýba ID experimentu.[/] Použitie: symbolon experiments promote <id> --variant <variantId>");
            return 1;
        }

        string id = args[0];
        string? variantId = GetArg(args, "--variant") ?? GetArg(args, "-v");
        if (string.IsNullOrWhiteSpace(variantId))
        {
            AnsiConsole.MarkupLine("[red]Chýba parameter --variant.[/]");
            return 1;
        }

        string server = GetArg(args, "--server") ?? "http://localhost:8080";
        string? apiKey = GetArg(args, "--api-key");

        using var client = CreateHttpClient(server, apiKey);
        var response = await client.PostAsync(new Uri($"/admin/v1/experiments/{Uri.EscapeDataString(id)}/promote/{Uri.EscapeDataString(variantId)}", UriKind.Relative), null).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            AnsiConsole.MarkupLine($"[red]Chyba pri promovaní variantu:[/] {response.StatusCode}");
            return 1;
        }

        AnsiConsole.MarkupLine($"[green]Variant '{variantId}' bol promovaný na 100% v experimente '{id}'. Experiment je Completed.[/]");
        return 0;
    }

    private static async Task<int> HandleRollbackAsync(string[] args)
    {
        if (args.Length == 0 || args[0].StartsWith('-'))
        {
            AnsiConsole.MarkupLine("[red]Chýba ID experimentu.[/] Použitie: symbolon experiments rollback <id>");
            return 1;
        }

        string id = args[0];
        string server = GetArg(args, "--server") ?? "http://localhost:8080";
        string? apiKey = GetArg(args, "--api-key");

        using var client = CreateHttpClient(server, apiKey);
        var response = await client.PostAsync(new Uri($"/admin/v1/experiments/{Uri.EscapeDataString(id)}/rollback", UriKind.Relative), null).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            AnsiConsole.MarkupLine($"[red]Chyba pri rollbacu experimentu:[/] {response.StatusCode}");
            return 1;
        }

        AnsiConsole.MarkupLine($"[red]Experiment '{id}' bol vrátený späť (RolledBack).[/]");
        return 0;
    }

    private static async Task<int> HandleReportAsync(string[] args)
    {
        if (args.Length == 0 || args[0].StartsWith('-'))
        {
            AnsiConsole.MarkupLine("[red]Chýba ID experimentu.[/] Použitie: symbolon experiments report <id>");
            return 1;
        }

        string id = args[0];
        string server = GetArg(args, "--server") ?? "http://localhost:8080";
        string? apiKey = GetArg(args, "--api-key");

        using var client = CreateHttpClient(server, apiKey);
        var response = await client.GetAsync(new Uri($"/admin/v1/experiments/{Uri.EscapeDataString(id)}/report", UriKind.Relative)).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            AnsiConsole.MarkupLine($"[red]Chyba pri načítaní reportu:[/] {response.StatusCode}");
            return 1;
        }

        var report = await response.Content.ReadFromJsonAsync<ExperimentStatisticalReport>(JsonOptions).ConfigureAwait(false);
        if (report is null) return 1;

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("[bold]Metrika[/]");
        table.AddColumn($"[bold]Control ({report.ControlVariantId})[/]");
        table.AddColumn($"[bold]Treatment ({report.TreatmentVariantId})[/]");
        table.AddColumn("[bold]Rozdiel / Štatistika[/]");

        table.AddRow(
            "Počet požiadaviek",
            report.ControlMetrics.TotalRequests.ToString(CultureInfo.InvariantCulture),
            report.TreatmentMetrics.TotalRequests.ToString(CultureInfo.InvariantCulture),
            $"Rozdiel: {report.TreatmentMetrics.TotalRequests - report.ControlMetrics.TotalRequests}");

        table.AddRow(
            "Úspešnosť checkoutov",
            $"{report.ControlMetrics.SuccessRate:P2}",
            $"{report.TreatmentMetrics.SuccessRate:P2}",
            $"Z-Score: {report.ZScore:F3} (p = {report.PValue:F4})");

        table.AddRow(
            "Chybovosť",
            $"{report.ControlMetrics.ErrorRate:P2}",
            $"{report.TreatmentMetrics.ErrorRate:P2}",
            $"CI: [[{report.ConfidenceIntervalLower:P2} .. {report.ConfidenceIntervalUpper:P2}]]");

        table.AddRow(
            "Priemerná latencia",
            $"{report.ControlMetrics.AverageLatencyMs:F2} ms",
            $"{report.TreatmentMetrics.AverageLatencyMs:F2} ms",
            $"t = {report.LatencyTScore:F3} (p = {report.LatencyPValue:F4})");

        AnsiConsole.Write(table);

        string recColor = report.IsStatisticallySignificant ? "green" : "yellow";
        var panel = new Panel($"[{recColor}]{report.Recommendation}[/]")
        {
            Header = new PanelHeader("[bold]Odporúčanie systému[/]"),
            Border = BoxBorder.Rounded
        };
        AnsiConsole.Write(panel);

        return 0;
    }

    private static async Task<int> HandleSimulateAsync(string[] args)
    {
        if (args.Length == 0 || args[0].StartsWith('-'))
        {
            AnsiConsole.MarkupLine("[red]Chýba ID experimentu.[/] Použitie: symbolon experiments simulate <id> [[--clients <n>]]");
            return 1;
        }

        string id = args[0];
        int clients = 1000;
        if (int.TryParse(GetArg(args, "--clients"), CultureInfo.InvariantCulture, out int cVal))
        {
            clients = cVal;
        }

        string server = GetArg(args, "--server") ?? "http://localhost:8080";
        string? apiKey = GetArg(args, "--api-key");
        string? tenant = GetArg(args, "--tenant");

        var payload = new
        {
            ClientCount = clients,
            TenantId = tenant
        };

        using var client = CreateHttpClient(server, apiKey, tenant);
        var response = await client.PostAsJsonAsync(new Uri($"/admin/v1/experiments/{Uri.EscapeDataString(id)}/simulate", UriKind.Relative), payload).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            AnsiConsole.MarkupLine($"[red]Chyba pri simulácii:[/] {response.StatusCode}");
            return 1;
        }

        var result = await response.Content.ReadFromJsonAsync<SimulateExperimentResultDto>(JsonOptions).ConfigureAwait(false);
        if (result is null) return 1;

        AnsiConsole.MarkupLine($"[bold cyan]Výsledky simulácie pre experiment '{id}' ({result.TotalSimulated} klientov):[/]");
        AnsiConsole.MarkupLine($"  [bold]V experimente:[/] {result.TotalInExperiment} ({(double)result.TotalInExperiment / result.TotalSimulated:P1})");
        AnsiConsole.MarkupLine($"  [bold]Baseline:[/] {result.TotalBaseline} ({(double)result.TotalBaseline / result.TotalSimulated:P1})");

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("[bold]Variant[/]");
        table.AddColumn("[bold]Počet klientov[/]");
        table.AddColumn("[bold]Podiel[/]");

        foreach (var (variant, count) in result.VariantCounts)
        {
            double pct = result.TotalSimulated > 0 ? (double)count / result.TotalSimulated : 0;
            table.AddRow(variant, count.ToString(CultureInfo.InvariantCulture), $"{pct:P2}");
        }

        AnsiConsole.Write(table);
        return 0;
    }

    private static HttpClient CreateHttpClient(string serverUrl, string? apiKey, string? tenant = null)
    {
        var client = new HttpClient { BaseAddress = new Uri(serverUrl) };
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        }
        if (!string.IsNullOrWhiteSpace(tenant))
        {
            client.DefaultRequestHeaders.Add("X-Tenant-Id", tenant);
        }
        return client;
    }

    private static string? GetArg(string[] args, string flag)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                return args[i + 1];
            }
        }
        return null;
    }

    private static int UnknownSubcommand(string sub)
    {
        AnsiConsole.MarkupLine($"[red]Neznámy príkaz:[/] {sub}");
        PrintExperimentHelp();
        return 1;
    }

    private sealed record ExperimentItemDto(
        string Id,
        string? TenantId,
        string Name,
        string? Description,
        string Status,
        string Salt,
        int TrafficAllocation,
        IReadOnlyList<ExperimentVariant> Variants,
        string? PromotedVariantId,
        DateTimeOffset CreatedAt,
        DateTimeOffset? StartedAt,
        DateTimeOffset? EndedAt);

    private sealed record SimulateExperimentResultDto(
        string ExperimentId,
        int TotalSimulated,
        int TotalInExperiment,
        int TotalBaseline,
        IReadOnlyDictionary<string, int> VariantCounts);
}
