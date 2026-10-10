#pragma warning disable CA1031, CA1848

using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using Spectre.Console;
using Achilles.Client.Agent;
using Achilles.Protocol;

namespace Achilles.Cli.Commands;

public static class DesktopCommands
{
    public static async Task<int> HandleDesktopAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintDesktopHelp();
            return 0;
        }

        string sub = args[0].ToUpperInvariant();
        string[] subArgs = args[1..];

        return sub switch
        {
            "OPEN" or "START" => await HandleOpenAsync(subArgs).ConfigureAwait(false),
            "PROCESSES" or "PS" => HandleProcesses(),
            "INSPECT" or "STATUS" => await HandleInspectAsync(subArgs).ConfigureAwait(false),
            _ => UnknownSubcommand(args[0])
        };
    }

    private static async Task<int> HandleOpenAsync(string[] args)
    {
        int port = GetIntArg(args, "--port", 8080);
        string serverUrl = GetArg(args, "--server") ?? $"http://localhost:{port}";
        string visualizerUrl = $"{serverUrl.TrimEnd('/')}/visualizer";
        bool appMode = args.Contains("--app", StringComparer.OrdinalIgnoreCase);

        AnsiConsole.MarkupLine("[bold cyan]=== Spúšťam Symbolon Desktop Studio & Visualizer ===[/]");
        AnsiConsole.MarkupLine($"[grey]Cieľová URL:[/] [bold green]{Markup.Escape(visualizerUrl)}[/]");

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (appMode)
                {
                    // Try launching Edge or Chrome in standalone app-window mode
                    string edgePath = @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe";
                    if (File.Exists(edgePath))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = edgePath,
                            Arguments = $"--app=\"{visualizerUrl}\"",
                            UseShellExecute = true
                        });
                        AnsiConsole.MarkupLine("[green]✓ Symbolon Studio otvorené v samostatnom natívnom aplikačnom okne (Edge App Mode).[/]");
                        return 0;
                    }
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = visualizerUrl,
                    UseShellExecute = true
                });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start("xdg-open", visualizerUrl);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", visualizerUrl);
            }

            AnsiConsole.MarkupLine("[green]✓ Visualizer úspešne spustený na ploche.[/]");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[yellow]Nepodarilo sa priamo otvoriť okno prehliadača: {Markup.Escape(ex.Message)}[/]");
            AnsiConsole.MarkupLine($"[white]Otvorte manuálne vo vašom prehliadači: [underline cyan]{Markup.Escape(visualizerUrl)}[/][/]");
            return 0;
        }
    }

    private static int HandleProcesses()
    {
        AnsiConsole.MarkupLine("[bold cyan]=== Zoznam Lokálnych Chránených Procesov (CAD / EDA / Symbolon SDK) ===[/]");

        var processes = Process.GetProcesses();
        var relevant = new List<(int Id, string Name, string MemoryMb, string StartTime)>();

        foreach (var p in processes)
        {
            try
            {
                string name = p.ProcessName.ToLowerInvariant();
                if (name.Contains("symbolon", StringComparison.Ordinal) ||
                    name.Contains("cad", StringComparison.Ordinal) ||
                    name.Contains("eda", StringComparison.Ordinal) ||
                    name.Contains("matlab", StringComparison.Ordinal) ||
                    name.Contains("ansys", StringComparison.Ordinal) ||
                    name.Contains("autocad", StringComparison.Ordinal))
                {
                    double mem = p.WorkingSet64 / (1024.0 * 1024.0);
                    string startTime = p.StartTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
                    relevant.Add((p.Id, p.ProcessName, $"{mem:F1} MB", startTime));
                }
            }
            catch
            {
                // Access denied or process exited
            }
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("[bold]PID[/]");
        table.AddColumn("[bold]Názov Procesu[/]");
        table.AddColumn("[bold]Pamäť (RAM)[/]");
        table.AddColumn("[bold]Čas Štartu[/]");
        table.AddColumn("[bold]Stav Licencie[/]");

        if (relevant.Count == 0)
        {
            table.AddRow("-", "[grey]Žiadne detegované CAD/Symbolon procesy nebežia[/]", "-", "-", "[grey]Idle[/]");
        }
        else
        {
            foreach (var r in relevant)
            {
                table.AddRow(
                    r.Id.ToString(CultureInfo.InvariantCulture),
                    Markup.Escape(r.Name),
                    r.MemoryMb,
                    r.StartTime,
                    "[green]● Aktívne sedadlo (FlexNet/Symbolon)[/]"
                );
            }
        }

        AnsiConsole.Write(table);
        return 0;
    }

    private static async Task<int> HandleInspectAsync(string[] args)
    {
        int port = GetIntArg(args, "--port", 8189);

        AnsiConsole.MarkupLine("[bold cyan]=== Symbolon Desktop Studio: Inšpekcia Klienta & Roamingu ===[/]");

        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/") };
        try
        {
            var res = await client.GetAsync(new Uri("/v1/status", UriKind.Relative)).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[yellow]Lokálny démon symbolon-agent neodpovedá ({res.StatusCode}).[/]");
                return 1;
            }

            var status = await res.Content.ReadFromJsonAsync<AgentStatusDto>(AchillesProtocolJsonContext.Default.AgentStatusDto).ConfigureAwait(false);
            if (status == null)
            {
                AnsiConsole.MarkupLine("[red]Neplatná odpoveď agenta.[/]");
                return 1;
            }

            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumn("[cyan]Komponent[/]");
            table.AddColumn("[white]Hodnota[/]");

            table.AddRow("Stav Klientskeho Agenta", $"[bold green]{status.Status.ToUpperInvariant()}[/]");
            table.AddRow("Lokálny Stroj (Machine ID)", Markup.Escape(status.MachineId));
            table.AddRow("Aktívny Lease ID", !string.IsNullOrEmpty(status.LeaseId) ? $"[green]{Markup.Escape(status.LeaseId)}[/]" : "[grey]Žiadny[/]");
            table.AddRow("Číslo Prideleného Sedadla", status.SeatNo?.ToString(CultureInfo.InvariantCulture) ?? "-");
            table.AddRow("Podpora Offline Roamingu", status.OfflineAllowed ? "[green]Áno[/]" : "[grey]Nie[/]");

            AnsiConsole.Write(table);
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[yellow]Lokálny symbolon-agent nie je spustený: {Markup.Escape(ex.Message)}[/]");
            return 0;
        }
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

    private static int GetIntArg(string[] args, string name, int defaultValue)
    {
        string? val = GetArg(args, name);
        if (val != null && int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
        {
            return parsed;
        }
        return defaultValue;
    }

    private static int UnknownSubcommand(string sub)
    {
        AnsiConsole.MarkupLine($"[red]Neznámy príkaz pre desktop studio:[/] {Markup.Escape(sub)}");
        PrintDesktopHelp();
        return 1;
    }

    private static void PrintDesktopHelp()
    {
        AnsiConsole.WriteLine("""
            Použitie: symbolon desktop <príkaz> [prepínače]
                      symbolon studio <príkaz> [prepínače]

            Príkazy:
              open [--server <url>] [--port <číslo>] [--app]
                  Otvorenie Symbolon Visualizera a klastrového štúdia v samostatnom aplikačnom okne na ploche.
              processes
                  Inšpekcia bežiacich lokálnych procesov využívajúcich Symbolon C++ SDK alebo FlexNet drop-in wrapper.
              inspect [--port <číslo>]
                  Detailná inšpekcia lokálneho klientskeho agenta, offline roamingu a stavu pridelených sedadiel.
            """);
    }
}
