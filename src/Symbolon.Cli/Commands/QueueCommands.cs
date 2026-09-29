using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Spectre.Console;
using Symbolon.Protocol;

namespace Symbolon.Cli.Commands;

public static class QueueCommands
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task<int> HandleQueueAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintQueueHelp();
            return 0;
        }

        return args[0].ToUpperInvariant() switch
        {
            "LIST" => await HandleListAsync(args[1..]).ConfigureAwait(false),
            "STATUS" => await HandleStatusAsync(args[1..]).ConfigureAwait(false),
            "CANCEL" => await HandleCancelAsync(args[1..]).ConfigureAwait(false),
            "PROMOTE" => await HandlePromoteAsync(args[1..]).ConfigureAwait(false),
            _ => UnknownSubcommand(args[0])
        };
    }

    private static async Task<int> HandleListAsync(string[] args)
    {
        string serverEndpoint = GetArg(args, "--server") ?? "http://localhost:8080";
        string? apiKey = GetArg(args, "--key") ?? GetArg(args, "--api-key") ?? Environment.GetEnvironmentVariable("SYMBOLON_ADMIN_KEY");
        string? licenseId = GetArg(args, "--license") ?? GetArg(args, "--license-id");
        string? status = GetArg(args, "--status");
        bool jsonOutput = HasFlag(args, "--json");

        using var client = CreateClient(serverEndpoint, apiKey);

        var queryParams = new List<string>();
        if (!string.IsNullOrWhiteSpace(licenseId)) queryParams.Add($"licenseId={Uri.EscapeDataString(licenseId)}");
        if (!string.IsNullOrWhiteSpace(status)) queryParams.Add($"status={Uri.EscapeDataString(status)}");
        string url = "/admin/v1/queue" + (queryParams.Count > 0 ? "?" + string.Join("&", queryParams) : string.Empty);

        try
        {
            var response = await client.GetAsync(new Uri(url, UriKind.Relative)).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]Server vrátil chybu:[/] {response.StatusCode}");
                return 1;
            }

            var items = await response.Content.ReadFromJsonAsync(SymbolonProtocolJsonContext.Default.ListQueueTicketItemDto).ConfigureAwait(false);
            if (items is null || items.Count == 0)
            {
                if (jsonOutput)
                {
                    Console.WriteLine("[]");
                }
                else
                {
                    AnsiConsole.MarkupLine("[yellow]V rade sa nenachádzajú žiadne čakajúce požiadavky.[/]");
                }
                return 0;
            }

            if (jsonOutput)
            {
                Console.WriteLine(JsonSerializer.Serialize(items, JsonOptions));
                return 0;
            }

            AnsiConsole.MarkupLine($"[bold cyan]=== Symbolon License Queue ({items.Count} záznamov) ===[/]");
            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumn("[bold]Pozícia[/]");
            table.AddColumn("[bold]Ticket[/]");
            table.AddColumn("[bold]Licencia[/]");
            table.AddColumn("[bold]Používateľ / Stroj[/]");
            table.AddColumn("[bold]Priorita[/]");
            table.AddColumn("[bold]Stav[/]");
            table.AddColumn("[bold]Vytvorené[/]");
            table.AddColumn("[bold]Expirácia[/]");

            foreach (var item in items)
            {
                string statusColor = item.Status switch
                {
                    "waiting" => "yellow",
                    "ready" => "green",
                    "cancelled" => "grey",
                    "expired" => "red",
                    _ => "white"
                };

                string posStr = item.Position > 0 ? $"#{item.Position}" : "-";
                string identity = item.UserId ?? item.MachineId ?? (item.Fingerprint.Length > 10 ? item.Fingerprint[..10] + "..." : item.Fingerprint);

                table.AddRow(
                    posStr,
                    item.Ticket,
                    item.LicenseId,
                    identity,
                    item.Priority.ToString(CultureInfo.InvariantCulture),
                    $"[{statusColor}]{item.Status}[/]",
                    item.CreatedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
                    item.ExpiresAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
                );
            }

            AnsiConsole.Write(table);
            return 0;
        }
        catch (HttpRequestException ex)
        {
            AnsiConsole.MarkupLine($"[red]Chyba pripojenia k serveru:[/] {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> HandleStatusAsync(string[] args)
    {
        if (args.Length == 0 || args[0].StartsWith('-'))
        {
            AnsiConsole.MarkupLine("[red]Chýba povinný parameter: ticket ID[/]");
            return 1;
        }

        string ticket = args[0];
        string serverEndpoint = GetArg(args, "--server") ?? "http://localhost:8080";
        bool jsonOutput = HasFlag(args, "--json");

        using var client = CreateClient(serverEndpoint, null);

        try
        {
            var response = await client.GetAsync(new Uri($"/v1/queue/{Uri.EscapeDataString(ticket)}", UriKind.Relative)).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]Ticket '{ticket}' nebol nájdený alebo server vrátil chybu:[/] {response.StatusCode}");
                return 1;
            }

            var statusDto = await response.Content.ReadFromJsonAsync(SymbolonProtocolJsonContext.Default.QueueStatusResponseDto).ConfigureAwait(false);
            if (statusDto is null)
            {
                AnsiConsole.MarkupLine("[red]Odpoveď servera je neplatná.[/]");
                return 1;
            }

            if (jsonOutput)
            {
                Console.WriteLine(JsonSerializer.Serialize(statusDto, JsonOptions));
                return 0;
            }

            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumn("[bold]Vlastnosť[/]");
            table.AddColumn("[bold]Hodnota[/]");

            table.AddRow("Ticket", statusDto.Ticket);
            table.AddRow("Stav", statusDto.Status);
            table.AddRow("Pozícia v rade", statusDto.Position > 0 ? $"#{statusDto.Position}" : "-");
            table.AddRow("Priorita", statusDto.Priority.HasValue ? statusDto.Priority.Value.ToString(CultureInfo.InvariantCulture) : "0");
            if (!string.IsNullOrWhiteSpace(statusDto.EstimatedWait))
                table.AddRow("Odhadované čakanie", statusDto.EstimatedWait);
            if (statusDto.RetryAfterSeconds.HasValue)
                table.AddRow("Retry-After", $"{statusDto.RetryAfterSeconds.Value} s");
            if (!string.IsNullOrWhiteSpace(statusDto.LeaseId))
                table.AddRow("Pridelený Lease ID", $"[green]{statusDto.LeaseId}[/]");
            if (statusDto.Seat.HasValue)
                table.AddRow("Pridelené sedadlo", $"[green]Seat #{statusDto.Seat.Value}[/]");
            if (statusDto.ExpiresAt.HasValue)
                table.AddRow("Platnosť do", statusDto.ExpiresAt.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));

            AnsiConsole.MarkupLine("[bold cyan]=== Stav čakacieho ticketu ===[/]");
            AnsiConsole.Write(table);
            return 0;
        }
        catch (HttpRequestException ex)
        {
            AnsiConsole.MarkupLine($"[red]Chyba pripojenia k serveru:[/] {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> HandleCancelAsync(string[] args)
    {
        if (args.Length == 0 || args[0].StartsWith('-'))
        {
            AnsiConsole.MarkupLine("[red]Chýba povinný parameter: ticket ID[/]");
            return 1;
        }

        string ticket = args[0];
        string serverEndpoint = GetArg(args, "--server") ?? "http://localhost:8080";
        string? apiKey = GetArg(args, "--key") ?? GetArg(args, "--api-key") ?? Environment.GetEnvironmentVariable("SYMBOLON_ADMIN_KEY");

        using var client = CreateClient(serverEndpoint, apiKey);

        try
        {
            // First try public cancel /v1/queue/{ticket}
            var response = await client.DeleteAsync(new Uri($"/v1/queue/{Uri.EscapeDataString(ticket)}", UriKind.Relative)).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                // Fall back to admin cancel
                response = await client.DeleteAsync(new Uri($"/admin/v1/queue/{Uri.EscapeDataString(ticket)}", UriKind.Relative)).ConfigureAwait(false);
            }

            if (response.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[green]Čakajúci ticket '{ticket}' bol úspešne zrušený.[/]");
                return 0;
            }

            AnsiConsole.MarkupLine($"[red]Nepodarilo sa zrušiť ticket '{ticket}': {response.StatusCode}[/]");
            return 1;
        }
        catch (HttpRequestException ex)
        {
            AnsiConsole.MarkupLine($"[red]Chyba pripojenia k serveru:[/] {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> HandlePromoteAsync(string[] args)
    {
        if (args.Length == 0 || args[0].StartsWith('-'))
        {
            AnsiConsole.MarkupLine("[red]Chýba povinný parameter: ticket ID[/]");
            return 1;
        }

        string ticket = args[0];
        string serverEndpoint = GetArg(args, "--server") ?? "http://localhost:8080";
        string? apiKey = GetArg(args, "--key") ?? GetArg(args, "--api-key") ?? Environment.GetEnvironmentVariable("SYMBOLON_ADMIN_KEY");

        using var client = CreateClient(serverEndpoint, apiKey);

        try
        {
            var response = await client.PostAsync(new Uri($"/admin/v1/queue/{Uri.EscapeDataString(ticket)}/promote", UriKind.Relative), null).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[green]Ticket '{ticket}' bol úspešne povýšený na aktívny lease![/]");
                return 0;
            }

            string err = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            AnsiConsole.MarkupLine($"[red]Povýšenie ticketu zlyhalo: {response.StatusCode}[/]\n{err}");
            return 1;
        }
        catch (HttpRequestException ex)
        {
            AnsiConsole.MarkupLine($"[red]Chyba pripojenia k serveru:[/] {ex.Message}");
            return 1;
        }
    }

    private static HttpClient CreateClient(string serverEndpoint, string? apiKey)
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri(serverEndpoint.TrimEnd('/') + "/")
        };
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        }
        return client;
    }

    private static void PrintQueueHelp()
    {
        Console.WriteLine("""
            Použitie: symbolon queue <subcommand> [options]

            Subcommands:
              list [--server <url>] [--key <admin_key>] [--license <id>] [--status <waiting|ready|...>] [--json]
                  Zoznam požiadaviek v rade na licenciu zo servera.
              status <ticket> [--server <url>] [--json]
                  Zistenie aktuálneho stavu a pozície čakacieho ticketu.
              cancel <ticket> [--server <url>] [--key <admin_key>]
                  Zrušenie čakacieho ticketu v rade.
              promote <ticket> [--server <url>] [--key <admin_key>]
                  Okamžité manuálne povýšenie čakacieho ticketu na aktívny lease (vyžaduje admin oprávnenie).
            """);
    }

    private static string? GetArg(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }
        return null;
    }

    private static bool HasFlag(string[] args, string flag)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static int UnknownSubcommand(string sub)
    {
        AnsiConsole.MarkupLine($"[red]Neznámy príkaz pre radu:[/] {sub}");
        PrintQueueHelp();
        return 1;
    }
}
