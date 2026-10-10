using System.Globalization;
using System.Net.Http.Json;
using Spectre.Console;

namespace Achilles.Cli.Commands;

public static class TokenCommands
{
    public static async Task<int> HandleTokensAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintTokensHelp();
            return 0;
        }

        return args[0].ToUpperInvariant() switch
        {
            "WALLETS" or "LIST" => await HandleListWalletsAsync(args[1..]).ConfigureAwait(false),
            "CREATE" => await HandleCreateWalletAsync(args[1..]).ConfigureAwait(false),
            "CREDIT" => await HandleCreditWalletAsync(args[1..]).ConfigureAwait(false),
            "RATES" => await HandleListRatesAsync(args[1..]).ConfigureAwait(false),
            "SET-RATE" => await HandleSetRateAsync(args[1..]).ConfigureAwait(false),
            "BALANCE" => await HandleBalanceAsync(args[1..]).ConfigureAwait(false),
            _ => UnknownSubcommand(args[0])
        };
    }

    private static async Task<int> HandleListWalletsAsync(string[] args)
    {
        string serverEndpoint = GetArg(args, "--server") ?? "http://localhost:8080";
        string? tenantId = GetArg(args, "--tenant");

        AnsiConsole.MarkupLine("[bold blue]=== Symbolon Kreditové Peňaženky (Token Pools) ===[/]");

        using var client = CreateClient(serverEndpoint);
        string url = string.IsNullOrWhiteSpace(tenantId) ? "/admin/v1/tokens/wallets" : $"/admin/v1/tokens/wallets?tenantId={tenantId}";

        try
        {
            var response = await client.GetAsync(new Uri(url, UriKind.Relative)).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]Server vrátil kód: {response.StatusCode}[/]");
                return 1;
            }

            var wallets = await response.Content.ReadFromJsonAsync<List<WalletCliDto>>().ConfigureAwait(false) ?? [];

            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumn("[cyan]Kód[/]");
            table.AddColumn("[white]Názov[/]");
            table.AddColumn("[green]Zostatok[/]");
            table.AddColumn("[yellow]Rezervované[/]");
            table.AddColumn("[grey]Overdraft[/]");
            table.AddColumn("[blue]Stav[/]");

            foreach (var w in wallets)
            {
                table.AddRow(
                    w.Code,
                    w.Name,
                    w.Balance.ToString("N2", CultureInfo.InvariantCulture),
                    w.ReservedCredits.ToString("N2", CultureInfo.InvariantCulture),
                    w.OverdraftLimit.ToString("N2", CultureInfo.InvariantCulture),
                    w.State);
            }

            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine($"Celkovo peňaženiek: [bold]{wallets.Count.ToString(CultureInfo.InvariantCulture)}[/]");
            return 0;
        }
        catch (HttpRequestException ex)
        {
            AnsiConsole.MarkupLine($"[red]Chyba spojenia so serverom ({serverEndpoint}):[/] {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> HandleCreateWalletAsync(string[] args)
    {
        string serverEndpoint = GetArg(args, "--server") ?? "http://localhost:8080";
        string tenantId = GetArg(args, "--tenant") ?? "default";
        string? code = GetArg(args, "--code");
        string? name = GetArg(args, "--name");
        decimal initialCredits = decimal.TryParse(GetArg(args, "--credits"), CultureInfo.InvariantCulture, out var c) ? c : 1000m;
        decimal overdraft = decimal.TryParse(GetArg(args, "--overdraft"), CultureInfo.InvariantCulture, out var o) ? o : 0m;

        if (string.IsNullOrWhiteSpace(code))
        {
            if (Console.IsInputRedirected || !AnsiConsole.Profile.Capabilities.Interactive)
            {
                AnsiConsole.MarkupLine("[red]Chyba: Chýba povinný argument --code v neinteraktívnom režime.[/]");
                return 1;
            }
            code = AnsiConsole.Prompt(new TextPrompt<string>("Zadajte [green]kód peňaženky[/] (napr. WAL-CAD-PRO):"));
        }

        name ??= code;

        using var client = CreateClient(serverEndpoint);
        var payload = new { tenantId, code, name, initialCredits, overdraftLimit = overdraft };

        try
        {
            var res = await client.PostAsJsonAsync(new Uri("/admin/v1/tokens/wallets", UriKind.Relative), payload).ConfigureAwait(false);
            if (res.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[green]✓ Peňaženka '{code}' úspešne vytvorená s kreditom {initialCredits.ToString("N2", CultureInfo.InvariantCulture)}.[/]");
                return 0;
            }

            string err = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            AnsiConsole.MarkupLine($"[red]Chyba vytvorenia peňaženky:[/] {err}");
            return 1;
        }
        catch (HttpRequestException ex)
        {
            AnsiConsole.MarkupLine($"[red]Chyba spojenia:[/] {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> HandleCreditWalletAsync(string[] args)
    {
        string serverEndpoint = GetArg(args, "--server") ?? "http://localhost:8080";
        string? walletId = GetArg(args, "--wallet") ?? GetArg(args, "-w");
        string? amountStr = GetArg(args, "--amount") ?? GetArg(args, "-a");
        string reason = GetArg(args, "--reason") ?? "CLI credit allocation";

        if (string.IsNullOrWhiteSpace(walletId))
        {
            if (Console.IsInputRedirected || !AnsiConsole.Profile.Capabilities.Interactive)
            {
                AnsiConsole.MarkupLine("[red]Chyba: Chýba povinný argument --wallet v neinteraktívnom režime.[/]");
                return 1;
            }
            walletId = AnsiConsole.Prompt(new TextPrompt<string>("Zadajte [green]ID peňaženky[/]:"));
        }

        if (!decimal.TryParse(amountStr, CultureInfo.InvariantCulture, out decimal amount) || amount <= 0)
        {
            if (Console.IsInputRedirected || !AnsiConsole.Profile.Capabilities.Interactive)
            {
                AnsiConsole.MarkupLine("[red]Chyba: Chýba povinný kladný argument --amount v neinteraktívnom režime.[/]");
                return 1;
            }
            amount = AnsiConsole.Prompt(new TextPrompt<decimal>("Zadajte [green]počet kreditov[/] na dobitie:"));
        }

        using var client = CreateClient(serverEndpoint);
        var payload = new { amount, reason };

        try
        {
            var res = await client.PostAsJsonAsync(new Uri($"/admin/v1/tokens/wallets/{walletId}/credit", UriKind.Relative), payload).ConfigureAwait(false);
            if (res.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[green]✓ Peňaženka úspešne dobitá o {amount.ToString("N2", CultureInfo.InvariantCulture)} kreditov.[/]");
                return 0;
            }

            string err = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            AnsiConsole.MarkupLine($"[red]Chyba dobitia:[/] {err}");
            return 1;
        }
        catch (HttpRequestException ex)
        {
            AnsiConsole.MarkupLine($"[red]Chyba spojenia:[/] {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> HandleListRatesAsync(string[] args)
    {
        string serverEndpoint = GetArg(args, "--server") ?? "http://localhost:8080";
        string? tenantId = GetArg(args, "--tenant");

        AnsiConsole.MarkupLine("[bold blue]=== Symbolon Sadzobník Spotreby Tokenov (Rates) ===[/]");

        using var client = CreateClient(serverEndpoint);
        string url = string.IsNullOrWhiteSpace(tenantId) ? "/admin/v1/tokens/rates" : $"/admin/v1/tokens/rates?tenantId={tenantId}";

        try
        {
            var response = await client.GetAsync(new Uri(url, UriKind.Relative)).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]Server vrátil kód: {response.StatusCode}[/]");
                return 1;
            }

            var rates = await response.Content.ReadFromJsonAsync<List<RateCliDto>>().ConfigureAwait(false) ?? [];

            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumn("[cyan]Funkcia (Feature)[/]");
            table.AddColumn("[green]Sadzba / Min[/]");
            table.AddColumn("[yellow]Sadzba / Job[/]");
            table.AddColumn("[white]Popis[/]");

            foreach (var r in rates)
            {
                table.AddRow(
                    r.FeatureCode,
                    r.RatePerMinute.ToString("N4", CultureInfo.InvariantCulture),
                    r.RatePerUnit.ToString("N4", CultureInfo.InvariantCulture),
                    r.Description);
            }

            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine($"Celkovo sadzieb: [bold]{rates.Count.ToString(CultureInfo.InvariantCulture)}[/]");
            return 0;
        }
        catch (HttpRequestException ex)
        {
            AnsiConsole.MarkupLine($"[red]Chyba spojenia:[/] {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> HandleSetRateAsync(string[] args)
    {
        string serverEndpoint = GetArg(args, "--server") ?? "http://localhost:8080";
        string tenantId = GetArg(args, "--tenant") ?? "default";
        string? feature = GetArg(args, "--feature");
        decimal rateMin = decimal.TryParse(GetArg(args, "--rate-min"), CultureInfo.InvariantCulture, out var rm) ? rm : 0m;
        decimal rateUnit = decimal.TryParse(GetArg(args, "--rate-unit"), CultureInfo.InvariantCulture, out var ru) ? ru : 1.0m;
        string desc = GetArg(args, "--desc") ?? "Metered task rate";

        if (string.IsNullOrWhiteSpace(feature))
        {
            if (Console.IsInputRedirected || !AnsiConsole.Profile.Capabilities.Interactive)
            {
                AnsiConsole.MarkupLine("[red]Chyba: Chýba povinný argument --feature v neinteraktívnom režime.[/]");
                return 1;
            }
            feature = AnsiConsole.Prompt(new TextPrompt<string>("Zadajte [green]kód funkcie[/] (napr. FEA-SOLVER):"));
        }

        using var client = CreateClient(serverEndpoint);
        var payload = new { tenantId, featureCode = feature, ratePerMinute = rateMin, ratePerUnit = rateUnit, description = desc };

        try
        {
            var res = await client.PostAsJsonAsync(new Uri("/admin/v1/tokens/rates", UriKind.Relative), payload).ConfigureAwait(false);
            if (res.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[green]✓ Sadzba pre funkciu '{feature}' úspešne nastavená: {rateMin.ToString("N4", CultureInfo.InvariantCulture)}/min, {rateUnit.ToString("N4", CultureInfo.InvariantCulture)}/job.[/]");
                return 0;
            }

            string err = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            AnsiConsole.MarkupLine($"[red]Chyba uloženia sadzby:[/] {err}");
            return 1;
        }
        catch (HttpRequestException ex)
        {
            AnsiConsole.MarkupLine($"[red]Chyba spojenia:[/] {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> HandleBalanceAsync(string[] args)
    {
        string serverEndpoint = GetArg(args, "--server") ?? "http://localhost:8080";
        string? walletId = GetArg(args, "--wallet") ?? GetArg(args, "-w");

        if (string.IsNullOrWhiteSpace(walletId))
        {
            if (Console.IsInputRedirected || !AnsiConsole.Profile.Capabilities.Interactive)
            {
                AnsiConsole.MarkupLine("[red]Chyba: Chýba povinný argument --wallet v neinteraktívnom režime.[/]");
                return 1;
            }
            walletId = AnsiConsole.Prompt(new TextPrompt<string>("Zadajte [green]ID peňaženky[/]:"));
        }

        using var client = CreateClient(serverEndpoint);

        try
        {
            var res = await client.GetAsync(new Uri($"/v1/tokens/wallets/{walletId}/balance", UriKind.Relative)).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]Peňaženka '{walletId}' nebola nájdená.[/]");
                return 1;
            }

            var b = await res.Content.ReadFromJsonAsync<BalanceCliDto>().ConfigureAwait(false);
            if (b is null) return 1;

            AnsiConsole.MarkupLine($"[bold blue]Peňaženka:[/] [cyan]{b.Code}[/] ({b.Name})");
            AnsiConsole.MarkupLine($"Disponibilný zostatok: [bold green]{b.AvailableCredits.ToString("N2", CultureInfo.InvariantCulture)}[/] kreditov");
            AnsiConsole.MarkupLine($"Rezervované v úlohách: [bold yellow]{b.ReservedCredits.ToString("N2", CultureInfo.InvariantCulture)}[/] kreditov");
            AnsiConsole.MarkupLine($"Overdraft buffer:      [grey]{b.OverdraftLimit.ToString("N2", CultureInfo.InvariantCulture)}[/] kreditov");
            AnsiConsole.MarkupLine($"Stav:                  {(b.IsLowBalance ? "[red]● NÍZKY ZOSTATOK[/]" : "[green]● OK[/]")}");
            return 0;
        }
        catch (HttpRequestException ex)
        {
            AnsiConsole.MarkupLine($"[red]Chyba spojenia:[/] {ex.Message}");
            return 1;
        }
    }

    private static HttpClient CreateClient(string serverEndpoint)
    {
        return new HttpClient
        {
            BaseAddress = new Uri(serverEndpoint),
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    private static void PrintTokensHelp()
    {
        Console.WriteLine("""
            Použitie: symbolon tokens <subcommand> [options]

            Subcommands:
              wallets | list [--server <url>] [--tenant <id>]     Zoznam kreditových peňaženiek
              create --code <c> --name <n> [--credits <n>]        Vytvorenie novej peňaženky
              credit --wallet <id> --amount <n> [--reason <t>]    Dobitie kreditu do peňaženky
              rates [--server <url>] [--tenant <id>]              Výpis sadzobníka funkcií
              set-rate --feature <f> [--rate-min <n>]             Nastavenie spotrebnej sadzby
              balance --wallet <id> [--server <url>]              Zistenie disponibilného zostatku
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
        AnsiConsole.MarkupLine($"[red]Neznámy príkaz pre tokeny:[/] {sub}");
        PrintTokensHelp();
        return 1;
    }
}

public sealed record WalletCliDto(string Id, string Code, string Name, decimal Balance, decimal TotalCredits, decimal ReservedCredits, decimal OverdraftLimit, string State);
public sealed record RateCliDto(string FeatureCode, decimal RatePerMinute, decimal RatePerUnit, string Description);
public sealed record BalanceCliDto(string WalletId, string Code, string Name, decimal AvailableCredits, decimal ReservedCredits, decimal OverdraftLimit, bool IsLowBalance);
