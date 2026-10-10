using System.Globalization;
using System.Net.Http.Json;
using Spectre.Console;
using Symbolon.Client.Agent;
using Symbolon.Protocol;

namespace Symbolon.Cli.Commands;

public static class AgentCommands
{
    public static async Task<int> HandleAgentAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintAgentHelp();
            return 0;
        }

        string sub = args[0].ToUpperInvariant();
        string[] subArgs = args[1..];

        return sub switch
        {
            "START" => await HandleStartAsync(subArgs).ConfigureAwait(false),
            "STATUS" => await HandleStatusAsync(subArgs).ConfigureAwait(false),
            "ACQUIRE" => await HandleAcquireAsync(subArgs).ConfigureAwait(false),
            "RELEASE" => await HandleReleaseAsync(subArgs).ConfigureAwait(false),
            "BORROW" => await HandleBorrowAsync(subArgs).ConfigureAwait(false),
            "TRAY" => await HandleTrayAsync(subArgs).ConfigureAwait(false),
            "STOP" => await HandleStopAsync(subArgs).ConfigureAwait(false),
            _ => UnknownSubcommand(args[0])
        };
    }

    private static async Task<int> HandleStartAsync(string[] args)
    {
        int port = GetIntArg(args, "--port", 8189);
        string server = GetArg(args, "--server") ?? "http://localhost:8080";
        string? licenseKey = GetArg(args, "--license") ?? GetArg(args, "-l");
        bool isDaemon = args.Contains("--daemon", StringComparer.OrdinalIgnoreCase);

        AnsiConsole.MarkupLine("[bold blue]=== Spúšťam Klientsky Agent Symbolon (symbolon-agent) ===[/]");

        Uri? serverUri = null;
        if (Uri.TryCreate(server, UriKind.Absolute, out var parsed))
        {
            serverUri = parsed;
        }

        await using var daemon = new SymbolonAgentDaemon(port, serverUri, licenseKey);
        try
        {
            daemon.Start();
            AnsiConsole.MarkupLine($"[green]✓ Agent úspešne spustený a počúva na:[/] [bold cyan]http://127.0.0.1:{port}/[/]");
            AnsiConsole.MarkupLine($"[grey]ID Lokálneho Stroja:[/] [white]{Markup.Escape(daemon.MachineId)}[/]");

            if (isDaemon || Console.IsInputRedirected)
            {
                AnsiConsole.MarkupLine("[grey]Agent beží v režime démona na pozadí.[/]");
                return 0;
            }

            AnsiConsole.MarkupLine("[yellow]Stlačte Ctrl+C alebo pošlite 'symbolon agent stop' pre ukončenie agenta.[/]");

            // Wait until cancelled
            var tcs = new TaskCompletionSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                tcs.TrySetResult();
            };

            await tcs.Task.ConfigureAwait(false);
            AnsiConsole.MarkupLine("[cyan]Agent riadne ukončený.[/]");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[bold red]Chyba spustenia agenta:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
    }

    private static async Task<int> HandleStatusAsync(string[] args)
    {
        int port = GetIntArg(args, "--port", 8189);

        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}/") };

        try
        {
            var res = await client.GetAsync(new Uri("/v1/status", UriKind.Relative)).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]Agent vrátil chybu ({res.StatusCode})[/]");
                return 1;
            }

            var status = await res.Content.ReadFromJsonAsync<AgentStatusDto>(SymbolonProtocolJsonContext.Default.AgentStatusDto).ConfigureAwait(false);
            if (status == null)
            {
                AnsiConsole.MarkupLine("[red]Neplatná odpoveď agenta.[/]");
                return 1;
            }

            var table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Cyan1);
            table.AddColumn("[bold white]Parameter[/]");
            table.AddColumn("[bold white]Hodnota[/]");

            string statusColor = status.Status switch
            {
                "active" => "green bold",
                "borrowed" => "yellow bold",
                "grace" => "magenta bold",
                _ => "grey"
            };

            table.AddRow("[cyan]Stav Agenta[/]", $"[{statusColor}]{status.Status.ToUpperInvariant()}[/]");
            table.AddRow("[cyan]Lokálny Port[/]", port.ToString(CultureInfo.InvariantCulture));
            table.AddRow("[cyan]ID Klienta / Machine[/]", Markup.Escape(status.MachineId));
            table.AddRow("[cyan]Licenčný Kľúč[/]", Markup.Escape(status.LicenseKey ?? "Nezadaný"));
            table.AddRow("[cyan]Aktívny Lease ID[/]", Markup.Escape(status.LeaseId ?? "Žiadny"));
            table.AddRow("[cyan]Číslo Sedadla[/]", status.SeatNo?.ToString(CultureInfo.InvariantCulture) ?? "-");
            table.AddRow("[cyan]Offline Roaming[/]", status.OfflineAllowed ? "[green]Povolený[/]" : "[grey]Zakázaný[/]");
            if (!string.IsNullOrWhiteSpace(status.LastError))
            {
                table.AddRow("[red]Posledná Chyba[/]", $"[red]{Markup.Escape(status.LastError)}[/]");
            }

            var panel = new Panel(table)
                .Header("[bold cyan] Symbolon Client Agent Status [/]");
            panel.Border = BoxBorder.Double;
            AnsiConsole.Write(panel);
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Agent nie je dostupný na porte {port}: {Markup.Escape(ex.Message)}[/]");
            return 1;
        }
    }

    private static async Task<int> HandleAcquireAsync(string[] args)
    {
        int port = GetIntArg(args, "--port", 8189);
        string? licenseKey = GetArg(args, "--license") ?? GetArg(args, "-l");

        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}/") };

        try
        {
            using var content = licenseKey != null ? JsonContent.Create(new { licenseKey }) : null;
            var res = await client.PostAsync(new Uri("/v1/acquire", UriKind.Relative), content).ConfigureAwait(false);
            var result = await res.Content.ReadFromJsonAsync<AgentActionResponseDto>(SymbolonProtocolJsonContext.Default.AgentActionResponseDto).ConfigureAwait(false);

            if (res.IsSuccessStatusCode && result?.Success == true)
            {
                AnsiConsole.MarkupLine($"[green]✓ {Markup.Escape(result.Message)}[/]");
                return 0;
            }

            AnsiConsole.MarkupLine($"[red]✗ Získanie zlyhalo: {Markup.Escape(result?.Message ?? res.ReasonPhrase ?? "Neznáma chyba")}[/]");
            return 1;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Chyba komunikácie s agentom: {Markup.Escape(ex.Message)}[/]");
            return 1;
        }
    }

    private static async Task<int> HandleReleaseAsync(string[] args)
    {
        int port = GetIntArg(args, "--port", 8189);

        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}/") };

        try
        {
            var res = await client.PostAsync(new Uri("/v1/release", UriKind.Relative), null).ConfigureAwait(false);
            var result = await res.Content.ReadFromJsonAsync<AgentActionResponseDto>(SymbolonProtocolJsonContext.Default.AgentActionResponseDto).ConfigureAwait(false);

            if (res.IsSuccessStatusCode && result?.Success == true)
            {
                AnsiConsole.MarkupLine($"[green]✓ {Markup.Escape(result.Message)}[/]");
                return 0;
            }

            AnsiConsole.MarkupLine($"[red]✗ Uvoľnenie zlyhalo: {Markup.Escape(result?.Message ?? res.ReasonPhrase ?? "Neznáma chyba")}[/]");
            return 1;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Chyba komunikácie s agentom: {Markup.Escape(ex.Message)}[/]");
            return 1;
        }
    }

    private static async Task<int> HandleBorrowAsync(string[] args)
    {
        int port = GetIntArg(args, "--port", 8189);
        int days = GetIntArg(args, "--days", 7);

        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}/") };

        try
        {
            using var content = JsonContent.Create(new { days });
            var res = await client.PostAsync(new Uri("/v1/borrow", UriKind.Relative), content).ConfigureAwait(false);
            var result = await res.Content.ReadFromJsonAsync<AgentActionResponseDto>(SymbolonProtocolJsonContext.Default.AgentActionResponseDto).ConfigureAwait(false);

            if (res.IsSuccessStatusCode && result?.Success == true)
            {
                AnsiConsole.MarkupLine($"[green]✓ {Markup.Escape(result.Message)}[/]");
                return 0;
            }

            AnsiConsole.MarkupLine($"[red]✗ Offline borrow zlyhalo: {Markup.Escape(result?.Message ?? res.ReasonPhrase ?? "Neznáma chyba")}[/]");
            return 1;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Chyba komunikácie s agentom: {Markup.Escape(ex.Message)}[/]");
            return 1;
        }
    }

    private static async Task<int> HandleTrayAsync(string[] args)
    {
        int port = GetIntArg(args, "--port", 8189);
        string? testTitle = GetArg(args, "--title") ?? "Symbolon Tray";
        string? testMsg = GetArg(args, "--message");

        var traySvc = new TrayNotificationService();

        if (!string.IsNullOrWhiteSpace(testMsg))
        {
            traySvc.Notify(testTitle, testMsg, TrayNotificationLevel.Info);
            AnsiConsole.MarkupLine($"[green]✓ Notifikácia odoslaná do systému:[/] {Markup.Escape(testTitle)} - {Markup.Escape(testMsg)}");
            return 0;
        }

        AnsiConsole.MarkupLine("[bold cyan]=== SYMBOLON DESKTOP TRAY NOTIFIKÁTOR & ROAMING MENU ===[/]");

        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}/") };

        try
        {
            var res = await client.GetAsync(new Uri("/v1/status", UriKind.Relative)).ConfigureAwait(false);
            if (res.IsSuccessStatusCode)
            {
                var status = await res.Content.ReadFromJsonAsync<AgentStatusDto>(SymbolonProtocolJsonContext.Default.AgentStatusDto).ConfigureAwait(false);
                if (status != null)
                {
                    var table = new Table();
                    table.Border = TableBorder.Rounded;
                    table.AddColumn("[bold]Parameter[/]");
                    table.AddColumn("[bold]Hodnota[/]");

                    table.AddRow("Stav Agenta", $"[bold cyan]{Markup.Escape(status.Status.ToUpperInvariant())}[/]");
                    table.AddRow("Stroj (Machine ID)", Markup.Escape(status.MachineId));
                    table.AddRow("Aktívny Lease", !string.IsNullOrWhiteSpace(status.LeaseId) ? $"[green]{Markup.Escape(status.LeaseId)}[/]" : "[grey]Žiaden[/]");
                    table.AddRow("Sedadlo", status.SeatNo?.ToString(CultureInfo.InvariantCulture) ?? "-");
                    table.AddRow("Offline Roaming", status.OfflineAllowed ? "[green]Povolený[/]" : "[grey]Zakázaný[/]");
                    if (status.ExpiresAt.HasValue)
                    {
                        var diff = status.ExpiresAt.Value - DateTimeOffset.UtcNow;
                        string diffStr = diff.TotalSeconds > 0 ? $"{diff.TotalMinutes:F1} minút" : "Expirovaný";
                        table.AddRow("Expirácia Lease", $"{status.ExpiresAt.Value:u} ({diffStr})");
                    }

                    AnsiConsole.Write(table);

                    var panel = new Panel(
                        new Markup("[bold yellow][[B]][/] Vypožičať licenciu na cesty (Offline Borrow)\n" +
                                   "[bold yellow][[R]][/] Uvoľniť pridelené sedadlo (Release Lease)\n" +
                                   "[bold yellow][[T]][/] Poslať testovaciu systémovú notifikáciu\n" +
                                   "[bold yellow][[Q]][/] Ukončiť zobrazenie tray manažéra")
                    );
                    panel.Header = new PanelHeader("[bold cyan] Roaming Borrow & Akcie [/]");
                    panel.Border = BoxBorder.Double;
                    AnsiConsole.Write(panel);

                    traySvc.Notify(
                        "Symbolon Tray Manažér",
                        $"Monitorovanie aktívne pre stroj {status.MachineId} (Lease: {status.LeaseId ?? "žiaden"})",
                        TrayNotificationLevel.Info
                    );
                    return 0;
                }
            }
            else
            {
                AnsiConsole.MarkupLine($"[yellow]Varovanie: Lokálny agent na porte {port} neodpovedá ({res.StatusCode}).[/]");
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[yellow]Lokálny agent na porte {port} nie je dostupný: {Markup.Escape(ex.Message)}[/]");
        }

        traySvc.Notify("Symbolon Tray", "Klientsky agent v pohotovostnom režime.", TrayNotificationLevel.Info);
        AnsiConsole.MarkupLine("[green]✓ Tray notifikačná služba inicializovaná.[/]");
        return 0;
    }

    private static async Task<int> HandleStopAsync(string[] args)
    {
        int port = GetIntArg(args, "--port", 8189);

        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}/") };

        try
        {
            var res = await client.PostAsync(new Uri("/v1/stop", UriKind.Relative), null).ConfigureAwait(false);
            if (res.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine("[green]✓ Požiadavka na zastavenie agenta bola úspešne odoslaná.[/]");
                return 0;
            }

            AnsiConsole.MarkupLine($"[red]Zastavenie zlyhalo ({res.StatusCode})[/]");
            return 1;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Chyba komunikácie s agentom: {Markup.Escape(ex.Message)}[/]");
            return 1;
        }
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

    private static int GetIntArg(string[] args, string flag, int defaultValue)
    {
        string? val = GetArg(args, flag);
        if (val != null && int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
        {
            return parsed;
        }
        return defaultValue;
    }

    private static int UnknownSubcommand(string sub)
    {
        AnsiConsole.MarkupLine($"[red]Neznámy príkaz: {Markup.Escape(sub)}[/]");
        PrintAgentHelp();
        return 1;
    }

    private static void PrintAgentHelp()
    {
        AnsiConsole.MarkupLine("[bold]Použitie:[/] symbolon agent <príkaz> [[prepínače]]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("Klientsky desktop a CLI tray agent (IPC daemon na 127.0.0.1:8189) pre automatický heartbeat a offline borrow.");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Príkazy:[/] ");
        AnsiConsole.MarkupLine("  start                     Spustenie lokálneho agenta na 127.0.0.1:8189");
        AnsiConsole.MarkupLine("  status                    Zobrazenie stavu lokálneho agenta a držaného lease");
        AnsiConsole.MarkupLine("  acquire                   Vyžiadanie a udržiavanie floating lease cez agenta");
        AnsiConsole.MarkupLine("  release                   Uvoľnenie držaného floating lease");
        AnsiConsole.MarkupLine("  borrow                    Vypožičanie sedadla pre offline prácu (--days <n>)");
        AnsiConsole.MarkupLine("  tray                      Desktop GUI tray notifikátor & roaming borrow menu");
        AnsiConsole.MarkupLine("  stop                      Ukončenie bežiaceho agent démona");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Prepínače:[/] ");
        AnsiConsole.MarkupLine("  --port <číslo>            Port lokálneho agenta (predvolené: 8189)");
        AnsiConsole.MarkupLine("  --server <url>            URL licenčného servera");
        AnsiConsole.MarkupLine("  -l, --license <kľúč>      Predvolený licenčný kľúč");
        AnsiConsole.MarkupLine("  --days <počet>            Dĺžka offline borrow v dňoch (1 až 30)");
        AnsiConsole.MarkupLine("  --daemon                  Spustenie v neinteraktívnom režime");
    }
}
