using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Spectre.Console;

namespace Achilles.Cli.Commands;

public static class TenantCommands
{
    public static async Task<int> HandleTenantAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintTenantHelp();
            return 0;
        }

        return args[0].ToUpperInvariant() switch
        {
            "BRANDING" => await HandleBrandingAsync(args[1..]).ConfigureAwait(false),
            "QUOTAS" or "QUOTA" => await HandleQuotasAsync(args[1..]).ConfigureAwait(false),
            "DEPARTMENTS" or "DEPTS" or "LIST" => await HandleListDepartmentsAsync(args[1..]).ConfigureAwait(false),
            _ => UnknownSubcommand(args[0])
        };
    }

    private static async Task<int> HandleBrandingAsync(string[] args)
    {
        if (args.Length == 0 || args[0].StartsWith('-'))
        {
            AnsiConsole.MarkupLine("[red]Chyba: Chýba identifikátor tenanta (tenantId).[/]");
            PrintTenantHelp();
            return 1;
        }

        string tenantId = args[0];
        string server = GetArg(args, "--server") ?? "http://localhost:8080";
        string? name = GetArg(args, "--name");
        string? logo = GetArg(args, "--logo");
        string? primaryColor = GetArg(args, "--color");
        string? accentColor = GetArg(args, "--accent");
        string? title = GetArg(args, "--title");
        string? css = GetArg(args, "--css");

        using var client = CreateClient(server);

        // If no update flags provided, fetch and display current branding
        if (name is null && logo is null && primaryColor is null && accentColor is null && title is null && css is null)
        {
            AnsiConsole.MarkupLine($"[cyan bold]=== TENANT BRANDING & WHITE-LABEL (Tenant: {Markup.Escape(tenantId)}) ===[/]");
            var getRes = await client.GetAsync(new Uri($"/admin/v1/tenants/{Uri.EscapeDataString(tenantId)}/branding", UriKind.Relative)).ConfigureAwait(false);
            if (!getRes.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]Chyba načítania: {getRes.StatusCode}[/]");
                return 1;
            }

            var doc = await getRes.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumn("Vlastnosť");
            table.AddColumn("Hodnota");

            table.AddRow("Spoločnosť (Company)", doc.GetProperty("companyName").GetString() ?? "-");
            table.AddRow("Titulok Portálu", doc.GetProperty("portalTitle").GetString() ?? "-");
            table.AddRow("Primárna Farba", $"[bold]{doc.GetProperty("primaryColorHex").GetString()}[/]");
            table.AddRow("Akcentová Farba", $"[bold]{doc.GetProperty("accentColorHex").GetString()}[/]");
            table.AddRow("Logo URL", doc.TryGetProperty("logoUrl", out var l) && l.GetString() != null ? l.GetString()! : "[dim](predvolené Symbolon logo)[/]");
            table.AddRow("Vlastné CSS", doc.TryGetProperty("customCss", out var c) && c.GetString() != null ? "[green]Áno[/]" : "[dim]Nie[/]");

            AnsiConsole.Write(table);
            return 0;
        }

        // Otherwise update branding
        var updatePayload = new
        {
            companyName = name ?? tenantId,
            logoUrl = logo,
            primaryColorHex = primaryColor,
            accentColorHex = accentColor,
            portalTitle = title,
            customCss = css
        };

        var postRes = await client.PostAsJsonAsync(new Uri($"/admin/v1/tenants/{Uri.EscapeDataString(tenantId)}/branding", UriKind.Relative), updatePayload).ConfigureAwait(false);
        if (!postRes.IsSuccessStatusCode)
        {
            AnsiConsole.MarkupLine($"[red]Chyba aktualizácie: {postRes.StatusCode}[/]");
            return 1;
        }

        AnsiConsole.MarkupLine($"[green bold]✓ Branding pre tenanta '{Markup.Escape(tenantId)}' bol úspešne uložený.[/]");
        return 0;
    }

    private static async Task<int> HandleQuotasAsync(string[] args)
    {
        if (args.Length == 0 || args[0].StartsWith('-'))
        {
            AnsiConsole.MarkupLine("[red]Chyba: Chýba identifikátor tenanta (tenantId).[/]");
            PrintTenantHelp();
            return 1;
        }

        string tenantId = args[0];
        string server = GetArg(args, "--server") ?? "http://localhost:8080";
        string? dept = GetArg(args, "--dept");
        string? seatsStr = GetArg(args, "--seats");
        bool strict = args.Contains("--strict", StringComparer.OrdinalIgnoreCase);
        bool delete = args.Contains("--delete", StringComparer.OrdinalIgnoreCase);

        using var client = CreateClient(server);

        if (string.IsNullOrWhiteSpace(dept))
        {
            // View current quotas
            return await HandleListDepartmentsInternalAsync(client, tenantId).ConfigureAwait(false);
        }

        if (delete)
        {
            var delRes = await client.DeleteAsync(new Uri($"/admin/v1/tenants/{Uri.EscapeDataString(tenantId)}/departments/{Uri.EscapeDataString(dept)}", UriKind.Relative)).ConfigureAwait(false);
            if (!delRes.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]Chyba pri mazaní kvóty: {delRes.StatusCode}[/]");
                return 1;
            }
            AnsiConsole.MarkupLine($"[green bold]✓ Kvóta oddelenia '{Markup.Escape(dept)}' bola zmazaná.[/]");
            return 0;
        }

        if (seatsStr is null || !int.TryParse(seatsStr, CultureInfo.InvariantCulture, out int seats))
        {
            AnsiConsole.MarkupLine("[red]Chyba: Zadajte počet sedadiel cez --seats <číslo>.[/]");
            return 1;
        }

        var payload = new
        {
            departmentName = dept,
            allocatedSeats = seats,
            enforceStrictQuota = strict || !args.Contains("--permissive", StringComparer.OrdinalIgnoreCase)
        };

        var setRes = await client.PostAsJsonAsync(new Uri($"/admin/v1/tenants/{Uri.EscapeDataString(tenantId)}/departments", UriKind.Relative), payload).ConfigureAwait(false);
        if (!setRes.IsSuccessStatusCode)
        {
            AnsiConsole.MarkupLine($"[red]Chyba pri ukladaní kvóty: {setRes.StatusCode}[/]");
            return 1;
        }

        AnsiConsole.MarkupLine($"[green bold]✓ Kvóta pre oddelenie '{Markup.Escape(dept)}' nastavená na {seats} sedadiel (Prísne: {payload.enforceStrictQuota}).[/]");
        return 0;
    }

    private static async Task<int> HandleListDepartmentsAsync(string[] args)
    {
        if (args.Length == 0 || args[0].StartsWith('-'))
        {
            AnsiConsole.MarkupLine("[red]Chyba: Chýba identifikátor tenanta (tenantId).[/]");
            PrintTenantHelp();
            return 1;
        }

        string tenantId = args[0];
        string server = GetArg(args, "--server") ?? "http://localhost:8080";
        using var client = CreateClient(server);

        return await HandleListDepartmentsInternalAsync(client, tenantId).ConfigureAwait(false);
    }

    private static async Task<int> HandleListDepartmentsInternalAsync(HttpClient client, string tenantId)
    {
        AnsiConsole.MarkupLine($"[cyan bold]=== ODDIELENIA & SPOTREBA SEDADIEL (Tenant: {Markup.Escape(tenantId)}) ===[/]");
        var res = await client.GetAsync(new Uri($"/admin/v1/tenants/{Uri.EscapeDataString(tenantId)}/departments", UriKind.Relative)).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode)
        {
            AnsiConsole.MarkupLine($"[red]Chyba pri načítaní: {res.StatusCode}[/]");
            return 1;
        }

        var doc = await res.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Oddelenie");
        table.AddColumn("Pridelené sedadlá");
        table.AddColumn("Aktívne sedadlá");
        table.AddColumn("Voľné");
        table.AddColumn("Prísny limit");

        var depts = doc.GetProperty("departments");
        int totalAlloc = doc.GetProperty("totalAllocatedSeats").GetInt32();
        int totalActive = doc.GetProperty("totalActiveSeats").GetInt32();

        foreach (var item in depts.EnumerateArray())
        {
            string dName = item.GetProperty("departmentName").GetString() ?? "-";
            int alloc = item.GetProperty("allocatedSeats").GetInt32();
            int active = item.GetProperty("activeSeats").GetInt32();
            bool strict = item.GetProperty("enforceStrictQuota").GetBoolean();
            int free = alloc - active;

            string freeColor = free <= 0 ? "red" : (free < 3 ? "yellow" : "green");

            table.AddRow(
                Markup.Escape(dName),
                alloc.ToString(CultureInfo.InvariantCulture),
                active.ToString(CultureInfo.InvariantCulture),
                $"[{freeColor}]{free}[/]",
                strict ? "[green]Áno[/]" : "[dim]Nie[/]");
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"[bold]Celkovo v oddeleniach:[/] Pridelené: [cyan]{totalAlloc}[/] | Aktívne: [yellow]{totalActive}[/]\n");
        return 0;
    }

    private static HttpClient CreateClient(string serverUrl)
    {
        var client = new HttpClient { BaseAddress = new Uri(serverUrl) };
        return client;
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

    private static int UnknownSubcommand(string subcmd)
    {
        AnsiConsole.MarkupLine($"[red]Neznámy Tenant príkaz: {Markup.Escape(subcmd)}[/]");
        PrintTenantHelp();
        return 1;
    }

    private static void PrintTenantHelp()
    {
        AnsiConsole.WriteLine("""
            Použitie: symbolon tenant <subcommand> <tenantId> [options]

            Príkazy:
              branding     <tenantId> [--name <n>] [--logo <url>] [--color <hex>] [--accent <hex>] [--title <t>] [--css <css>]
                           Zobrazí alebo nastaví white-labeling a firemný branding portálu pre daného tenanta.
              quotas       <tenantId> [--dept <d> --seats <n> [--strict] [--delete]]
                           Zobrazí alebo nastaví kvóty sedadiel pre jednotlivé oddelenia tenanta.
              departments  <tenantId>
                           Zobrazí prehľad oddelení, alokovaných a živých sedadiel.

            Možnosti:
              --server <url>   URL Symbolon servera (predvolené: http://localhost:8080)
            """);
    }
}
