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
            "COMPANION" => await HandleCompanionAsync(subArgs).ConfigureAwait(false),
            "BORROW" => await HandleBorrowAsync(subArgs).ConfigureAwait(false),
            "RETURN" => await HandleReturnAsync(subArgs).ConfigureAwait(false),
            "NOTIFY" => HandleNotify(subArgs),
            _ => UnknownSubcommand(args[0])
        };
    }

    private static async Task<int> HandleOpenAsync(string[] args)
    {
        int port = GetIntArg(args, "--port", 8080);
        string serverUrl = GetArg(args, "--server") ?? $"http://localhost:{port}";
        string visualizerUrl = $"{serverUrl.TrimEnd('/')}/visualizer";
        bool appMode = args.Contains("--app", StringComparer.OrdinalIgnoreCase);

        AnsiConsole.MarkupLine("[bold cyan]=== Spúšťam Achilles Desktop Studio & Visualizer ===[/]");
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
                        AnsiConsole.MarkupLine("[green]✓ Achilles Studio otvorené v samostatnom natívnom aplikačnom okne (Edge App Mode).[/]");
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
        AnsiConsole.MarkupLine("[bold cyan]=== Zoznam Lokálnych Chránených Procesov (CAD / EDA / Achilles SDK) ===[/]");

        var processes = Process.GetProcesses();
        var relevant = new List<(int Id, string Name, string MemoryMb, string StartTime)>();

        foreach (var p in processes)
        {
            try
            {
                string name = p.ProcessName.ToLowerInvariant();
                if (name.Contains("achilles", StringComparison.Ordinal) ||
                    name.Contains("symbolon", StringComparison.Ordinal) ||
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
            table.AddRow("-", "[grey]Žiadne detegované CAD/Achilles procesy nebežia[/]", "-", "-", "[grey]Idle[/]");
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
                    "[green]● Aktívne sedadlo (FlexNet/Achilles)[/]"
                );
            }
        }

        AnsiConsole.Write(table);
        return 0;
    }

    private static async Task<int> HandleInspectAsync(string[] args)
    {
        int port = GetIntArg(args, "--port", 8189);

        AnsiConsole.MarkupLine("[bold cyan]=== Achilles Desktop Studio: Inšpekcia Klienta & Roamingu ===[/]");

        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}/") };
        try
        {
            var res = await client.GetAsync(new Uri("/v1/status", UriKind.Relative)).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[yellow]Lokálny démon achilles-agent neodpovedá ({res.StatusCode}).[/]");
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
            AnsiConsole.MarkupLine($"[yellow]Lokálny achilles-agent nie je spustený: {Markup.Escape(ex.Message)}[/]");
            return 0;
        }
    }

    private static async Task<int> HandleCompanionAsync(string[] args)
    {
        int port = GetIntArg(args, "--port", 8189);
        bool notify = args.Contains("--notify", StringComparer.OrdinalIgnoreCase);
        bool once = args.Contains("--once", StringComparer.OrdinalIgnoreCase);
        int pollMs = GetIntArg(args, "--poll-ms", 1000);

        AnsiConsole.MarkupLine("[bold cyan]╔════════════════════════════════════════════════════════════════╗[/]");
        AnsiConsole.MarkupLine("[bold cyan]║       ACHILLES WORKSTATION COMPANION & TRAY MONITOR           ║[/]");
        AnsiConsole.MarkupLine("[bold cyan]╚════════════════════════════════════════════════════════════════╝[/]");
        AnsiConsole.MarkupLine($"[grey]Port agenta:[/] [white]{port}[/] | [grey]Notifikácie:[/] {(notify ? "[green]Zapnuté[/]" : "[grey]Vypnuté[/]")}");

        var traySvc = notify ? new TrayNotificationService() : null;
        string? previousStatus = null;

        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}/") };

        do
        {
            try
            {
                var res = await client.GetAsync(new Uri("/v1/status", UriKind.Relative)).ConfigureAwait(false);
                if (res.IsSuccessStatusCode)
                {
                    var status = await res.Content.ReadFromJsonAsync<AgentStatusDto>(AchillesProtocolJsonContext.Default.AgentStatusDto).ConfigureAwait(false);
                    if (status != null)
                    {
                        string currentStatus = status.Status.ToUpperInvariant();
                        string badge = currentStatus switch
                        {
                            "ACTIVE" => "[bold black on green] AKTÍVNA LICENCIA [/]",
                            "BORROWED" => "[bold black on yellow] OFFLINE BORROW [/]",
                            "GRACE" => "[bold black on red] OFFLINE GRACE OCHRANA [/]",
                            _ => "[bold white on grey] STANDBY / IDLE [/]"
                        };

                        AnsiConsole.MarkupLine($"[grey][[{DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture)}]][/] Stav: {badge} | Stroj: [white]{Markup.Escape(status.MachineId)}[/] | Lease: [cyan]{Markup.Escape(status.LeaseId ?? "N/A")}[/]");

                        if (status.ExpiresAt.HasValue)
                        {
                            var rem = status.ExpiresAt.Value - DateTimeOffset.UtcNow;
                            string remFormatted = rem.TotalSeconds > 0 ? $"{rem.Hours}h {rem.Minutes}m {rem.Seconds}s" : "EXPIRUJÚCA";
                            AnsiConsole.MarkupLine($"         [grey]Zostávajúca platnosť sedadla:[/] [bold yellow]{remFormatted}[/]");
                        }

                        if (notify && traySvc != null && previousStatus != null && !string.Equals(previousStatus, currentStatus, StringComparison.OrdinalIgnoreCase))
                        {
                            traySvc.Notify(
                                "Achilles: Zmena Stavu Sedadla",
                                $"Stav zmenený na: {currentStatus} (Lease: {status.LeaseId ?? "N/A"})",
                                currentStatus == "ACTIVE" ? TrayNotificationLevel.Success : TrayNotificationLevel.Warning
                            );
                        }

                        previousStatus = currentStatus;
                    }
                }
                else
                {
                    AnsiConsole.MarkupLine($"[grey][[{DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture)}]][/] [yellow]Agent neodpovedá ({res.StatusCode})[/]");
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[grey][[{DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture)}]][/] [grey]Agent offline: {Markup.Escape(ex.Message)}[/]");
            }

            if (once)
            {
                break;
            }

            await Task.Delay(pollMs).ConfigureAwait(false);
        } while (!Console.IsInputRedirected && pollMs > 0);

        return 0;
    }

    private static async Task<int> HandleBorrowAsync(string[] args)
    {
        int port = GetIntArg(args, "--port", 8189);
        int days = GetIntArg(args, "--days", 7);

        AnsiConsole.MarkupLine($"[bold cyan]=== Achilles Offline Roaming: Výpožička Sedadla na {days} Dní ===[/]");

        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}/") };
        try
        {
            using var content = JsonContent.Create(new { days });
            var res = await client.PostAsync(new Uri("/v1/borrow", UriKind.Relative), content).ConfigureAwait(false);
            var result = await res.Content.ReadFromJsonAsync<AgentActionResponseDto>(AchillesProtocolJsonContext.Default.AgentActionResponseDto).ConfigureAwait(false);

            if (res.IsSuccessStatusCode && result?.Success == true)
            {
                AnsiConsole.MarkupLine($"[bold green]✓ Sedadlo úspešne vypožičané na {days} dní![/]");
                AnsiConsole.MarkupLine($"[white]{Markup.Escape(result.Message)}[/]");
                return 0;
            }

            AnsiConsole.MarkupLine($"[red]✗ Výpožička zlyhala: {Markup.Escape(result?.Message ?? res.ReasonPhrase ?? "Neznáma chyba")}[/]");
            return 1;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Chyba komunikácie s agentom: {Markup.Escape(ex.Message)}[/]");
            return 1;
        }
    }

    private static async Task<int> HandleReturnAsync(string[] args)
    {
        int port = GetIntArg(args, "--port", 8189);

        AnsiConsole.MarkupLine("[bold cyan]=== Achilles: Predčasné Vrátenie Sedadla do Poolu ===[/]");

        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}/") };
        try
        {
            var res = await client.PostAsync(new Uri("/v1/release", UriKind.Relative), null).ConfigureAwait(false);
            var result = await res.Content.ReadFromJsonAsync<AgentActionResponseDto>(AchillesProtocolJsonContext.Default.AgentActionResponseDto).ConfigureAwait(false);

            if (res.IsSuccessStatusCode && result?.Success == true)
            {
                AnsiConsole.MarkupLine($"[bold green]✓ Sedadlo vrátené do licenčného poolu:[/] {Markup.Escape(result.Message)}");
                return 0;
            }

            AnsiConsole.MarkupLine($"[red]✗ Vrátenie zlyhalo: {Markup.Escape(result?.Message ?? res.ReasonPhrase ?? "Neznáma chyba")}[/]");
            return 1;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Chyba komunikácie s agentom: {Markup.Escape(ex.Message)}[/]");
            return 1;
        }
    }

    private static int HandleNotify(string[] args)
    {
        string title = GetArg(args, "--title") ?? "Achilles Workstation Notification";
        string message = GetArg(args, "--message") ?? "Testovacia správa z klientskeho desktop asistenta.";
        string levelStr = (GetArg(args, "--level") ?? "info").ToUpperInvariant();

        var level = levelStr switch
        {
            "WARNING" or "WARN" => TrayNotificationLevel.Warning,
            "ERROR" or "ERR" => TrayNotificationLevel.Error,
            "SUCCESS" or "OK" => TrayNotificationLevel.Success,
            _ => TrayNotificationLevel.Info
        };

        var tray = new TrayNotificationService();
        tray.Notify(title, message, level);
        AnsiConsole.MarkupLine($"[bold green]✓ Natívna notifikácia odoslaná:[/] [[{Markup.Escape(title)}]] {Markup.Escape(message)}");
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
        AnsiConsole.MarkupLine($"[red]Neznámy príkaz pre desktop companion:[/] {Markup.Escape(sub)}");
        PrintDesktopHelp();
        return 1;
    }

    private static void PrintDesktopHelp()
    {
        AnsiConsole.WriteLine("""
            Použitie: achilles desktop <príkaz> [prepínače]
                      achilles studio <príkaz> [prepínače]

            Príkazy:
              open [--server <url>] [--port <číslo>] [--app]
                  Otvorenie Achilles Visualizera a klastrového štúdia v samostatnom okne.
              companion [--port <číslo>] [--notify] [--once] [--poll-ms <ms>]
                  Spustenie workstation asistenta: živý monitor lease heartbeat, grace countdown a tray notifikácie.
              borrow [--days <číslo>] [--port <číslo>]
                  Offline výpožička (roaming) sedadla na N dní pre prácu v teréne bez internetu.
              return [--port <číslo>]
                  Predčasné vrátenie vypožičaného alebo aktívneho sedadla späť do licenčného poolu.
              notify --title <titulok> --message <správa> [--level <info|warning|error|success>]
                  Manuálne odoslanie natívnej systémovej tray notifikácie do operačného systému.
              processes
                  Inšpekcia bežiacich lokálnych procesov využívajúcich Achilles C++ SDK alebo FlexNet drop-in wrapper.
              inspect [--port <číslo>]
                  Detailná inšpekcia lokálneho klientskeho agenta, offline roamingu a stavu pridelených sedadiel.
            """);
    }
}
