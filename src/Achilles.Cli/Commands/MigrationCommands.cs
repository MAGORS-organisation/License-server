using System.Globalization;
using System.Text;
using System.Text.Json;
using Spectre.Console;

namespace Achilles.Cli.Commands;

public static class MigrationCommands
{
    private static readonly JsonSerializerOptions IndentedJsonOptions = new() { WriteIndented = true };

    public static async Task<int> HandleImportAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            AnsiConsole.MarkupLine("[bold blue]Použitie:[/] symbolon import --file <cesta> --policy <policy-id> [[options]]");
            AnsiConsole.MarkupLine("  [yellow]--file <path>[/]      Cesta k importovanému súboru (.json alebo .csv)");
            AnsiConsole.MarkupLine("  [yellow]--policy <id>[/]      ID licenčnej politiky v Symbolon Control Plane");
            AnsiConsole.MarkupLine("  [yellow]--format <type>[/]    keygen | csv (predvolené podľa prípony súboru)");
            AnsiConsole.MarkupLine("  [yellow]--url <url>[/]        URL Symbolon Control Plane (predvolené http://localhost:5000)");
            AnsiConsole.MarkupLine("  [yellow]--dry-run[/]          Overí formát a dáta bez reálneho zápisu do databázy");
            return 0;
        }

        string? filePath = GetArg(args, "--file");
        string? policyId = GetArg(args, "--policy");
        string? format = GetArg(args, "--format");
        string url = GetArg(args, "--url") ?? "http://localhost:5000";
        bool dryRun = args.Contains("--dry-run", StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            AnsiConsole.MarkupLine("[bold red]Chyba:[/] Súbor pre import neexistuje alebo nebol zadaný (--file).");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(policyId))
        {
            AnsiConsole.MarkupLine("[bold red]Chyba:[/] Je potrebné špecifikovať ID politiky (--policy).");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(format))
        {
            format = Path.GetExtension(filePath).Equals(".csv", StringComparison.OrdinalIgnoreCase) ? "csv" : "keygen";
        }

        AnsiConsole.MarkupLine($"[bold]Spúšťam import z formátu:[/] [green]{format.ToUpperInvariant()}[/] | Súbor: [yellow]{filePath}[/]");
        if (dryRun)
        {
            AnsiConsole.MarkupLine("[bold yellow]*** DRY RUN REŽIM: Žiadne licencie nebudú reálne vytvorené ***[/]");
        }

        var items = new List<ImportLicenseItem>();
        if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
        {
            items = ParseCsv(filePath);
        }
        else
        {
            items = ParseKeygenJson(filePath);
        }

        if (items.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]V súbore sa nenašli žiadne položky na import.[/]");
            return 0;
        }

        AnsiConsole.MarkupLine($"Načítaných [cyan]{items.Count}[/] položiek na import.");

        using var http = new HttpClient { BaseAddress = new Uri(url.TrimEnd('/') + "/") };
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Zákazník")
            .AddColumn("Sedadlá")
            .AddColumn("Platnosť Do")
            .AddColumn("Stav")
            .AddColumn("Licenčný Kľúč");

        int successCount = 0;
        int failedCount = 0;

        foreach (var item in items)
        {
            if (dryRun)
            {
                table.AddRow(
                    Markup.Escape(item.CustomerRef ?? "-"),
                    item.MaxSeats.ToString(CultureInfo.InvariantCulture),
                    item.ExpiresAt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "Neobmedzená",
                    "[yellow]VALIDATED (Dry-Run)[/]",
                    Markup.Escape(item.ExistingKey ?? "-"));
                successCount++;
                continue;
            }

            try
            {
                var payload = new
                {
                    policyId,
                    customerRef = item.CustomerRef,
                    maxSeats = item.MaxSeats,
                    expiresAt = item.ExpiresAt
                };

                using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                using var res = await http.PostAsync(new Uri("admin/v1/licenses", UriKind.Relative), content).ConfigureAwait(false);

                if (res.IsSuccessStatusCode)
                {
                    var json = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    string key = doc.RootElement.TryGetProperty("licenseKey", out var kp) ? kp.GetString() ?? "" : "";

                    table.AddRow(
                        Markup.Escape(item.CustomerRef ?? "-"),
                        item.MaxSeats.ToString(CultureInfo.InvariantCulture),
                        item.ExpiresAt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "Neobmedzená",
                        "[green]OK[/]",
                        $"[cyan]{Markup.Escape(key)}[/]");
                    successCount++;
                }
                else
                {
                    string err = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                    table.AddRow(
                        Markup.Escape(item.CustomerRef ?? "-"),
                        item.MaxSeats.ToString(CultureInfo.InvariantCulture),
                        item.ExpiresAt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "Neobmedzená",
                        $"[red]CHYBA: {Markup.Escape(err)}[/]",
                        "-");
                    failedCount++;
                }
            }
            catch (Exception ex)
            {
                table.AddRow(
                    Markup.Escape(item.CustomerRef ?? "-"),
                    item.MaxSeats.ToString(CultureInfo.InvariantCulture),
                    item.ExpiresAt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "Neobmedzená",
                    $"[red]Sieťová chyba: {Markup.Escape(ex.Message)}[/]",
                    "-");
                failedCount++;
            }
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"[bold]Výsledok importu:[/] Úspešné: [green]{successCount}[/], Zlyhané: [red]{failedCount}[/]");
        return failedCount == 0 ? 0 : 1;
    }

    public static async Task<int> HandleExportAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            AnsiConsole.MarkupLine("[bold blue]Použitie:[/] symbolon export --out <cesta> [[options]]");
            AnsiConsole.MarkupLine("  [yellow]--out <path>[/]       Cesta k výstupnému súboru");
            AnsiConsole.MarkupLine("  [yellow]--format <type>[/]    json | csv (predvolené json)");
            AnsiConsole.MarkupLine("  [yellow]--url <url>[/]        URL Symbolon Control Plane (predvolené http://localhost:5000)");
            return 0;
        }

        string? outPath = GetArg(args, "--out");
        string format = GetArg(args, "--format") ?? "json";
        string url = GetArg(args, "--url") ?? "http://localhost:5000";

        if (string.IsNullOrWhiteSpace(outPath))
        {
            AnsiConsole.MarkupLine("[bold red]Chyba:[/] Cesta k výstupu nie je zadaná (--out).");
            return 1;
        }

        using var http = new HttpClient { BaseAddress = new Uri(url.TrimEnd('/') + "/") };
        try
        {
            using var res = await http.GetAsync(new Uri("admin/v1/licenses", UriKind.Relative)).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[bold red]Chyba:[/] Server vrátil HTTP {(int)res.StatusCode}");
                return 1;
            }

            string json = await res.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
            {
                using var doc = JsonDocument.Parse(json);
                var sb = new StringBuilder();
                sb.AppendLine("Id,CustomerRef,PolicyId,MaxSeats,State,IssuedAt,ExpiresAt");

                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    string id = el.GetProperty("id").GetString() ?? "";
                    string customer = el.TryGetProperty("customerRef", out var cp) ? cp.GetString() ?? "" : "";
                    string policy = el.GetProperty("policyId").GetString() ?? "";
                    int seats = el.GetProperty("maxSeats").GetInt32();
                    string state = el.GetProperty("state").GetString() ?? "";
                    string issued = el.GetProperty("issuedAt").GetString() ?? "";
                    string expires = el.TryGetProperty("expiresAt", out var ep) ? ep.GetString() ?? "" : "";

                    sb.AppendLine(CultureInfo.InvariantCulture, $"\"{id}\",\"{customer}\",\"{policy}\",{seats},\"{state}\",\"{issued}\",\"{expires}\"");
                }

                await File.WriteAllTextAsync(outPath, sb.ToString()).ConfigureAwait(false);
            }
            else
            {
                using var doc = JsonDocument.Parse(json);
                string pretty = JsonSerializer.Serialize(doc, IndentedJsonOptions);
                await File.WriteAllTextAsync(outPath, pretty).ConfigureAwait(false);
            }

            AnsiConsole.MarkupLine($"[bold green]Export úspešne uložený do:[/] [yellow]{outPath}[/]");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[bold red]Chyba exportu:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
    }

    private static List<ImportLicenseItem> ParseCsv(string filePath)
    {
        var list = new List<ImportLicenseItem>();
        var lines = File.ReadAllLines(filePath);
        if (lines.Length == 0) return list;

        int startIndex = 0;
        if (lines[0].Contains("customer", StringComparison.OrdinalIgnoreCase) || lines[0].Contains("seats", StringComparison.OrdinalIgnoreCase))
        {
            startIndex = 1; // Preskočiť hlavičku
        }

        for (int i = startIndex; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            var parts = line.Split(',');
            string customer = parts.Length > 0 ? parts[0].Trim().Trim('"') : "";
            int seats = parts.Length > 1 && int.TryParse(parts[1].Trim(), CultureInfo.InvariantCulture, out int s) ? s : 1;
            DateTimeOffset? expires = null;
            if (parts.Length > 2 && DateTimeOffset.TryParse(parts[2].Trim().Trim('"'), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            {
                expires = d;
            }

            list.Add(new ImportLicenseItem(customer, seats, expires, null));
        }

        return list;
    }

    private static List<ImportLicenseItem> ParseKeygenJson(string filePath)
    {
        var list = new List<ImportLicenseItem>();
        string json = File.ReadAllText(filePath);
        using var doc = JsonDocument.Parse(json);

        JsonElement dataArray;
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            dataArray = doc.RootElement;
        }
        else if (doc.RootElement.TryGetProperty("data", out var dProp) && dProp.ValueKind == JsonValueKind.Array)
        {
            dataArray = dProp;
        }
        else
        {
            return list;
        }

        foreach (var item in dataArray.EnumerateArray())
        {
            if (item.TryGetProperty("attributes", out var attrs))
            {
                string? name = attrs.TryGetProperty("name", out var np) ? np.GetString() : null;
                string? key = attrs.TryGetProperty("key", out var kp) ? kp.GetString() : null;
                int seats = 1;
                if (attrs.TryGetProperty("maxMachines", out var mmp) && mmp.TryGetInt32(out int mm))
                {
                    seats = mm;
                }
                else if (attrs.TryGetProperty("maxUsers", out var mup) && mup.TryGetInt32(out int mu))
                {
                    seats = mu;
                }

                DateTimeOffset? expires = null;
                if (attrs.TryGetProperty("expiry", out var expProp) && expProp.ValueKind == JsonValueKind.String)
                {
                    if (DateTimeOffset.TryParse(expProp.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var exp))
                    {
                        expires = exp;
                    }
                }

                list.Add(new ImportLicenseItem(name, seats, expires, key));
            }
        }

        return list;
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

    private sealed record ImportLicenseItem(string? CustomerRef, int MaxSeats, DateTimeOffset? ExpiresAt, string? ExistingKey);
}
