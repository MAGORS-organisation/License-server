using System.Globalization;
using System.Text.Json;
using Spectre.Console;
using Achilles.Client.Discovery;
using Achilles.Protocol;

namespace Achilles.Cli.Commands;

public static class DiscoverCommands
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task<int> HandleDiscoverAsync(string[] args)
    {
        if (args.Length > 0 && args[0] is "-h" or "--help")
        {
            PrintHelp();
            return 0;
        }

        if (args.Length > 0 && string.Equals(args[0], "resolve", StringComparison.OrdinalIgnoreCase))
        {
            return HandleResolve(args[1..]);
        }

        return await HandleProbeAsync(args).ConfigureAwait(false);
    }

    private static async Task<int> HandleProbeAsync(string[] args)
    {
        int timeoutMs = 1500;
        if (int.TryParse(GetArg(args, "--timeout"), CultureInfo.InvariantCulture, out int t) && t > 0)
        {
            timeoutMs = t;
        }

        int port = SymbolonDiscoveryConstants.DefaultPort;
        if (int.TryParse(GetArg(args, "--port"), CultureInfo.InvariantCulture, out int p) && p > 0)
        {
            port = p;
        }

        bool jsonOutput = HasFlag(args, "--json");
        string? productCode = GetArg(args, "--product");

        IReadOnlyList<DiscoveredServerInfo> discovered;

        if (jsonOutput)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs + 500));
            discovered = await SymbolonDiscoveryClient.DiscoverServersAsync(
                productCode,
                TimeSpan.FromMilliseconds(timeoutMs),
                port,
                ct: cts.Token).ConfigureAwait(false);

            string json = JsonSerializer.Serialize(discovered, JsonOptions);
            Console.WriteLine(json);
            return 0;
        }

        AnsiConsole.MarkupLine("[cyan bold]=== ZERO-CONFIG SERVER DISCOVERY (UDP BROADCAST) ===[/]");
        AnsiConsole.MarkupLine($"Odosielam broadcast sondu na UDP port [yellow]{port}[/] (časový limit: [yellow]{timeoutMs} ms[/])...\n");

        using (var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs + 500)))
        {
            discovered = await SymbolonDiscoveryClient.DiscoverServersAsync(
                productCode,
                TimeSpan.FromMilliseconds(timeoutMs),
                port,
                ct: cts.Token).ConfigureAwait(false);
        }

        if (discovered.Count > 0)
        {
            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumn("Node ID");
            table.AddColumn("Typ");
            table.AddColumn("URL Servera");
            table.AddColumn("Klaster");
            table.AddColumn("RTT Latencia");
            table.AddColumn("Verzia");
            table.AddColumn("Stav");

            foreach (var s in discovered)
            {
                string rtt = $"{s.RoundTripMs:F1} ms";
                string typeFormatted = string.Equals(s.Announcement.ServerType, "relay", StringComparison.OrdinalIgnoreCase)
                    ? "[yellow]Relay (Edge)[/]"
                    : "[cyan]ControlPlane[/]";

                table.AddRow(
                    $"[bold]{Markup.Escape(s.Announcement.NodeId)}[/]",
                    typeFormatted,
                    $"[link={Markup.Escape(s.Announcement.ServerUrl)}]{Markup.Escape(s.Announcement.ServerUrl)}[/]",
                    Markup.Escape(s.Announcement.ClusterId ?? "-"),
                    $"[green]{rtt}[/]",
                    Markup.Escape(s.Announcement.Version),
                    "[green]ONLINE (Aktívny)[/]");
            }

            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine($"\nNájdených serverov: [bold green]{discovered.Count}[/]");
        }
        else
        {
            AnsiConsole.MarkupLine("[yellow]V lokálnej sieti nebol detegovaný žiadny bežiaci server cez UDP broadcast.[/]");
        }

        // Show Environment Variable Resolution status
        string? envServer = Environment.GetEnvironmentVariable(SymbolonDiscoveryConstants.EnvironmentVariable);
        string? envServersAlt = Environment.GetEnvironmentVariable(SymbolonDiscoveryConstants.EnvironmentVariableAlt);

        AnsiConsole.MarkupLine("\n[dim]=== ENTERPRISE PREMENNÉ PROSTREDIA ===[/]");
        if (!string.IsNullOrWhiteSpace(envServer))
        {
            AnsiConsole.MarkupLine($"[green]{SymbolonDiscoveryConstants.EnvironmentVariable}[/]: {Markup.Escape(envServer)}");
            var resolved = SymbolonServerResolver.Resolve();
            foreach (var u in resolved)
            {
                AnsiConsole.MarkupLine($"  -> Rozpoznaný cieľový endpoint: [cyan]{Markup.Escape(u.ToString())}[/]");
            }
        }
        else if (!string.IsNullOrWhiteSpace(envServersAlt))
        {
            AnsiConsole.MarkupLine($"[green]{SymbolonDiscoveryConstants.EnvironmentVariableAlt}[/]: {Markup.Escape(envServersAlt)}");
            var resolved = SymbolonServerResolver.Resolve();
            foreach (var u in resolved)
            {
                AnsiConsole.MarkupLine($"  -> Rozpoznaný cieľový endpoint: [cyan]{Markup.Escape(u.ToString())}[/]");
            }
        }
        else
        {
            AnsiConsole.MarkupLine($"[dim]Premenné {SymbolonDiscoveryConstants.EnvironmentVariable} a {SymbolonDiscoveryConstants.EnvironmentVariableAlt} nie sú nastavené.[/]");
            AnsiConsole.MarkupLine("[dim]Tip: Nastavte premennú napr: set SYMBOLON_LICENSE_SERVER=27000@licserver.corp.local alebo https://lic1:5000;https://lic2:5000[/]");
        }

        return 0;
    }

    private static int HandleResolve(string[] args)
    {
        string? input = GetArg(args, "--server") ?? (args.Length > 0 ? args[0] : null);

        if (string.IsNullOrWhiteSpace(input))
        {
            // Resolve from active environment variables
            var envUris = SymbolonServerResolver.Resolve();
            AnsiConsole.MarkupLine("[cyan bold]=== ROZPOZNANIE ZO SYSTÉMOVÉHO PROSTREDIA ===[/]");
            if (envUris.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]Žiadne licenčné servery nie sú definované v premenných prostredia.[/]");
                return 0;
            }

            foreach (var u in envUris)
            {
                AnsiConsole.MarkupLine($"[green]✓[/] Endpoint: [bold]{Markup.Escape(u.ToString())}[/] (Schéma: {u.Scheme}, Host: {u.Host}, Port: {u.Port})");
            }
            return 0;
        }

        AnsiConsole.MarkupLine($"[cyan bold]=== TEST PARSOVANIA REŤAZCA SERVEROV: {Markup.Escape(input)} ===[/]");
        var uris = SymbolonServerResolver.Resolve(input, fallbackToEnvironment: false);

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Index");
        table.AddColumn("Rozpoznaná URL");
        table.AddColumn("Protokol");
        table.AddColumn("Host");
        table.AddColumn("Port");

        for (int i = 0; i < uris.Count; i++)
        {
            table.AddRow(
                (i + 1).ToString(CultureInfo.InvariantCulture),
                Markup.Escape(uris[i].ToString()),
                uris[i].Scheme,
                uris[i].Host,
                uris[i].Port.ToString(CultureInfo.InvariantCulture));
        }

        AnsiConsole.Write(table);
        return 0;
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
        return args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));
    }

    private static void PrintHelp()
    {
        AnsiConsole.MarkupLine("""
            [bold]Použitie:[/] symbolon discover [options]
                     symbolon servers [options]
                     symbolon servers resolve [string]

            Príkazy:
              discover                       Vyhľadá aktívne Symbolon servery v sieti cez UDP broadcast
              servers resolve [expr]         Otestuje parsovanie FlexNet port@host alebo URL reťazca

            Voľby:
              --timeout <ms>                 Časový limit čakania na odpovede (predvolené 1500 ms)
              --port <p>                     UDP port na vyhľadávanie (predvolené 7584)
              --json                         Výstup vo formáte JSON
              --product <code>               Filter pre kód produktu
            """);
    }
}
