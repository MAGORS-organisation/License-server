using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using Spectre.Console;
using Symbolon.Protocol;
using Symbolon.Protocol.Reporting;

namespace Symbolon.Cli.Commands;

public static class ReportCommands
{
    public static async Task<int> HandleReportsAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintReportsHelp();
            return 0;
        }

        return args[0].ToUpperInvariant() switch
        {
            "CONCURRENCY" => await HandleConcurrencyAsync(args[1..]).ConfigureAwait(false),
            "TRUE-UP" or "TRUEUP" => await HandleTrueUpAsync(args[1..]).ConfigureAwait(false),
            "DENIALS" => await HandleDenialsAsync(args[1..]).ConfigureAwait(false),
            "VERIFY-AUDIT" or "VERIFY" => await HandleVerifyAuditAsync(args[1..]).ConfigureAwait(false),
            _ => UnknownSubcommand(args[0])
        };
    }

    private static async Task<int> HandleConcurrencyAsync(string[] args)
    {
        string server = GetArg(args, "--server") ?? "http://localhost:8080";
        string bucket = GetArg(args, "--bucket") ?? "hour";
        string? licenseId = GetArg(args, "--license");
        string? fromStr = GetArg(args, "--from");
        string? toStr = GetArg(args, "--to");

        AnsiConsole.MarkupLine("[bold cyan]=== Symbolon Analytika Súbežnosti (FLT-38 Peak Concurrency) ===[/]");

        using var client = CreateClient(server);
        var urlBuilder = new StringBuilder();
        urlBuilder.Append(CultureInfo.InvariantCulture, $"/admin/v1/reports/concurrency/timeline?bucket={Uri.EscapeDataString(bucket)}");
        if (!string.IsNullOrWhiteSpace(licenseId))
        {
            urlBuilder.Append(CultureInfo.InvariantCulture, $"&licenseId={Uri.EscapeDataString(licenseId)}");
        }
        if (!string.IsNullOrWhiteSpace(fromStr))
        {
            urlBuilder.Append(CultureInfo.InvariantCulture, $"&from={Uri.EscapeDataString(fromStr)}");
        }
        if (!string.IsNullOrWhiteSpace(toStr))
        {
            urlBuilder.Append(CultureInfo.InvariantCulture, $"&to={Uri.EscapeDataString(toStr)}");
        }

        try
        {
            var res = await client.GetAsync(new Uri(urlBuilder.ToString(), UriKind.Relative)).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[bold red]Chyba servera:[/] HTTP {(int)res.StatusCode} {res.ReasonPhrase}");
                return 1;
            }

            var report = await res.Content.ReadFromJsonAsync<ConcurrencyAnalyticsResponseDto>(SymbolonProtocolJsonContext.Default.ConcurrencyAnalyticsResponseDto).ConfigureAwait(false);
            if (report is null)
            {
                AnsiConsole.MarkupLine("[red]Neplatná odpoveď servera.[/]");
                return 1;
            }

            AnsiConsole.MarkupLine($"Rozsah: [yellow]{report.RangeStart:g}[/] až [yellow]{report.RangeEnd:g}[/]");
            AnsiConsole.MarkupLine($"Kapacita licencie: [bold green]{report.LicensedCapacity}[/] sedadiel | Celkový Peak: [bold red]{report.OverallPeak}[/] ({report.OverallPeakUtilizationPercent}% vyťaženie)");
            AnsiConsole.MarkupLine($"Celkový počet checkoutov: [cyan]{report.TotalCheckouts}[/] | Zamietnutia (FLT-39): [yellow]{report.TotalDenials}[/]");
            AnsiConsole.WriteLine();

            var table = new Table();
            table.Border(TableBorder.Rounded);
            table.AddColumn(new TableColumn("[bold]Časové Okno[/]").LeftAligned());
            table.AddColumn(new TableColumn("[bold]Peak Sedadiel[/]").Centered());
            table.AddColumn(new TableColumn("[bold]Priem. Súbeh[/]").Centered());
            table.AddColumn(new TableColumn("[bold]Checkouty[/]").Centered());
            table.AddColumn(new TableColumn("[bold]Zamietnuté[/]").Centered());
            table.AddColumn(new TableColumn("[bold]Vyťaženie[/]").RightAligned());

            foreach (var b in report.Buckets)
            {
                string peakColor = b.PeakConcurrency > b.CapacityLimit ? "red bold" : (b.PeakConcurrency >= b.CapacityLimit * 0.9 ? "yellow" : "green");
                string utilBar = RenderMiniBar(b.PeakUtilizationPercent);

                table.AddRow(
                    b.Timestamp.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                    $"[{peakColor}]{b.PeakConcurrency}[/]",
                    b.AverageConcurrency.ToString("F1", CultureInfo.InvariantCulture),
                    b.TotalCheckouts.ToString(CultureInfo.InvariantCulture),
                    b.TotalDenials > 0 ? $"[red]{b.TotalDenials}[/]" : "0",
                    $"{b.PeakUtilizationPercent,5:F1}% {utilBar}"
                );
            }

            AnsiConsole.Write(table);
            return 0;
        }
        catch (HttpRequestException ex)
        {
            AnsiConsole.MarkupLine($"[bold red]Chyba spojenia:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
    }

    private static async Task<int> HandleTrueUpAsync(string[] args)
    {
        string server = GetArg(args, "--server") ?? "http://localhost:8080";
        string? licenseId = GetArg(args, "--license");
        string? fromStr = GetArg(args, "--from");
        string? toStr = GetArg(args, "--to");
        string? exportFile = GetArg(args, "--export");

        AnsiConsole.MarkupLine("[bold cyan]=== Symbolon Enterprise True-Up & Compliance Report ===[/]");

        using var client = CreateClient(server);
        var urlBuilder = new StringBuilder("/admin/v1/reports/true-up");
        bool hasQuery = false;
        if (!string.IsNullOrWhiteSpace(licenseId))
        {
            urlBuilder.Append(CultureInfo.InvariantCulture, $"?licenseId={Uri.EscapeDataString(licenseId)}");
            hasQuery = true;
        }
        if (!string.IsNullOrWhiteSpace(fromStr))
        {
            urlBuilder.Append(CultureInfo.InvariantCulture, $"{(hasQuery ? "&" : "?")}from={Uri.EscapeDataString(fromStr)}");
            hasQuery = true;
        }
        if (!string.IsNullOrWhiteSpace(toStr))
        {
            urlBuilder.Append(CultureInfo.InvariantCulture, $"{(hasQuery ? "&" : "?")}to={Uri.EscapeDataString(toStr)}");
        }

        try
        {
            var res = await client.GetAsync(new Uri(urlBuilder.ToString(), UriKind.Relative)).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[bold red]Chyba servera:[/] HTTP {(int)res.StatusCode} {res.ReasonPhrase}");
                return 1;
            }

            var report = await res.Content.ReadFromJsonAsync<TrueUpReportDto>(SymbolonProtocolJsonContext.Default.TrueUpReportDto).ConfigureAwait(false);
            if (report is null)
            {
                AnsiConsole.MarkupLine("[red]Neplatná odpoveď servera.[/]");
                return 1;
            }

            string complianceBadge = report.ContractCompliance.ToUpperInvariant() switch
            {
                "COMPLIANT" => "[bold green]100% COMPLIANT[/]",
                "OVERAGEWARNING" => "[bold yellow]OVERAGE WARNING[/] (v zmluvnom tolerančnom pásme)",
                _ => "[bold red]NON-COMPLIANT (PREČERPANIE KAPACITY)[/]"
            };

            var panel = new Panel(new Markup(
                $"Tenant: [bold]{Markup.Escape(report.TenantId)}[/]\n" +
                $"Licencia: [bold]{Markup.Escape(report.LicenseId ?? "Všetky")}[/] ([cyan]{Markup.Escape(report.ProductName)}[/])\n" +
                $"Sledované obdobie: [yellow]{report.PeriodStart:d}[/] až [yellow]{report.PeriodEnd:d}[/]\n" +
                $"Zmluvný status: {complianceBadge}\n\n" +
                $"Zakúpené sedadlá (Baseline): [bold]{report.LicensedSeats}[/]\n" +
                $"Reálna špičková súbežnosť (Peak): [bold]{report.PeakConcurrentSeats}[/]\n" +
                $"Zmluvný buffer (Overage Buffer): [bold]{report.OverageBufferSeats}[/]\n" +
                $"Prečerpané sedadlá (Overage Used): [bold]{report.OverageSeatsUsed}[/]\n" +
                $"Zúčtovateľné sedadlohodiíny: [bold]{report.BillableOverageSeatSeconds / 3600.0:F1} h[/]\n" +
                $"Celkový počet checkoutov: [bold]{report.TotalCheckouts}[/] | Zamietnuté: [bold]{report.TotalDenials}[/]"
            ));
            panel.Header = new PanelHeader("[bold white on blue] True-Up Výkaz Vyťaženia [/]");
            panel.Border = BoxBorder.Rounded;
            AnsiConsole.Write(panel);

            if (!string.IsNullOrWhiteSpace(exportFile))
            {
                string exportUrl = urlBuilder.ToString().Replace("/admin/v1/reports/true-up", "/admin/v1/reports/true-up/export", StringComparison.Ordinal);
                bool isCsv = exportFile.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);
                exportUrl += (exportUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?") + (isCsv ? "format=csv" : "format=json");

                var exportRes = await client.GetAsync(new Uri(exportUrl, UriKind.Relative)).ConfigureAwait(false);
                if (exportRes.IsSuccessStatusCode)
                {
                    string data = await exportRes.Content.ReadAsStringAsync().ConfigureAwait(false);
                    await File.WriteAllTextAsync(exportFile, data).ConfigureAwait(false);
                    AnsiConsole.MarkupLine($"[bold green]Report bol úspešne exportovaný do:[/] [underline]{Markup.Escape(exportFile)}[/]");
                }
            }

            return 0;
        }
        catch (HttpRequestException ex)
        {
            AnsiConsole.MarkupLine($"[bold red]Chyba spojenia:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
    }

    private static async Task<int> HandleDenialsAsync(string[] args)
    {
        string server = GetArg(args, "--server") ?? "http://localhost:8080";
        string? licenseId = GetArg(args, "--license");
        string limitStr = GetArg(args, "--limit") ?? "25";

        AnsiConsole.MarkupLine("[bold cyan]=== Symbolon Analytika Zamietnutí (FLT-39 Denials) ===[/]");

        using var client = CreateClient(server);
        var urlBuilder = new StringBuilder();
        urlBuilder.Append(CultureInfo.InvariantCulture, $"/admin/v1/reports/denials?limit={Uri.EscapeDataString(limitStr)}");
        if (!string.IsNullOrWhiteSpace(licenseId))
        {
            urlBuilder.Append(CultureInfo.InvariantCulture, $"&licenseId={Uri.EscapeDataString(licenseId)}");
        }

        try
        {
            var res = await client.GetAsync(new Uri(urlBuilder.ToString(), UriKind.Relative)).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[bold red]Chyba servera:[/] HTTP {(int)res.StatusCode} {res.ReasonPhrase}");
                return 1;
            }

            var report = await res.Content.ReadFromJsonAsync<DenialsAnalyticsResponseDto>(SymbolonProtocolJsonContext.Default.DenialsAnalyticsResponseDto).ConfigureAwait(false);
            if (report is null)
            {
                AnsiConsole.MarkupLine("[red]Neplatná odpoveď servera.[/]");
                return 1;
            }

            AnsiConsole.MarkupLine($"Celkový počet zamietnutí: [bold red]{report.TotalDenials}[/] | Postihnuté subjekty/stroje: [bold yellow]{report.UniqueSubjectsAffected}[/]");
            AnsiConsole.WriteLine();

            if (report.DenialsByReason.Count > 0)
            {
                var reasonTable = new Table();
                reasonTable.Border(TableBorder.Simple);
                reasonTable.AddColumn("Dôvod Zamietnutia");
                reasonTable.AddColumn(new TableColumn("Počet").RightAligned());

                foreach (var (r, count) in report.DenialsByReason)
                {
                    reasonTable.AddRow(Markup.Escape(r), $"[bold red]{count}[/]");
                }
                AnsiConsole.Write(reasonTable);
                AnsiConsole.WriteLine();
            }

            if (report.RecentDenials.Count > 0)
            {
                var table = new Table();
                table.Border(TableBorder.Rounded);
                table.AddColumn("Čas");
                table.AddColumn("Licencia");
                table.AddColumn("Subjekt / Fingerprint");
                table.AddColumn("Dôvod");

                foreach (var d in report.RecentDenials)
                {
                    table.AddRow(
                        d.Timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                        Markup.Escape(d.LicenseId),
                        Markup.Escape(d.Subject ?? "-"),
                        $"[red]{Markup.Escape(d.Reason)}[/]"
                    );
                }
                AnsiConsole.Write(table);
            }
            else
            {
                AnsiConsole.MarkupLine("[green]V zadanom období neboli zaznamenané žiadne zamietnuté licenčné požiadavky.[/]");
            }

            return 0;
        }
        catch (HttpRequestException ex)
        {
            AnsiConsole.MarkupLine($"[bold red]Chyba spojenia:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
    }

    private static async Task<int> HandleVerifyAuditAsync(string[] args)
    {
        string server = GetArg(args, "--server") ?? "http://localhost:8080";

        AnsiConsole.MarkupLine("[bold cyan]=== Symbolon Kryptografická Verifikácia Integrity Auditu (FLT-37) ===[/]");

        using var client = CreateClient(server);
        try
        {
            var res = await client.PostAsync(new Uri("/admin/v1/reports/audit/verify-integrity", UriKind.Relative), null).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[bold red]Chyba servera:[/] HTTP {(int)res.StatusCode} {res.ReasonPhrase}");
                return 1;
            }

            var proof = await res.Content.ReadFromJsonAsync<AuditVerificationProofDto>(SymbolonProtocolJsonContext.Default.AuditVerificationProofDto).ConfigureAwait(false);
            if (proof is null)
            {
                AnsiConsole.MarkupLine("[red]Neplatná odpoveď servera.[/]");
                return 1;
            }

            if (proof.IsChainIntact)
            {
                var panel = new Panel(new Markup(
                    $"[bold green]STATUS: INTEGRITA AUDITNÉHO REŤAZCA JE 100% NEPORUŠENÁ[/]\n\n" +
                    $"Počet overených auditných udalostí: [bold white]{proof.TotalEventsVerified}[/]\n" +
                    $"Prvý záznam: [cyan]{proof.FirstEventId}[/] ([yellow]{proof.FirstEventTimestamp:O}[/])\n" +
                    $"Posledný záznam: [cyan]{proof.LastEventId}[/] ([yellow]{proof.LastEventTimestamp:O}[/])\n" +
                    $"Koreňový kryptografický hash (Root Hash): [bold white]{proof.RootHashHex}[/]\n" +
                    $"Čas verifikácie: [grey]{proof.VerifiedAtUtc:O}[/]"
                ));
                panel.Border = BoxBorder.Double;
                panel.Header = new PanelHeader("[bold white on green] AUDIT VERIFIED OK [/]");
                AnsiConsole.Write(panel);
                return 0;
            }
            else
            {
                var panel = new Panel(new Markup(
                    $"[bold red]KRITICKÉ VAROVANIE: INTEGRITA AUDITNÉHO DENNÍKA BOLA PORUŠENÁ![/]\n\n" +
                    $"Poškodená udalosť: [bold red]{proof.TamperedEventId}[/]\n" +
                    $"Dôvod zlyhania: [bold yellow]{Markup.Escape(proof.TamperReason ?? "Neznáme zlyhanie")}[/]\n" +
                    $"Počet predchádzajúcich overených udalostí: [bold]{proof.TotalEventsVerified}[/]\n" +
                    $"Posledný známy platný hash: [white]{proof.RootHashHex}[/]"
                ));
                panel.Border = BoxBorder.Heavy;
                panel.Header = new PanelHeader("[bold white on red] TAMPERING DETECTED [/]");
                AnsiConsole.Write(panel);
                return 2;
            }
        }
        catch (HttpRequestException ex)
        {
            AnsiConsole.MarkupLine($"[bold red]Chyba spojenia:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
    }

    private static string RenderMiniBar(double percent)
    {
        int totalBlocks = 10;
        int filled = (int)Math.Clamp(Math.Round(percent / 10.0), 0, totalBlocks);
        string color = percent > 100 ? "red" : (percent >= 90 ? "yellow" : "green");
        return $"[{color}]{new string('■', filled)}[/][grey]{new string('□', totalBlocks - filled)}[/]";
    }

    private static HttpClient CreateClient(string serverEndpoint, string? apiKey = null)
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri(serverEndpoint.EndsWith('/') ? serverEndpoint : serverEndpoint + "/")
        };
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        }
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

    private static int UnknownSubcommand(string sub)
    {
        AnsiConsole.MarkupLine($"[bold red]Neznámy príkaz:[/] {Markup.Escape(sub)}");
        PrintReportsHelp();
        return 1;
    }

    private static void PrintReportsHelp()
    {
        AnsiConsole.MarkupLine("[bold]Použitie:[/] symbolon reports <príkaz> [[prepínače]]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Dostupné príkazy:[/] (FLT-37, FLT-38, FLT-39 Compliance)");
        AnsiConsole.MarkupLine("  [cyan]concurrency[/]     Analýza špičkovej súbežnosti vypočítaná priamo z auditných udalostí");
        AnsiConsole.MarkupLine("  [cyan]true-up[/]         Enterprise True-Up compliance výkaz s vyhodnotením zmluvného stavu");
        AnsiConsole.MarkupLine("  [cyan]denials[/]         Analytika zamietnutí s kategorizáciou dôvodov a postihnutých strojov");
        AnsiConsole.MarkupLine("  [cyan]verify-audit[/]    Kryptografická verifikácia append-only reťazca SHA-256 hashov");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Prepínače:[/] --server <url>, --license <id>, --from <iso>, --to <iso>, --bucket <hour|day|week>, --export <path>");
    }
}
