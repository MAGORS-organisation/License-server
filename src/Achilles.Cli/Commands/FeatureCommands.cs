using System.Globalization;
using System.Net.Http.Json;
using Spectre.Console;

namespace Achilles.Cli.Commands;

public static class FeatureCommands
{
    public static async Task<int> HandleFeaturesAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintFeaturesHelp();
            return 0;
        }

        return args[0].ToUpperInvariant() switch
        {
            "LIST" or "FEATURES" => await HandleListFeaturesAsync(args[1..]).ConfigureAwait(false),
            "CREATE" => await HandleCreateFeatureAsync(args[1..]).ConfigureAwait(false),
            "DELETE" => await HandleDeleteFeatureAsync(args[1..]).ConfigureAwait(false),
            "SUITES" => await HandleListSuitesAsync(args[1..]).ConfigureAwait(false),
            "CREATE-SUITE" => await HandleCreateSuiteAsync(args[1..]).ConfigureAwait(false),
            "DELETE-SUITE" => await HandleDeleteSuiteAsync(args[1..]).ConfigureAwait(false),
            "GRANT" => await HandleGrantEntitlementAsync(args[1..]).ConfigureAwait(false),
            "USAGE" => await HandleUsageAsync(args[1..]).ConfigureAwait(false),
            _ => UnknownSubcommand(args[0])
        };
    }

    private static async Task<int> HandleListFeaturesAsync(string[] args)
    {
        string serverEndpoint = GetArg(args, "--server") ?? "http://localhost:8080";
        string? apiKey = GetArg(args, "--key");
        string? productId = GetArg(args, "--product");

        AnsiConsole.MarkupLine("[bold blue]=== Symbolon Katalóg Funkcií a Modulov (Feature Definitions) ===[/]");

        using var client = CreateClient(serverEndpoint, apiKey);
        string url = string.IsNullOrWhiteSpace(productId)
            ? "/admin/v1/entitlements/features"
            : $"/admin/v1/entitlements/features?productId={Uri.EscapeDataString(productId)}";

        try
        {
            var response = await client.GetAsync(new Uri(url, UriKind.Relative)).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]Server vrátil kód: {response.StatusCode}[/]");
                return 1;
            }

            var list = await response.Content.ReadFromJsonAsync<List<FeatureDefCliDto>>().ConfigureAwait(false) ?? [];

            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumn("[cyan]Kód[/]");
            table.AddColumn("[white]Názov[/]");
            table.AddColumn("[grey]Produkt[/]");
            table.AddColumn("[yellow]Typ[/]");
            table.AddColumn("[blue]Predvolené Sedadlá[/]");
            table.AddColumn("[green]Verzie[/]");

            foreach (var f in list)
            {
                string verRange = (string.IsNullOrWhiteSpace(f.MinVersion) && string.IsNullOrWhiteSpace(f.MaxVersion))
                    ? "všetky (*)"
                    : $"{f.MinVersion ?? "0"} - {f.MaxVersion ?? "latest"}";

                table.AddRow(
                    $"[bold cyan]{f.Code}[/]",
                    Markup.Escape(f.Name),
                    Markup.Escape(f.ProductId ?? "-"),
                    f.IsFloating ? "[green]Floating Seat[/]" : "[blue]Stála (Node/Seatless)[/]",
                    f.DefaultMaxSeats.HasValue ? f.DefaultMaxSeats.Value.ToString(CultureInfo.InvariantCulture) : "Neobmedzené",
                    Markup.Escape(verRange));
            }

            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine($"Celkom definícií: [bold]{list.Count}[/]");
            return 0;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            AnsiConsole.MarkupLine($"[red]Chyba spojenia:[/] {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> HandleCreateFeatureAsync(string[] args)
    {
        string? code = GetArg(args, "--code");
        string? name = GetArg(args, "--name");
        string? product = GetArg(args, "--product");
        string? description = GetArg(args, "--description");
        string? minVer = GetArg(args, "--min-ver");
        string? maxVer = GetArg(args, "--max-ver");
        string serverEndpoint = GetArg(args, "--server") ?? "http://localhost:8080";
        string? apiKey = GetArg(args, "--key");
        bool isFloating = !string.Equals(GetArg(args, "--floating"), "false", StringComparison.OrdinalIgnoreCase);

        int? maxSeats = null;
        if (int.TryParse(GetArg(args, "--seats"), CultureInfo.InvariantCulture, out int s))
        {
            maxSeats = s;
        }

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
        {
            AnsiConsole.MarkupLine("[red]Chyba: Parametre --code a --name sú povinné.[/]");
            return 1;
        }

        using var client = CreateClient(serverEndpoint, apiKey);
        var payload = new
        {
            Code = code.Trim().ToUpperInvariant(),
            Name = name.Trim(),
            ProductId = product,
            Description = description,
            MinVersion = minVer,
            MaxVersion = maxVer,
            IsFloating = isFloating,
            DefaultMaxSeats = maxSeats
        };

        try
        {
            var response = await client.PostAsJsonAsync(
                new Uri("/admin/v1/entitlements/features", UriKind.Relative),
                payload).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]Chyba pri vytváraní funkcie: {response.StatusCode}[/]");
                return 1;
            }

            AnsiConsole.MarkupLine($"[green]Funkcia [bold]{code.ToUpperInvariant()}[/] bola úspešne zaregistrovaná v katalógu.[/]");
            return 0;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            AnsiConsole.MarkupLine($"[red]Chyba spojenia:[/] {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> HandleDeleteFeatureAsync(string[] args)
    {
        string? id = GetArg(args, "--id");
        string serverEndpoint = GetArg(args, "--server") ?? "http://localhost:8080";
        string? apiKey = GetArg(args, "--key");

        if (string.IsNullOrWhiteSpace(id))
        {
            AnsiConsole.MarkupLine("[red]Chyba: Parameter --id je povinný.[/]");
            return 1;
        }

        using var client = CreateClient(serverEndpoint, apiKey);
        try
        {
            var response = await client.DeleteAsync(
                new Uri($"/admin/v1/entitlements/features/{Uri.EscapeDataString(id)}", UriKind.Relative)).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]Chyba pri mazaní funkcie: {response.StatusCode}[/]");
                return 1;
            }

            AnsiConsole.MarkupLine($"[green]Funkcia s ID [bold]{id}[/] bola úspešne vymazaná.[/]");
            return 0;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            AnsiConsole.MarkupLine($"[red]Chyba spojenia:[/] {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> HandleListSuitesAsync(string[] args)
    {
        string serverEndpoint = GetArg(args, "--server") ?? "http://localhost:8080";
        string? apiKey = GetArg(args, "--key");
        string? productId = GetArg(args, "--product");

        AnsiConsole.MarkupLine("[bold blue]=== Symbolon Balíčky a Suity (Package Suites) ===[/]");

        using var client = CreateClient(serverEndpoint, apiKey);
        string url = string.IsNullOrWhiteSpace(productId)
            ? "/admin/v1/entitlements/suites"
            : $"/admin/v1/entitlements/suites?productId={Uri.EscapeDataString(productId)}";

        try
        {
            var response = await client.GetAsync(new Uri(url, UriKind.Relative)).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]Server vrátil kód: {response.StatusCode}[/]");
                return 1;
            }

            var list = await response.Content.ReadFromJsonAsync<List<PackageSuiteCliDto>>().ConfigureAwait(false) ?? [];

            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumn("[cyan]Kód Suity[/]");
            table.AddColumn("[white]Názov[/]");
            table.AddColumn("[grey]Produkt[/]");
            table.AddColumn("[yellow]Zahrnuté Moduly / Funkcie[/]");

            foreach (var s in list)
            {
                string feats = string.Join(", ", s.FeatureCodes);
                table.AddRow(
                    $"[bold cyan]{s.Code}[/]",
                    Markup.Escape(s.Name),
                    Markup.Escape(s.ProductId ?? "-"),
                    Markup.Escape(feats));
            }

            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine($"Celkom balíčkov: [bold]{list.Count}[/]");
            return 0;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            AnsiConsole.MarkupLine($"[red]Chyba spojenia:[/] {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> HandleCreateSuiteAsync(string[] args)
    {
        string? code = GetArg(args, "--code");
        string? name = GetArg(args, "--name");
        string? product = GetArg(args, "--product");
        string? description = GetArg(args, "--description");
        string? featsArg = GetArg(args, "--features");
        string serverEndpoint = GetArg(args, "--server") ?? "http://localhost:8080";
        string? apiKey = GetArg(args, "--key");

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(featsArg))
        {
            AnsiConsole.MarkupLine("[red]Chyba: Parametre --code, --name a --features sú povinné.[/]");
            return 1;
        }

        var featureCodes = featsArg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        using var client = CreateClient(serverEndpoint, apiKey);
        var payload = new
        {
            Code = code.Trim().ToUpperInvariant(),
            Name = name.Trim(),
            ProductId = product,
            Description = description,
            FeatureCodes = featureCodes
        };

        try
        {
            var response = await client.PostAsJsonAsync(
                new Uri("/admin/v1/entitlements/suites", UriKind.Relative),
                payload).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]Chyba pri vytváraní suity: {response.StatusCode}[/]");
                return 1;
            }

            AnsiConsole.MarkupLine($"[green]Balíček [bold]{code.ToUpperInvariant()}[/] bol úspešne vytvorený ({featureCodes.Length} modulov).[/]");
            return 0;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            AnsiConsole.MarkupLine($"[red]Chyba spojenia:[/] {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> HandleDeleteSuiteAsync(string[] args)
    {
        string? id = GetArg(args, "--id");
        string serverEndpoint = GetArg(args, "--server") ?? "http://localhost:8080";
        string? apiKey = GetArg(args, "--key");

        if (string.IsNullOrWhiteSpace(id))
        {
            AnsiConsole.MarkupLine("[red]Chyba: Parameter --id je povinný.[/]");
            return 1;
        }

        using var client = CreateClient(serverEndpoint, apiKey);
        try
        {
            var response = await client.DeleteAsync(
                new Uri($"/admin/v1/entitlements/suites/{Uri.EscapeDataString(id)}", UriKind.Relative)).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]Chyba pri mazaní suity: {response.StatusCode}[/]");
                return 1;
            }

            AnsiConsole.MarkupLine($"[green]Balíček s ID [bold]{id}[/] bol úspešne vymazaný.[/]");
            return 0;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            AnsiConsole.MarkupLine($"[red]Chyba spojenia:[/] {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> HandleGrantEntitlementAsync(string[] args)
    {
        string? licenseId = GetArg(args, "--license");
        string? featureCode = GetArg(args, "--feature");
        string? versionRange = GetArg(args, "--version-range");
        string serverEndpoint = GetArg(args, "--server") ?? "http://localhost:8080";
        string? apiKey = GetArg(args, "--key");

        int? maxSeats = null;
        if (int.TryParse(GetArg(args, "--seats"), CultureInfo.InvariantCulture, out int s))
        {
            maxSeats = s;
        }

        if (string.IsNullOrWhiteSpace(licenseId) || string.IsNullOrWhiteSpace(featureCode))
        {
            AnsiConsole.MarkupLine("[red]Chyba: Parametre --license a --feature sú povinné.[/]");
            return 1;
        }

        using var client = CreateClient(serverEndpoint, apiKey);
        var payload = new
        {
            FeatureCode = featureCode.Trim().ToUpperInvariant(),
            MaxSeats = maxSeats,
            AllowedVersionRange = versionRange,
            IsEnabled = true
        };

        try
        {
            var response = await client.PostAsJsonAsync(
                new Uri($"/admin/v1/entitlements/licenses/{Uri.EscapeDataString(licenseId)}", UriKind.Relative),
                payload).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]Chyba pri prideľovaní oprávnenia: {response.StatusCode}[/]");
                return 1;
            }

            AnsiConsole.MarkupLine($"[green]Oprávnenie pre modul [bold]{featureCode.ToUpperInvariant()}[/] na licenciu [bold]{licenseId}[/] bolo úspešne nastavené.[/]");
            return 0;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            AnsiConsole.MarkupLine($"[red]Chyba spojenia:[/] {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> HandleUsageAsync(string[] args)
    {
        string serverEndpoint = GetArg(args, "--server") ?? "http://localhost:8080";
        string? apiKey = GetArg(args, "--key");
        string? licenseId = GetArg(args, "--license");

        AnsiConsole.MarkupLine("[bold blue]=== Symbolon Živé Využitie Modulov & Konkurenčné Limity ===[/]");

        using var client = CreateClient(serverEndpoint, apiKey);
        string url = string.IsNullOrWhiteSpace(licenseId)
            ? "/admin/v1/entitlements/usage"
            : $"/admin/v1/entitlements/usage?licenseId={Uri.EscapeDataString(licenseId)}";

        try
        {
            var response = await client.GetAsync(new Uri(url, UriKind.Relative)).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]Server vrátil kód: {response.StatusCode}[/]");
                return 1;
            }

            var metrics = await response.Content.ReadFromJsonAsync<List<FeatureUsageMetricCliDto>>().ConfigureAwait(false) ?? [];

            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumn("[cyan]Modul[/]");
            table.AddColumn("[white]Názov[/]");
            table.AddColumn("[green]Obsadené / Limit[/]");
            table.AddColumn("[yellow]Využitie[/]");
            table.AddColumn("[red]Zamietnuté požiadavky[/]");
            table.AddColumn("[grey]Aktívne Leasy[/]");

            foreach (var m in metrics)
            {
                string limitStr = m.MaxSeats.HasValue ? m.MaxSeats.Value.ToString(CultureInfo.InvariantCulture) : "∞";
                double percent = (m.MaxSeats.HasValue && m.MaxSeats.Value > 0)
                    ? Math.Min(100.0, (m.InUseSeats / (double)m.MaxSeats.Value) * 100.0)
                    : 0.0;

                string barColor = percent > 85 ? "red" : percent > 50 ? "yellow" : "green";
                string usageBar = $"[{barColor}]{percent:F0}%[/]";

                table.AddRow(
                    $"[bold cyan]{m.FeatureCode}[/]",
                    Markup.Escape(m.Name),
                    $"{m.InUseSeats} / {limitStr}",
                    usageBar,
                    m.DenialsCount > 0 ? $"[red bold]{m.DenialsCount}[/]" : "0",
                    m.ActiveLeaseIds.Count.ToString(CultureInfo.InvariantCulture));
            }

            AnsiConsole.Write(table);
            return 0;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            AnsiConsole.MarkupLine($"[red]Chyba spojenia:[/] {ex.Message}");
            return 1;
        }
    }

    private static HttpClient CreateClient(string serverEndpoint, string? apiKey = null)
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri(serverEndpoint),
            Timeout = TimeSpan.FromSeconds(10)
        };
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        }
        return client;
    }

    private static void PrintFeaturesHelp()
    {
        Console.WriteLine("""
            Použitie: symbolon features <subcommand> [options]

            Subcommands:
              list [--server <url>] [--key <k>] [--product <p>]        Zoznam definícií modulov
              create --code <code> --name <name> [options]              Vytvorenie definície modulu
              delete --id <id> [--server <url>] [--key <k>]             Vymazanie modulu z katalógu
              suites [--server <url>] [--key <k>]                      Zoznam balíčkov (suites)
              create-suite --code <code> --name <n> --features <f1,f2>  Vytvorenie balíčka
              delete-suite --id <id> [--server <url>]                   Vymazanie balíčka
              grant --license <id> --feature <code> [options]           Priradenie modulu k licencii
              usage [--server <url>] [--key <k>] [--license <id>]       Živé merače konkurenčného využitia

            Možnosti pre create:
              --floating <true|false>  Určuje, či ide o floating modul (default: true)
              --seats <n>              Predvolený max limit sedadiel
              --min-ver <v>            Minimálna podporovaná verzia
              --max-ver <v>            Maximálna podporovaná verzia
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

    private static int UnknownSubcommand(string sub)
    {
        AnsiConsole.MarkupLine($"[red]Neznámy príkaz pre moduly:[/] {sub}");
        PrintFeaturesHelp();
        return 1;
    }
}

public sealed record FeatureDefCliDto(string Id, string Code, string Name, string? ProductId, string? Description, string? MinVersion, string? MaxVersion, bool IsFloating, int? DefaultMaxSeats);
public sealed record PackageSuiteCliDto(string Id, string Code, string Name, string? ProductId, string? Description, IReadOnlyList<string> FeatureCodes);
public sealed record FeatureUsageMetricCliDto(string FeatureCode, string Name, string? ProductCode, int? MaxSeats, int InUseSeats, int DenialsCount, IReadOnlyList<string> ActiveLeaseIds);
