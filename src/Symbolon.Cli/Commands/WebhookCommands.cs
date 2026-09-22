using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Spectre.Console;

namespace Symbolon.Cli.Commands;

public static class WebhookCommands
{
    public static async Task<int> HandleWebhooksAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintWebhooksHelp();
            return 0;
        }

        return args[0].ToUpperInvariant() switch
        {
            "LIST" => await HandleListAsync(args[1..]).ConfigureAwait(false),
            "CREATE" => await HandleCreateAsync(args[1..]).ConfigureAwait(false),
            "DELETE" => await HandleDeleteAsync(args[1..]).ConfigureAwait(false),
            "TEST" => await HandleTestAsync(args[1..]).ConfigureAwait(false),
            "DELIVERIES" or "LOG" => await HandleDeliveriesAsync(args[1..]).ConfigureAwait(false),
            "REPLAY" => await HandleReplayAsync(args[1..]).ConfigureAwait(false),
            "LIFECYCLE" or "EXPIRING" => await HandleLifecycleAsync(args[1..]).ConfigureAwait(false),
            _ => UnknownSubcommand(args[0])
        };
    }

    private static async Task<int> HandleListAsync(string[] args)
    {
        string serverEndpoint = GetArg(args, "--server") ?? "http://localhost:8080";
        string? tenantId = GetArg(args, "--tenant");

        AnsiConsole.MarkupLine("[bold blue]=== Symbolon Webhook Odberatelia (Outbound Subscriptions) ===[/]");

        using var client = CreateClient(serverEndpoint);
        string url = string.IsNullOrWhiteSpace(tenantId) ? "/admin/v1/webhooks" : $"/admin/v1/webhooks?tenantId={tenantId}";

        try
        {
            var response = await client.GetAsync(new Uri(url, UriKind.Relative)).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]Server vrátil kód: {response.StatusCode}[/]");
                return 1;
            }

            var webhooks = await response.Content.ReadFromJsonAsync<List<WebhookSubCliDto>>().ConfigureAwait(false) ?? [];

            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumn("[cyan]ID[/]");
            table.AddColumn("[white]Názov[/]");
            table.AddColumn("[yellow]Formát[/]");
            table.AddColumn("[grey]URL Cieľ[/]");
            table.AddColumn("[blue]Odoberané Udalosti[/]");
            table.AddColumn("[green]Stav[/]");

            foreach (var w in webhooks)
            {
                string statusColor = w.IsActive ? "green" : "red";
                string statusText = w.IsActive ? "Aktívny" : "Pozastavený";
                string eventsSummary = string.Join(", ", w.Events);

                table.AddRow(
                    $"[bold]{w.Id}[/]",
                    Markup.Escape(w.Name ?? "N/A"),
                    $"[magenta]{Markup.Escape(w.Format ?? "JSON")}[/]",
                    Markup.Escape(w.Url),
                    Markup.Escape(eventsSummary),
                    $"[{statusColor}]{statusText}[/]");
            }

            AnsiConsole.Write(table);
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Chyba pripojenia k serveru: {Markup.Escape(ex.Message)}[/]");
            return 1;
        }
    }

    private static async Task<int> HandleCreateAsync(string[] args)
    {
        string serverEndpoint = GetArg(args, "--server") ?? "http://localhost:8080";
        string? url = GetArg(args, "--url");
        string? eventsArg = GetArg(args, "--events");
        string? name = GetArg(args, "--name");
        string? format = GetArg(args, "--format") ?? "json";
        string? secret = GetArg(args, "--secret");

        if (string.IsNullOrWhiteSpace(url))
        {
            if (Console.IsInputRedirected)
            {
                AnsiConsole.MarkupLine("[red]Chyba: Chýba povinný parameter --url.[/]");
                return 1;
            }
            url = AnsiConsole.Ask<string>("Zadajte cieľovú [green]URL[/] webhooku:");
        }

        var events = string.IsNullOrWhiteSpace(eventsArg)
            ? ["*"]
            : eventsArg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        using var client = CreateClient(serverEndpoint);
        var dto = new
        {
            url,
            events,
            secret,
            name = string.IsNullOrWhiteSpace(name) ? url : name,
            format = format.ToUpperInvariant()
        };

        try
        {
            var response = await client.PostAsJsonAsync(new Uri("/admin/v1/webhooks", UriKind.Relative), dto).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                string err = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                AnsiConsole.MarkupLine($"[red]Vytvorenie zlyhalo ({response.StatusCode}): {Markup.Escape(err)}[/]");
                return 1;
            }

            var created = await response.Content.ReadFromJsonAsync<WebhookSubCliDto>().ConfigureAwait(false);
            AnsiConsole.MarkupLine($"[green]✓ Webhook úspešne vytvorený: ID={created?.Id}, URL={Markup.Escape(created?.Url ?? url)}[/]");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Chyba: {Markup.Escape(ex.Message)}[/]");
            return 1;
        }
    }

    private static async Task<int> HandleDeleteAsync(string[] args)
    {
        string serverEndpoint = GetArg(args, "--server") ?? "http://localhost:8080";
        string? id = GetArg(args, "--id") ?? (args.Length > 0 && !args[0].StartsWith('-') ? args[0] : null);

        if (string.IsNullOrWhiteSpace(id))
        {
            AnsiConsole.MarkupLine("[red]Chyba: Chýba parameter --id <webhookId>.[/]");
            return 1;
        }

        using var client = CreateClient(serverEndpoint);
        try
        {
            var response = await client.DeleteAsync(new Uri($"/admin/v1/webhooks/{id}", UriKind.Relative)).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[green]✓ Webhook {Markup.Escape(id)} bol úspešne zmazaný.[/]");
                return 0;
            }

            AnsiConsole.MarkupLine($"[red]Zmazanie zlyhalo s kódom {response.StatusCode}.[/]");
            return 1;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Chyba: {Markup.Escape(ex.Message)}[/]");
            return 1;
        }
    }

    private static async Task<int> HandleTestAsync(string[] args)
    {
        string serverEndpoint = GetArg(args, "--server") ?? "http://localhost:8080";
        string? id = GetArg(args, "--id") ?? (args.Length > 0 && !args[0].StartsWith('-') ? args[0] : null);

        if (string.IsNullOrWhiteSpace(id))
        {
            AnsiConsole.MarkupLine("[red]Chyba: Chýba parameter --id <webhookId>.[/]");
            return 1;
        }

        AnsiConsole.MarkupLine($"[blue]Odosielam testovací ping na webhook {Markup.Escape(id)}...[/]");
        using var client = CreateClient(serverEndpoint);

        try
        {
            var response = await client.PostAsync(new Uri($"/admin/v1/webhooks/{id}/test", UriKind.Relative), null).ConfigureAwait(false);
            var result = await response.Content.ReadFromJsonAsync<WebhookTestCliDto>().ConfigureAwait(false);

            if (result?.Success == true)
            {
                AnsiConsole.MarkupLine($"[green]✓ Test úspešný! HTTP kód: {result.StatusCode}, odozva: {result.ElapsedMilliseconds} ms[/]");
                if (!string.IsNullOrWhiteSpace(result.ResponseBody))
                {
                    AnsiConsole.MarkupLine($"[grey]Odpoveď servera: {Markup.Escape(result.ResponseBody)}[/]");
                }
                return 0;
            }

            AnsiConsole.MarkupLine($"[red]✗ Test zlyhal: HTTP kód: {result?.StatusCode}, Chyba: {Markup.Escape(result?.Error ?? "Neznáma chyba")}[/]");
            return 1;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Chyba: {Markup.Escape(ex.Message)}[/]");
            return 1;
        }
    }

    private static async Task<int> HandleDeliveriesAsync(string[] args)
    {
        string serverEndpoint = GetArg(args, "--server") ?? "http://localhost:8080";
        string? status = GetArg(args, "--status");
        string? limitArg = GetArg(args, "--limit");
        int limit = int.TryParse(limitArg, CultureInfo.InvariantCulture, out int l) ? l : 30;

        string title = string.IsNullOrWhiteSpace(status)
            ? "=== História Doručenia Webhookov (Všetky) ==="
            : $"=== História Doručenia Webhookov (Stav: {status}) ===";

        AnsiConsole.MarkupLine($"[bold blue]{title}[/]");
        using var client = CreateClient(serverEndpoint);

        string query = $"/admin/v1/webhooks/deliveries?limit={limit}";
        if (!string.IsNullOrWhiteSpace(status))
        {
            query += $"&status={status}";
        }

        try
        {
            var response = await client.GetAsync(new Uri(query, UriKind.Relative)).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]Server vrátil kód: {response.StatusCode}[/]");
                return 1;
            }

            var deliveries = await response.Content.ReadFromJsonAsync<List<WebhookDeliveryCliDto>>().ConfigureAwait(false) ?? [];

            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumn("[cyan]Delivery ID[/]");
            table.AddColumn("[white]Udalosť[/]");
            table.AddColumn("[yellow]Stav[/]");
            table.AddColumn("[grey]HTTP Kód[/]");
            table.AddColumn("[grey]Trvanie[/]");
            table.AddColumn("[grey]Pokusy[/]");
            table.AddColumn("[blue]Čas[/]");

            foreach (var d in deliveries)
            {
                string statusColor = d.Status.ToUpperInvariant() switch
                {
                    "DELIVERED" => "green",
                    "DEAD_LETTER" => "magenta",
                    "FAILED" => "red",
                    _ => "yellow"
                };

                table.AddRow(
                    $"[bold]{d.Id}[/]",
                    Markup.Escape(d.EventType),
                    $"[{statusColor}]{d.Status}[/]",
                    d.StatusCode?.ToString(CultureInfo.InvariantCulture) ?? "-",
                    $"{d.DurationMs} ms",
                    d.Attempts.ToString(CultureInfo.InvariantCulture),
                    d.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            }

            AnsiConsole.Write(table);
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Chyba: {Markup.Escape(ex.Message)}[/]");
            return 1;
        }
    }

    private static async Task<int> HandleReplayAsync(string[] args)
    {
        string serverEndpoint = GetArg(args, "--server") ?? "http://localhost:8080";
        string? id = GetArg(args, "--id") ?? (args.Length > 0 && !args[0].StartsWith('-') ? args[0] : null);

        if (string.IsNullOrWhiteSpace(id))
        {
            AnsiConsole.MarkupLine("[red]Chyba: Chýba parameter --id <deliveryId> pre zopakovanie správy.[/]");
            return 1;
        }

        AnsiConsole.MarkupLine($"[blue]Opätovne odosielam doručenie {Markup.Escape(id)} z Dead-Letter Queue...[/]");
        using var client = CreateClient(serverEndpoint);

        try
        {
            var response = await client.PostAsync(new Uri($"/admin/v1/webhooks/deliveries/{id}/replay", UriKind.Relative), null).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                string err = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                AnsiConsole.MarkupLine($"[red]Zopakovanie zlyhalo ({response.StatusCode}): {Markup.Escape(err)}[/]");
                return 1;
            }

            var result = await response.Content.ReadFromJsonAsync<WebhookDeliveryCliDto>().ConfigureAwait(false);
            if (string.Equals(result?.Status, "delivered", StringComparison.OrdinalIgnoreCase))
            {
                AnsiConsole.MarkupLine($"[green]✓ Správa úspešne doručená! HTTP kód: {result?.StatusCode}, Trvanie: {result?.DurationMs} ms[/]");
                return 0;
            }

            AnsiConsole.MarkupLine($"[yellow]Správa opätovne zlyhala: Stav={result?.Status}, Chyba={Markup.Escape(result?.LastError ?? "Neznáma")}[/]");
            return 1;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Chyba: {Markup.Escape(ex.Message)}[/]");
            return 1;
        }
    }

    private static async Task<int> HandleLifecycleAsync(string[] args)
    {
        string serverEndpoint = GetArg(args, "--server") ?? "http://localhost:8080";

        AnsiConsole.MarkupLine("[bold blue]=== Kontrola Životného Cyklu a Expirácií Licencií ===[/]");
        using var client = CreateClient(serverEndpoint);

        try
        {
            var response = await client.PostAsync(new Uri("/admin/v1/lifecycle/evaluate", UriKind.Relative), null).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]Server vrátil kód: {response.StatusCode}[/]");
                return 1;
            }

            string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            int total = root.GetProperty("totalEvaluated").GetInt32();
            int soon = root.GetProperty("expiringSoonCount").GetInt32();
            int grace = root.GetProperty("graceEnteredCount").GetInt32();
            int expired = root.GetProperty("expiredCount").GetInt32();
            int dispatched = root.GetProperty("dispatchedEvents").GetInt32();

            AnsiConsole.MarkupLine($"Celkovo vyhodnotených licencií: [bold]{total}[/]");
            AnsiConsole.MarkupLine($"Blížiaca sa expirácia (≤14 dní): [yellow]{soon}[/]");
            AnsiConsole.MarkupLine($"Ochranná lehota (Soft Grace): [magenta]{grace}[/]");
            AnsiConsole.MarkupLine($"Expirované licencie: [red]{expired}[/]");
            AnsiConsole.MarkupLine($"Vygenerované a odoslané webhook udalosti: [green]{dispatched}[/]");

            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Chyba: {Markup.Escape(ex.Message)}[/]");
            return 1;
        }
    }

    private static HttpClient CreateClient(string serverEndpoint)
    {
        var client = new HttpClient { BaseAddress = new Uri(serverEndpoint) };
        client.DefaultRequestHeaders.Add("X-Api-Key", "sym_adm_bootstrap_key_12345");
        return client;
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

    private static int UnknownSubcommand(string sub)
    {
        AnsiConsole.MarkupLine($"[red]Neznámy príkaz: {Markup.Escape(sub)}[/]");
        PrintWebhooksHelp();
        return 1;
    }

    private static void PrintWebhooksHelp()
    {
        AnsiConsole.MarkupLine("[bold]Použitie:[/] symbolon webhooks <príkaz> [[prepínače]]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Príkazy:[/] ");
        AnsiConsole.MarkupLine("  list                      Zoznam aktívnych webhook odberov");
        AnsiConsole.MarkupLine("  create                    Vytvorenie nového webhook odberu (--url, --events, --name, --format, --secret)");
        AnsiConsole.MarkupLine("  delete                    Odstránenie webhook odberu (--id <id>)");
        AnsiConsole.MarkupLine("  test                      Odoslanie testovacieho ping volania (--id <id>)");
        AnsiConsole.MarkupLine("  deliveries | log          História doručení a DLQ audit (--status, --limit)");
        AnsiConsole.MarkupLine("  replay                    Manuálne zopakovanie neúspešného doručenia z DLQ (--id <id>)");
        AnsiConsole.MarkupLine("  lifecycle | expiring      Vyhodnotenie životného cyklu licencií a odoslanie alertov");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Prepínače:[/] ");
        AnsiConsole.MarkupLine("  --url <url>               Cieľový HTTPS/HTTP endpoint");
        AnsiConsole.MarkupLine("  --events <ev1,ev2>        Zoznam odoberaných udalostí oddelený čiarkami (alebo * pre všetky)");
        AnsiConsole.MarkupLine("  --format <json|slack|teams> Formát správy (predvolené: json)");
        AnsiConsole.MarkupLine("  --secret <tajomstvo>      Zdieľaný tajný kľúč pre HMAC-SHA256 podpis");
        AnsiConsole.MarkupLine("  --status <status>         Filter histórie: delivered, failed, dead_letter");
        AnsiConsole.MarkupLine("  --server <url>            URL Symbolon servera (predvolené: http://localhost:8080)");
    }
}

#pragma warning disable CA1054, CA1056, CA1002 // CLI DTO mapping
public sealed record WebhookSubCliDto(
    string Id,
    string TenantId,
    string Url,
    List<string> Events,
    bool IsActive,
    DateTimeOffset CreatedAt,
    string? Name = null,
    string? Format = null,
    int FailureCount = 0,
    DateTimeOffset? LastDeliveredAt = null);
#pragma warning restore CA1054, CA1056, CA1002

public sealed record WebhookDeliveryCliDto(
    string Id,
    string SubscriptionId,
    string EventType,
    string Status,
    int? StatusCode,
    int Attempts,
    DateTimeOffset? DeliveredAt,
    string? LastError,
    DateTimeOffset CreatedAt,
    long DurationMs = 0);

public sealed record WebhookTestCliDto(
    bool Success,
    int? StatusCode,
    string? ResponseBody,
    long ElapsedMilliseconds,
    string? Error);
