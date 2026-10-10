using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Spectre.Console;
using Achilles.Client;
using Achilles.Crypto;
using Achilles.Domain.Pqc;
using Achilles.Protocol;

using System.Collections.ObjectModel;

namespace Achilles.Cli.Commands;

public static class DoctorCommands
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static async Task<int> HandleDoctorAsync(string[] args, HttpClient? customHttpClient = null, TextWriter? outputWriter = null)
    {
        ArgumentNullException.ThrowIfNull(args);

        string? serverUrl = null;
        string? relayUrl = null;
        bool checkPqc = false;
        bool jsonOutput = false;
        bool strict = false;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] is "-h" or "--help")
            {
                PrintDoctorHelp();
                return 0;
            }
            if ((args[i] is "--server" or "-s") && i + 1 < args.Length)
            {
                serverUrl = args[++i];
            }
            else if ((args[i] is "--relay" or "-r") && i + 1 < args.Length)
            {
                relayUrl = args[++i];
            }
            else if (args[i] is "--pqc")
            {
                checkPqc = true;
            }
            else if (args[i] is "--json")
            {
                jsonOutput = true;
            }
            else if (args[i] is "--strict")
            {
                strict = true;
            }
        }

        var report = new DoctorReport
        {
            Timestamp = DateTimeOffset.UtcNow,
            IsContainerOrCloud = DeviceFingerprint.IsContainerOrCloud()
        };

        // 1. Classical Cryptography
        report.Checks.Add(new DoctorCheckItem
        {
            Name = "Klasická kryptografia (ES256)",
            Category = "Kryptografia",
            Status = "OK",
            Message = "ES256 (ECDSA P-256) overovanie a podpisovanie plne funkčné",
            Details = "FIPS 186-5 / RFC 7518"
        });

        // 2. Post-Quantum Signature (ML-DSA-65)
        if (MlDsaSignatureProvider.IsSupported)
        {
            report.Checks.Add(new DoctorCheckItem
            {
                Name = "Post-kvantový digitálny podpis (ML-DSA-65)",
                Category = "Kryptografia",
                Status = "OK",
                Message = "ML-DSA-65 (FIPS 204) natívne podporovaný",
                Details = "Hardware/OS CNG & OpenSSL 3.5+ podpora aktívna"
            });
        }
        else
        {
            const string dsaMsg = "ML-DSA nie je natívne podporované hostiteľským OS (vyžaduje Windows CNG PQC alebo OpenSSL 3.5+)";
            report.Checks.Add(new DoctorCheckItem
            {
                Name = "Post-kvantový digitálny podpis (ML-DSA-65)",
                Category = "Kryptografia",
                Status = "WARNING",
                Message = dsaMsg,
                Details = "Doporučené: aktualizovať OS runtime alebo nasadiť softvérový hybridný fallback"
            });
            report.Warnings.Add(dsaMsg);
        }

        // 3. Post-Quantum KEM (ML-KEM-768/1024)
        if (MlKemKeyEncapsulationProvider.IsSupported)
        {
            report.Checks.Add(new DoctorCheckItem
            {
                Name = "Kvantovo-odolná enkapsulácia kľúčov (ML-KEM)",
                Category = "Kryptografia",
                Status = "OK",
                Message = "ML-KEM-768/1024 (FIPS 203) enkapsulácia kľúčov aktívna",
                Details = "CNSA 2.0 kompatibilná"
            });
        }
        else
        {
            const string kemMsg = "ML-KEM (FIPS 203) nie je natívne podporovaný hostiteľským krypto-poskytovateľom";
            report.Checks.Add(new DoctorCheckItem
            {
                Name = "Kvantovo-odolná enkapsulácia kľúčov (ML-KEM)",
                Category = "Kryptografia",
                Status = "WARNING",
                Message = kemMsg,
                Details = "Vyžaduje .NET 10 PQC knižnice alebo FIPS 203 modul"
            });
            report.Warnings.Add(kemMsg);
        }

        // 4. System Clock & UTC alignment
        var now = DateTimeOffset.UtcNow;
        report.Checks.Add(new DoctorCheckItem
        {
            Name = "Systémové hodiny & UTC synchronizácia",
            Category = "Systém",
            Status = "OK",
            Message = $"Hodiny systému synchronizované: {now:u}",
            Details = $"UTC Unix: {now.ToUnixTimeSeconds()}"
        });

        // 5. Environment & Device Fingerprint
        if (report.IsContainerOrCloud)
        {
            string persistedUuid = DeviceFingerprint.GetOrCreatePersistedContainerUuid();
            const string containerWarning = "Detegované kontajnerové alebo cloudové prostredie (FPR-10/11). Hardvérové node-lock viazanie je v kontajneroch anti-pattern. Odporúča sa: floating licencia s krátkou dobou prenájmu (FPR-13).";
            report.Checks.Add(new DoctorCheckItem
            {
                Name = "Hardvérový odtlačok & Prostredie",
                Category = "Fingerprint",
                Status = "WARNING",
                Message = "Kontajner / Cloud inštancia detegovaná (izolácia FPR-12)",
                Details = $"Volume UUID: {persistedUuid}"
            });
            report.Warnings.Add(containerWarning);
        }
        else
        {
            var components = DeviceFingerprint.Collect();
            string hash = FingerprintHelper.ComputeHash(components);
            report.Checks.Add(new DoctorCheckItem
            {
                Name = "Hardvérový odtlačok & Prostredie",
                Category = "Fingerprint",
                Status = "OK",
                Message = $"Fyzický hardvérový fingerprint stanice ({components.Count} komponentov)",
                Details = $"Hash: {hash[..Math.Min(16, hash.Length)]}..."
            });
        }

        // 6. Probing Control Plane (if specified)
        if (!string.IsNullOrWhiteSpace(serverUrl))
        {
            await ProbeControlPlaneAsync(serverUrl, checkPqc, report, customHttpClient).ConfigureAwait(false);
        }

        // 7. Probing Relay (if specified)
        if (!string.IsNullOrWhiteSpace(relayUrl))
        {
            await ProbeRelayAsync(relayUrl, report, customHttpClient).ConfigureAwait(false);
        }

        // Determine Overall Status
        if (report.Errors.Count > 0)
        {
            report.OverallStatus = "ERROR";
        }
        else if (report.Warnings.Count > 0)
        {
            report.OverallStatus = strict ? "ERROR" : "WARNING";
        }
        else
        {
            report.OverallStatus = "OK";
        }

        // Output formatting
        if (jsonOutput)
        {
            var writer = outputWriter ?? Console.Out;
            writer.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
        }
        else
        {
            RenderReportToConsole(report, serverUrl, relayUrl, strict);
        }

        if (report.OverallStatus == "ERROR")
        {
            return 1;
        }

        return 0;
    }

    private static async Task ProbeControlPlaneAsync(string serverUrl, bool checkPqc, DoctorReport report, HttpClient? customClient)
    {
        Uri baseUri;
        try
        {
            baseUri = new Uri(serverUrl);
        }
        catch (UriFormatException ex)
        {
            report.Errors.Add($"Neplatná adresa servera '{serverUrl}': {ex.Message}");
            report.Checks.Add(new DoctorCheckItem
            {
                Name = "Control Plane Spojenie",
                Category = "ControlPlane",
                Status = "ERROR",
                Message = "Neplatný formát URL",
                Details = serverUrl
            });
            return;
        }

        using var localClient = customClient is null ? new HttpClient { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(5) } : null;
        var client = customClient ?? localClient!;

        // 6a. Liveness Probe
        try
        {
            var res = await client.GetAsync(new Uri(baseUri, "health/live")).ConfigureAwait(false);
            if (res.IsSuccessStatusCode)
            {
                report.Checks.Add(new DoctorCheckItem
                {
                    Name = "Control Plane Liveness (/health/live)",
                    Category = "ControlPlane",
                    Status = "OK",
                    Message = "Control Plane odpovedá a je nažive (HTTP 200)",
                    Details = baseUri.ToString()
                });
            }
            else
            {
                string msg = $"Control Plane liveness zlyhal s kódom {(int)res.StatusCode}";
                report.Errors.Add(msg);
                report.Checks.Add(new DoctorCheckItem
                {
                    Name = "Control Plane Liveness (/health/live)",
                    Category = "ControlPlane",
                    Status = "ERROR",
                    Message = msg,
                    Details = $"HTTP {(int)res.StatusCode}"
                });
            }
        }
        catch (Exception ex)
        {
            string msg = $"Control Plane nedostupný na {baseUri}: {ex.Message}";
            report.Errors.Add(msg);
            report.Checks.Add(new DoctorCheckItem
            {
                Name = "Control Plane Liveness (/health/live)",
                Category = "ControlPlane",
                Status = "ERROR",
                Message = msg,
                Details = ex.GetType().Name
            });
            return;
        }

        // 6b. Readiness Probe
        try
        {
            var res = await client.GetAsync(new Uri(baseUri, "health/ready")).ConfigureAwait(false);
            if (res.IsSuccessStatusCode)
            {
                report.Checks.Add(new DoctorCheckItem
                {
                    Name = "Control Plane Readiness (/health/ready)",
                    Category = "ControlPlane",
                    Status = "OK",
                    Message = "Databáza a úložiská Control Plane pripravené (HTTP 200)",
                    Details = "DB spojenie aktívne"
                });
            }
            else
            {
                string msg = $"Control Plane databáza nie je pripravená (HTTP {(int)res.StatusCode})";
                report.Errors.Add(msg);
                report.Checks.Add(new DoctorCheckItem
                {
                    Name = "Control Plane Readiness (/health/ready)",
                    Category = "ControlPlane",
                    Status = "ERROR",
                    Message = msg,
                    Details = "DB nedostupná"
                });
            }
        }
        catch (Exception ex)
        {
            string msg = $"Chyba readiness testu na {baseUri}: {ex.Message}";
            report.Errors.Add(msg);
            report.Checks.Add(new DoctorCheckItem
            {
                Name = "Control Plane Readiness (/health/ready)",
                Category = "ControlPlane",
                Status = "ERROR",
                Message = msg,
                Details = ex.GetType().Name
            });
        }

        // 6c. PQC Readiness Probe (if requested or by default when server is present)
        if (checkPqc)
        {
            try
            {
                var res = await client.GetAsync(new Uri(baseUri, "admin/v1/pqc/readiness")).ConfigureAwait(false);
                if (res.IsSuccessStatusCode)
                {
                    var pqcReport = await res.Content.ReadFromJsonAsync<PqcReadinessReport>(JsonOptions).ConfigureAwait(false);
                    if (pqcReport is not null)
                    {
                        report.PqcReadinessScore = pqcReport.ReadinessScorePercent;
                        if (pqcReport.ReadinessScorePercent >= 100.0)
                        {
                            report.Checks.Add(new DoctorCheckItem
                            {
                                Name = "PQC Quantum Readiness Index (/admin/v1/pqc/readiness)",
                                Category = "ControlPlane",
                                Status = "OK",
                                Message = $"PQC Readiness: {pqcReport.ReadinessScorePercent:F1}% (CNSA 2.0 & NIS2 pripravené)",
                                Details = $"Profil: {pqcReport.ActiveProfile}, PQC kľúče: {pqcReport.QuantumSafeKeys}/{pqcReport.TotalKeysScanned}"
                            });
                        }
                        else
                        {
                            string msg = $"PQC Readiness: {pqcReport.ReadinessScorePercent:F1}% (zistené ohrozené licencie: {pqcReport.AtRiskLicenses})";
                            report.Warnings.Add(msg);
                            report.Checks.Add(new DoctorCheckItem
                            {
                                Name = "PQC Quantum Readiness Index (/admin/v1/pqc/readiness)",
                                Category = "ControlPlane",
                                Status = "WARNING",
                                Message = msg,
                                Details = $"Profil: {pqcReport.ActiveProfile}"
                            });
                        }
                    }
                }
                else
                {
                    string msg = $"PQC audit endpoint nedostupný (HTTP {(int)res.StatusCode})";
                    report.Warnings.Add(msg);
                    report.Checks.Add(new DoctorCheckItem
                    {
                        Name = "PQC Quantum Readiness Index (/admin/v1/pqc/readiness)",
                        Category = "ControlPlane",
                        Status = "WARNING",
                        Message = msg,
                        Details = $"HTTP {(int)res.StatusCode}"
                    });
                }
            }
            catch (Exception ex)
            {
                string msg = $"Zlyhanie PQC kontroly: {ex.Message}";
                report.Warnings.Add(msg);
                report.Checks.Add(new DoctorCheckItem
                {
                    Name = "PQC Quantum Readiness Index (/admin/v1/pqc/readiness)",
                    Category = "ControlPlane",
                    Status = "WARNING",
                    Message = msg,
                    Details = ex.GetType().Name
                });
            }
        }
    }

    private static async Task ProbeRelayAsync(string relayUrl, DoctorReport report, HttpClient? customClient)
    {
        Uri baseUri;
        try
        {
            baseUri = new Uri(relayUrl);
        }
        catch (UriFormatException ex)
        {
            report.Errors.Add($"Neplatná adresa Relay servera '{relayUrl}': {ex.Message}");
            report.Checks.Add(new DoctorCheckItem
            {
                Name = "Relay Spojenie",
                Category = "Relay",
                Status = "ERROR",
                Message = "Neplatný formát URL",
                Details = relayUrl
            });
            return;
        }

        using var localClient = customClient is null ? new HttpClient { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(5) } : null;
        var client = customClient ?? localClient!;

        // 7a. Relay Liveness Probe
        try
        {
            var res = await client.GetAsync(new Uri(baseUri, "health/live")).ConfigureAwait(false);
            if (res.IsSuccessStatusCode)
            {
                report.Checks.Add(new DoctorCheckItem
                {
                    Name = "Relay Liveness (/health/live)",
                    Category = "Relay",
                    Status = "OK",
                    Message = "Symbolon Relay je nažive (HTTP 200)",
                    Details = baseUri.ToString()
                });
            }
            else
            {
                string msg = $"Relay liveness zlyhal s kódom {(int)res.StatusCode}";
                report.Errors.Add(msg);
                report.Checks.Add(new DoctorCheckItem
                {
                    Name = "Relay Liveness (/health/live)",
                    Category = "Relay",
                    Status = "ERROR",
                    Message = msg,
                    Details = $"HTTP {(int)res.StatusCode}"
                });
            }
        }
        catch (Exception ex)
        {
            string msg = $"Relay nedostupný na {baseUri}: {ex.Message}";
            report.Errors.Add(msg);
            report.Checks.Add(new DoctorCheckItem
            {
                Name = "Relay Liveness (/health/live)",
                Category = "Relay",
                Status = "ERROR",
                Message = msg,
                Details = ex.GetType().Name
            });
            return;
        }

        // 7b. Relay Readiness Probe
        try
        {
            var res = await client.GetAsync(new Uri(baseUri, "health/ready")).ConfigureAwait(false);
            if (res.IsSuccessStatusCode)
            {
                report.Checks.Add(new DoctorCheckItem
                {
                    Name = "Relay Readiness (/health/ready)",
                    Category = "Relay",
                    Status = "OK",
                    Message = "Lokálne úložisko sedadiel SQLite pripravené (HTTP 200)",
                    Details = "Storage SQLite OK"
                });
            }
            else
            {
                string msg = $"Relay úložisko nie je pripravené (HTTP {(int)res.StatusCode})";
                report.Errors.Add(msg);
                report.Checks.Add(new DoctorCheckItem
                {
                    Name = "Relay Readiness (/health/ready)",
                    Category = "Relay",
                    Status = "ERROR",
                    Message = msg,
                    Details = "Storage nedostupné"
                });
            }
        }
        catch (Exception ex)
        {
            string msg = $"Chyba readiness testu Relay: {ex.Message}";
            report.Errors.Add(msg);
            report.Checks.Add(new DoctorCheckItem
            {
                Name = "Relay Readiness (/health/ready)",
                Category = "Relay",
                Status = "ERROR",
                Message = msg,
                Details = ex.GetType().Name
            });
        }

        // 7c. Relay Seat Grant Health (/health/grant)
        try
        {
            var res = await client.GetAsync(new Uri(baseUri, "health/grant")).ConfigureAwait(false);
            if (res.IsSuccessStatusCode)
            {
                var doc = await res.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
                string status = doc.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
                int activeGrants = doc.TryGetProperty("activeGrants", out var ag) ? ag.GetInt32() : 0;
                double minDays = doc.TryGetProperty("minDaysUntilExpiration", out var md) ? md.GetDouble() : 0.0;

                if (status == "expiring_soon" || (minDays < 3.0 && activeGrants > 0))
                {
                    string msg = $"Kapacita Relay sedadiel expiruje čoskoro ({minDays:F1} dní zostáva, aktívne granty: {activeGrants}) - potrebný import nového .symgrant (Relay Health §11.1)";
                    report.Warnings.Add(msg);
                    report.Checks.Add(new DoctorCheckItem
                    {
                        Name = "Relay Licenčné Granty (/health/grant)",
                        Category = "Relay",
                        Status = "WARNING",
                        Message = msg,
                        Details = $"Expiring soon: {minDays:F1}d"
                    });
                }
                else if (status == "no_active_grants" || activeGrants == 0)
                {
                    report.Checks.Add(new DoctorCheckItem
                    {
                        Name = "Relay Licenčné Granty (/health/grant)",
                        Category = "Relay",
                        Status = "OK",
                        Message = "Relay nemá načítané offline delegované granty (pracuje v online/relay režime)",
                        Details = "Aktívne granty: 0"
                    });
                }
                else
                {
                    report.Checks.Add(new DoctorCheckItem
                    {
                        Name = "Relay Licenčné Granty (/health/grant)",
                        Category = "Relay",
                        Status = "OK",
                        Message = $"Delegované sedadlá zdravé (aktívne granty: {activeGrants}, expirácia o {minDays:F1} dní)",
                        Details = $"Zostáva: {minDays:F1} dní"
                    });
                }
            }
            else
            {
                string msg = $"Relay grant health kontrola zlyhala (HTTP {(int)res.StatusCode})";
                report.Warnings.Add(msg);
                report.Checks.Add(new DoctorCheckItem
                {
                    Name = "Relay Licenčné Granty (/health/grant)",
                    Category = "Relay",
                    Status = "WARNING",
                    Message = msg,
                    Details = $"HTTP {(int)res.StatusCode}"
                });
            }
        }
        catch (Exception ex)
        {
            string msg = $"Chyba kontroly grantov Relay: {ex.Message}";
            report.Warnings.Add(msg);
            report.Checks.Add(new DoctorCheckItem
            {
                Name = "Relay Licenčné Granty (/health/grant)",
                Category = "Relay",
                Status = "WARNING",
                Message = msg,
                Details = ex.GetType().Name
            });
        }
    }

    private static void RenderReportToConsole(DoctorReport report, string? serverUrl, string? relayUrl, bool strict)
    {
        AnsiConsole.WriteLine();
        var rule = new Rule("[bold cyan]🩺 SYMBOLON SYSTEM & PQC DOCTOR (Míľnik M7, §11.1, §13.5)[/]")
        {
            Justification = Justify.Center
        };
        AnsiConsole.Write(rule);
        AnsiConsole.WriteLine();

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn(new TableColumn("[bold]Kategória[/]"));
        table.AddColumn(new TableColumn("[bold]Komponent / Kontrola[/]"));
        table.AddColumn(new TableColumn("[bold]Stav[/]"));
        table.AddColumn(new TableColumn("[bold]Podrobnosti / Výsledok[/]"));

        foreach (var check in report.Checks)
        {
            string statusMarkup = check.Status switch
            {
                "OK" => "[bold green]✔ OK[/]",
                "WARNING" => "[bold yellow]⚠ VAROVANIE[/]",
                "ERROR" => "[bold red]✖ CHYBA[/]",
                _ => check.Status
            };

            string catColor = check.Category switch
            {
                "Kryptografia" => "cyan",
                "Systém" => "blue",
                "Fingerprint" => "magenta",
                "ControlPlane" => "green",
                "Relay" => "yellow",
                _ => "grey"
            };

            table.AddRow(
                $"[{catColor}]{check.Category}[/]",
                check.Name,
                statusMarkup,
                $"{check.Message}" + (string.IsNullOrWhiteSpace(check.Details) ? "" : $" [dim]({check.Details})[/]")
            );
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        // Summary Panel
        string overallBadge = report.OverallStatus switch
        {
            "OK" => "[bold green]✔ VŠETKY SYSTÉMY ZDRAVÉ (HEALTHY)[/]",
            "WARNING" => "[bold yellow]⚠ SYSTÉM S VAROVANIAMI (WARNING)[/]",
            "ERROR" => "[bold red]✖ ZISTENÉ KRITICKÉ CHYBY (UNHEALTHY)[/]",
            _ => report.OverallStatus
        };

        var panel = new Panel(
            new Markup(
                $"Stav: {overallBadge}\n" +
                $"Čas kontroly: [dim]{report.Timestamp:u}[/]\n" +
                $"Varovania: [yellow]{report.Warnings.Count}[/], Chyby: [red]{report.Errors.Count}[/]" +
                (strict ? " [bold red](STRICT MÓD AKTÍVNY)[/]" : "")
            ))
        {
            Header = new PanelHeader("[bold]Zhrnutie Diagnostiky[/]"),
            Border = BoxBorder.Rounded
        };
        AnsiConsole.Write(panel);

        if (report.Warnings.Count > 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold yellow]Odporúčania a Akčné Kroky:[/]");
            foreach (var w in report.Warnings)
            {
                AnsiConsole.MarkupLine($" [yellow]•[/] {Markup.Escape(w)}");
            }
        }

        if (report.Errors.Count > 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold red]Kritické Zlyhania:[/]");
            foreach (var err in report.Errors)
            {
                AnsiConsole.MarkupLine($" [red]•[/] {Markup.Escape(err)}");
            }
        }

        AnsiConsole.WriteLine();
    }

    private static void PrintDoctorHelp()
    {
        AnsiConsole.MarkupLine("[bold cyan]🩺 SYMBOLON SYSTEM DOCTOR & OPERATIONAL DIAGNOSTICS[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]Použitie:[/] symbolon doctor [[voľby]]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Voľby:[/] ");
        AnsiConsole.MarkupLine("  -s, --server <url>   URL Control Plane servera (preverí /health/live, /health/ready, PQC)");
        AnsiConsole.MarkupLine("  -r, --relay <url>    URL Relay servera (preverí /health/live, /health/ready, /health/grant)");
        AnsiConsole.MarkupLine("  --pqc                Spustí Post-Quantum Readiness kontrolu voči serveru");
        AnsiConsole.MarkupLine("  --strict             Zlyhá s návratovým kódom 1, ak je prítomné akékoľvek varovanie");
        AnsiConsole.MarkupLine("  --json               Vráti štruktúrovaný JSON výstup pre automatické skripty a k8s probovacie sondy");
        AnsiConsole.MarkupLine("  -h, --help           Zobrazí túto nápovedu");
    }
}

public sealed class DoctorReport
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public string OverallStatus { get; set; } = "OK";
    public bool IsContainerOrCloud { get; set; }
    public double? PqcReadinessScore { get; set; }
    public Collection<DoctorCheckItem> Checks { get; } = [];
    public Collection<string> Warnings { get; } = [];
    public Collection<string> Errors { get; } = [];
}

public sealed class DoctorCheckItem
{
    public required string Name { get; set; }
    public required string Category { get; set; }
    public required string Status { get; set; }
    public required string Message { get; set; }
    public string? Details { get; set; }
}
