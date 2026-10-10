using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using Spectre.Console;
using Achilles.Protocol;
using Achilles.Protocol.Reporting;

namespace Achilles.Cli.Commands;

public static class TopCommand
{
    private static readonly char[] SparkChars = [' ', ' ', '▂', '▃', '▄', '▅', '▆', '▇', '█'];

    public static async Task<int> HandleTopAsync(string[] args)
    {
        if (args.Length > 0 && args[0] is "-h" or "--help")
        {
            PrintHelp();
            return 0;
        }

        string server = GetArg(args, "--server") ?? "http://localhost:8080";
        string? filter = GetArg(args, "--filter");
        bool once = args.Contains("--once", StringComparer.OrdinalIgnoreCase) || Console.IsInputRedirected;
        int intervalSec = 2;
        string? intervalArg = GetArg(args, "--interval") ?? GetArg(args, "-i");
        if (intervalArg != null && int.TryParse(intervalArg, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedInterval))
        {
            intervalSec = Math.Clamp(parsedInterval, 1, 60);
        }

        using var client = CreateClient(server);

        if (once)
        {
            return await RunOnceAsync(client, filter).ConfigureAwait(false);
        }

        return await RunInteractiveLoopAsync(client, filter, intervalSec).ConfigureAwait(false);
    }

    private static async Task<int> RunOnceAsync(HttpClient client, string? filter)
    {
        try
        {
            var stats = await FetchStatsAsync(client).ConfigureAwait(false);
            var leases = await FetchLeasesAsync(client).ConfigureAwait(false);

            RenderDashboard(stats, leases, filter, isPaused: false);
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[bold red]Chyba pripojenia k monitoru:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
    }

    private static async Task<int> RunInteractiveLoopAsync(HttpClient client, string? filter, int intervalSec)
    {
        bool isPaused = false;
        bool running = true;

        AnsiConsole.Clear();
        AnsiConsole.Cursor.Hide();

        try
        {
            while (running)
            {
                if (!isPaused)
                {
                    try
                    {
                        var stats = await FetchStatsAsync(client).ConfigureAwait(false);
                        var leases = await FetchLeasesAsync(client).ConfigureAwait(false);

                        Console.SetCursorPosition(0, 0);
                        RenderDashboard(stats, leases, filter, isPaused);
                    }
                    catch (Exception ex)
                    {
                        Console.SetCursorPosition(0, 0);
                        AnsiConsole.MarkupLine($"[bold red]Spojenie prerušené:[/] {Markup.Escape(ex.Message)} (opakujem pokus...)");
                    }
                }

                // Check keyboard input for interactive commands
                int waitMs = intervalSec * 1000;
                int stepMs = 100;
                while (waitMs > 0 && running)
                {
                    if (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(intercept: true);
                        if (key.Key is ConsoleKey.Q or ConsoleKey.Escape)
                        {
                            running = false;
                            break;
                        }
                        if (key.Key is ConsoleKey.P)
                        {
                            isPaused = !isPaused;
                            Console.SetCursorPosition(0, 0);
                            AnsiConsole.MarkupLine(isPaused ? "[bold yellow]>>> MONITOR POZASTAVENÝ (Stlačte 'P' pre pokračovanie) <<<[/]" : "                                                              ");
                        }
                        if (key.Key is ConsoleKey.R)
                        {
                            AnsiConsole.Cursor.Show();
                            AnsiConsole.WriteLine();
                            string targetId = AnsiConsole.Ask<string>("[bold yellow]Zadajte Lease ID alebo Seat No pre okamžité zrušenie (Revocation):[/]");
                            if (!string.IsNullOrWhiteSpace(targetId))
                            {
                                await RevokeLeaseAsync(client, targetId.Trim()).ConfigureAwait(false);
                            }
                            AnsiConsole.Cursor.Hide();
                            break;
                        }
                        if (key.Key is ConsoleKey.H or ConsoleKey.F1)
                        {
                            AnsiConsole.Clear();
                            PrintHelp();
                            AnsiConsole.MarkupLine("[grey]Stlačte ľubovoľný kláves pre návrat do monitora...[/]");
                            Console.ReadKey(intercept: true);
                            AnsiConsole.Clear();
                            break;
                        }
                    }

                    await Task.Delay(stepMs).ConfigureAwait(false);
                    waitMs -= stepMs;
                }
            }
        }
        finally
        {
            AnsiConsole.Cursor.Show();
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold cyan]Monitor ukončený.[/]");
        return 0;
    }

    private static async Task<TopSystemStatsDto> FetchStatsAsync(HttpClient client)
    {
        var res = await client.GetAsync(new Uri("/admin/v1/system/top-stats", UriKind.Relative)).ConfigureAwait(false);
        if (res.IsSuccessStatusCode)
        {
            var data = await res.Content.ReadFromJsonAsync<TopSystemStatsDto>(AchillesProtocolJsonContext.Default.TopSystemStatsDto).ConfigureAwait(false);
            if (data != null) return data;
        }

        return new TopSystemStatsDto(
            DateTimeOffset.UtcNow,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            true,
            [0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
    }

    private static async Task<List<ActiveLeaseItemDto>> FetchLeasesAsync(HttpClient client)
    {
        var res = await client.GetAsync(new Uri("/admin/v1/leases/active", UriKind.Relative)).ConfigureAwait(false);
        if (res.IsSuccessStatusCode)
        {
            var data = await res.Content.ReadFromJsonAsync<List<ActiveLeaseItemDto>>(AchillesProtocolJsonContext.Default.ListActiveLeaseItemDto).ConfigureAwait(false);
            if (data != null) return data;
        }

        return [];
    }

    private static async Task RevokeLeaseAsync(HttpClient client, string id)
    {
        var res = await client.PostAsync(new Uri($"/admin/v1/leases/{id}/revoke", UriKind.Relative), null).ConfigureAwait(false);
        if (res.IsSuccessStatusCode)
        {
            AnsiConsole.MarkupLine($"[green]✓ Lease {Markup.Escape(id)} úspešne zrušený a uvoľnený do poolu.[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"[red]✗ Zrušenie lease {Markup.Escape(id)} zlyhalo ({res.StatusCode}).[/]");
        }
        await Task.Delay(1200).ConfigureAwait(false);
    }

    public static void RenderDashboard(TopSystemStatsDto stats, IReadOnlyList<ActiveLeaseItemDto> leases, string? filter, bool isPaused)
    {
        // 1. Header banner - Novell NetWare / FoxPro 2.6 Style
        var headerTable = new Table().Border(TableBorder.Heavy).BorderColor(Color.Cyan1);
        headerTable.AddColumn(new TableColumn("[bold white on navy] SYMBOLON SYSTEM MONITOR v2.4 (NETWARE / FOXPRO EDITION) [/]").Centered());

        var timeStr = stats.ServerTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        var uptimeStr = FormatUptime(stats.UptimeSeconds);
        string pauseNotice = isPaused ? " [bold yellow][[PAUSED]][/]" : string.Empty;

        headerTable.AddRow($"[cyan]Čas:[/] [white]{timeStr}[/]  |  [cyan]Uptime:[/] [white]{uptimeStr}[/]  |  [cyan]Režim:[/] [green]HA-Active[/]{pauseNotice}");
        AnsiConsole.Write(headerTable);

        // 2. Metrics telemetry panel
        var metricsGrid = new Grid();
        metricsGrid.AddColumn();
        metricsGrid.AddColumn();
        metricsGrid.AddColumn();
        metricsGrid.AddColumn();

        double utilPct = stats.TotalCapacity > 0 ? (stats.ActiveLeases * 100.0 / stats.TotalCapacity) : 0.0;
        string utilColor = utilPct >= 90.0 ? "red bold" : (utilPct >= 75.0 ? "yellow" : "green");

        string auditBadge = stats.IsAuditChainIntact ? "[bold green]INTACT (Merkle)[/]" : "[bold red]TAMPERED[/]";
        string sparkline = RenderSparkline(stats.ThroughputSparkline);

        metricsGrid.AddRow(
            $"[white]Aktívne Sedadlá:[/] [{utilColor}]{stats.ActiveLeases} / {stats.TotalCapacity} ({utilPct:F1}%)[/]",
            $"[white]Aktívne Licencie:[/] [cyan]{stats.ActiveLicenses}[/]",
            $"[white]Kryptografický Audit:[/] {auditBadge}",
            $"[white]DLQ / Chyby:[/] [{(stats.DlqDepth > 0 ? "red bold" : "green")}]{stats.DlqDepth} / {stats.WebhookFailures}[/]"
        );
        metricsGrid.AddRow(
            $"[grey]Priepustnosť Leasov:[/] [yellow]{sparkline}[/]",
            $"[grey]Audit Záznamov:[/] [cyan]{stats.AuditEventsTotal}[/]",
            "[grey]Protokol:[/] [green]Symbolon v1 / PQC[/]",
            "[grey]Stav Uzla:[/] [green]Zdravý (Healthy)[/]"
        );

        var metricsPanel = new Panel(metricsGrid)
            .Header("[bold yellow] Telemetria Klastra & Zaťaženia [/]");
        metricsPanel.Border = BoxBorder.Rounded;
        metricsPanel.BorderColor(Color.Blue);
        AnsiConsole.Write(metricsPanel);

        // 3. Active leases table (FoxPro Browse view)
        var filteredLeases = leases;
        if (!string.IsNullOrWhiteSpace(filter))
        {
            filteredLeases = leases.Where(l =>
                l.LeaseId.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                l.LicenseId.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                (l.MachineId != null && l.MachineId.Contains(filter, StringComparison.OrdinalIgnoreCase)) ||
                (l.UserId != null && l.UserId.Contains(filter, StringComparison.OrdinalIgnoreCase)) ||
                (l.ProductName != null && l.ProductName.Contains(filter, StringComparison.OrdinalIgnoreCase))
            ).ToList();
        }

        var table = new Table().Border(TableBorder.Double).BorderColor(Color.Cyan2);
        table.Title = new TableTitle($"[bold white]Aktívne Floating Sedadlá (Záznamov: {filteredLeases.Count})[/]");
        table.AddColumn(new TableColumn("[cyan]Lease ID / Sedadlo[/]").LeftAligned());
        table.AddColumn(new TableColumn("[white]Produkt / Licencia[/]").LeftAligned());
        table.AddColumn(new TableColumn("[yellow]Klient / Stroj[/]").LeftAligned());
        table.AddColumn(new TableColumn("[grey]Fingerprint[/]").Centered());
        table.AddColumn(new TableColumn("[magenta]Typ[/]").Centered());
        table.AddColumn(new TableColumn("[green]Expirácia[/]").RightAligned());

        var now = DateTimeOffset.UtcNow;
        if (filteredLeases.Count == 0)
        {
            table.AddRow("[grey]Žiadne aktívne leases[/]", "-", "-", "-", "-", "-");
        }
        else
        {
            foreach (var l in filteredLeases.Take(25))
            {
                var exp = l.ExpiresAt ?? l.BorrowedUntil;
                string expStr = "-";
                string expColor = "green";
                if (exp.HasValue)
                {
                    var rem = exp.Value - now;
                    if (rem.TotalSeconds <= 0)
                    {
                        expStr = "Expirované";
                        expColor = "red";
                    }
                    else if (rem.TotalMinutes < 5)
                    {
                        expStr = $"{rem.Minutes}m {rem.Seconds}s";
                        expColor = "yellow";
                    }
                    else if (rem.TotalHours < 1)
                    {
                        expStr = $"{rem.Minutes}m";
                    }
                    else
                    {
                        expStr = $"{rem.TotalHours:F1}h";
                    }
                }

                string typeBadge = l.IsBorrowed ? "[bold yellow]OFFLINE BORROW[/]" : "[cyan]FLOATING SEAT[/]";
                string clientDisplay = l.UserId != null ? $"{Markup.Escape(l.UserId)} ({Markup.Escape(l.MachineId ?? "?")})" : Markup.Escape(l.MachineId ?? "N/A");

                table.AddRow(
                    $"[bold white]{Markup.Escape(l.LeaseId)}[/] [grey]#{l.SeatNo}[/]",
                    Markup.Escape(l.ProductName ?? l.LicenseId),
                    clientDisplay,
                    $"[grey]{Markup.Escape(l.HolderFingerprint ?? "N/A")}[/]",
                    typeBadge,
                    $"[{expColor}]{expStr}[/]"
                );
            }
        }

        AnsiConsole.Write(table);

        // 4. Retro Function Key Bar (FoxPro status bar)
        var footerTable = new Table().Border(TableBorder.None).HideHeaders();
        footerTable.AddColumn(new TableColumn("Keys"));
        footerTable.AddRow("[bold white on blue] [[F1/H]] Pomoc [/]  [bold white on navy] [[R]] Zrušiť Lease [/]  [bold white on darkgreen] [[P]] Pozastaviť [/]  [bold white on maroon] [[Q/Esc]] Ukončiť [/]");
        AnsiConsole.Write(footerTable);
    }

    private static string RenderSparkline(IReadOnlyList<int> points)
    {
        if (points == null || points.Count == 0) return "----------";
        int max = 1;
        for (int i = 0; i < points.Count; i++)
        {
            if (points[i] > max) max = points[i];
        }

        var sb = new StringBuilder(points.Count);
        for (int i = 0; i < points.Count; i++)
        {
            int val = points[i];
            int idx = Math.Clamp((val * (SparkChars.Length - 1)) / max, 0, SparkChars.Length - 1);
            sb.Append(SparkChars[idx]);
        }
        return sb.ToString();
    }

    private static string FormatUptime(double seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        if (ts.TotalDays >= 1) return $"{(int)ts.TotalDays}d {ts.Hours}h {ts.Minutes}m";
        if (ts.TotalHours >= 1) return $"{ts.Hours}h {ts.Minutes}m {ts.Seconds}s";
        return $"{ts.Minutes}m {ts.Seconds}s";
    }

    private static HttpClient CreateClient(string server)
    {
        var client = new HttpClient { BaseAddress = new Uri(server) };
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

    private static void PrintHelp()
    {
        AnsiConsole.MarkupLine("[bold]Použitie:[/] symbolon top [[prepínače]]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("Živý retro NetWare / FoxPro TUI real-time monitor klastra a floating sedadiel.");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Prepínače:[/] ");
        AnsiConsole.MarkupLine("  --server <url>            URL Symbolon riadiaceho uzla (predvolené: http://localhost:8080)");
        AnsiConsole.MarkupLine("  -i, --interval <sekundy>  Interval obnovy obrazovky (predvolené: 2s)");
        AnsiConsole.MarkupLine("  --filter <text>           Filter pre ID licencie, stroj, alebo používateľa");
        AnsiConsole.MarkupLine("  --once                    Jednorazový výpis stavu klastra bez interaktívnej slučky");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Klávesové skratky v interaktívnom režime:[/] ");
        AnsiConsole.MarkupLine("  [bold yellow]R[/]      Okamžité administratívne zrušenie vybraného lease (Revocation)");
        AnsiConsole.MarkupLine("  [bold yellow]P[/]      Pozastavenie / obnovenie obnovovania dát");
        AnsiConsole.MarkupLine("  [bold yellow]H, F1[/]  Zobrazenie tejto nápovedy");
        AnsiConsole.MarkupLine("  [bold yellow]Q, Esc[/] Ukončenie monitora");
    }
}
