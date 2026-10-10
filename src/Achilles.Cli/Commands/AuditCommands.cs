using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Spectre.Console;
using Achilles.Domain.Transparency;
using Achilles.Protocol;
using Achilles.Protocol.Reporting;

namespace Achilles.Cli.Commands;

public static class AuditCommands
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task<int> HandleAuditAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintAuditHelp();
            return 0;
        }

        return args[0].ToUpperInvariant() switch
        {
            "VERIFY-CHAIN" => await HandleVerifyChainAsync(args[1..]).ConfigureAwait(false),
            "EXPORT-PROOF" or "PROOF" => await HandleExportProofAsync(args[1..]).ConfigureAwait(false),
            "EXPORT-COMPLIANCE-BUNDLE" or "EXPORT-BUNDLE" or "BUNDLE" => await HandleExportComplianceBundleAsync(args[1..]).ConfigureAwait(false),
            "LIST" => await HandleListAsync(args[1..]).ConfigureAwait(false),
            _ => UnknownSubcommand(args[0])
        };
    }

    private static async Task<int> HandleVerifyChainAsync(string[] args)
    {
        string server = GetArg(args, "--server") ?? "http://localhost:8080";
        string? apiKey = GetArg(args, "--api-key");
        bool jsonOutput = HasFlag(args, "--json");

        using var client = CreateClient(server, apiKey);
        try
        {
            var res = await client.GetAsync(new Uri("/admin/v1/audit/chain-status", UriKind.Relative)).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                // Fallback to legacy reports endpoint if needed
                res = await client.PostAsync(new Uri("/admin/v1/reports/audit/verify-integrity", UriKind.Relative), null).ConfigureAwait(false);
            }

            if (!res.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[bold red]Chyba servera:[/] HTTP {(int)res.StatusCode} {res.ReasonPhrase}");
                return 1;
            }

            var proof = await res.Content.ReadFromJsonAsync(AchillesProtocolJsonContext.Default.AuditVerificationProofDto).ConfigureAwait(false);
            if (proof is null)
            {
                AnsiConsole.MarkupLine("[red]Neplatná odpoveď servera.[/]");
                return 1;
            }

            if (jsonOutput)
            {
                Console.WriteLine(JsonSerializer.Serialize(proof, JsonOptions));
                return proof.IsChainIntact ? 0 : 2;
            }

            if (proof.IsChainIntact)
            {
                var panel = new Panel(new Markup(
                    $"[bold green]STATUS: REŤAZEC AUDITNÉHO DENNÍKA JE 100% KRYPTOGRAFICKY PLATNÝ[/]\n\n" +
                    $"Počet overených auditných udalostí: [bold white]{proof.TotalEventsVerified}[/]\n" +
                    $"Genesis udalosť (prvý záznam): [cyan]{proof.FirstEventId}[/] ([yellow]{proof.FirstEventTimestamp:O}[/])\n" +
                    $"Hlava reťazca (posledný záznam): [cyan]{proof.LastEventId}[/] ([yellow]{proof.LastEventTimestamp:O}[/])\n" +
                    $"Koreňový hash reťazca (Head Digest): [bold white]{proof.RootHashHex}[/]\n" +
                    $"Čas verifikácie: [grey]{proof.VerifiedAtUtc:O}[/]"
                ));
                panel.Border = BoxBorder.Double;
                panel.Header = new PanelHeader("[bold white on green] AUDIT HASH CHAIN INTACT [/]");
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

    private static async Task<int> HandleExportProofAsync(string[] args)
    {
        string? auditId = args.FirstOrDefault(a => !a.StartsWith('-'));
        if (string.IsNullOrWhiteSpace(auditId))
        {
            auditId = GetArg(args, "--id") ?? GetArg(args, "--audit-id");
        }

        if (string.IsNullOrWhiteSpace(auditId))
        {
            AnsiConsole.MarkupLine("[red]Chyba: Zadajte audit ID záznamu (symbolon audit export-proof <auditId>).[/]");
            return 1;
        }

        string server = GetArg(args, "--server") ?? "http://localhost:8080";
        string? apiKey = GetArg(args, "--api-key");
        string? outFile = GetArg(args, "--out");
        bool jsonOutput = HasFlag(args, "--json");

        using var client = CreateClient(server, apiKey);
        try
        {
            var res = await client.GetAsync(new Uri($"/admin/v1/audit/{Uri.EscapeDataString(auditId)}/proof", UriKind.Relative)).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                // Fallback to transparency inclusion
                res = await client.GetAsync(new Uri($"/v1/transparency/inclusion/{Uri.EscapeDataString(auditId)}", UriKind.Relative)).ConfigureAwait(false);
            }

            if (!res.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[bold red]Chyba servera:[/] HTTP {(int)res.StatusCode} {res.ReasonPhrase}");
                return 1;
            }

            var proof = await res.Content.ReadFromJsonAsync(AchillesProtocolJsonContext.Default.TransparencyInclusionResponseDto).ConfigureAwait(false);
            if (proof is null)
            {
                AnsiConsole.MarkupLine("[red]Neplatná odpoveď servera.[/]");
                return 1;
            }

            // Verify locally
            byte[] leafHash = Convert.FromHexString(proof.LeafHash);
            byte[] rootHash = Convert.FromHexString(proof.RootHash);
            var domainSteps = proof.Path.Select(p => new MerkleProofStep(p.Hash, p.Direction)).ToList();
            bool isValidLocally = MerkleTree.VerifyInclusion(leafHash, rootHash, domainSteps);

            if (jsonOutput || !string.IsNullOrWhiteSpace(outFile))
            {
                string json = JsonSerializer.Serialize(proof, JsonOptions);
                if (!string.IsNullOrWhiteSpace(outFile))
                {
                    await File.WriteAllTextAsync(outFile, json).ConfigureAwait(false);
                    AnsiConsole.MarkupLine($"[bold green]Kryptografický dôkaz bol uložený do:[/] [underline]{Markup.Escape(outFile)}[/]");
                }
                if (jsonOutput)
                {
                    Console.WriteLine(json);
                }
                return isValidLocally ? 0 : 2;
            }

            var panel = new Panel(new Markup(
                $"Udalosť ID: [cyan]{proof.AuditId}[/]\n" +
                $"Index v strome: [yellow]{proof.LeafIndex}[/] / [white]{proof.TreeSize}[/]\n" +
                $"Leaf Hash: [white]{proof.LeafHash}[/]\n" +
                $"Merkle Root: [bold green]{proof.RootHash}[/]\n" +
                $"Počet krokov v dôkaze: [bold white]{proof.Path.Count}[/]\n" +
                $"Lokálna verifikácia: {(isValidLocally ? "[bold green]ÚSPEŠNÁ (VALID)[/]" : "[bold red]ZLYHALA (INVALID)[/]")}"
            ));
            panel.Border = BoxBorder.Double;
            panel.Header = new PanelHeader($"[bold white on blue] MERKLE INCLUSION PROOF [/]");
            AnsiConsole.Write(panel);

            if (proof.Path.Count > 0)
            {
                var table = new Table();
                table.Border(TableBorder.Simple);
                table.AddColumn("Krok");
                table.AddColumn("Smer");
                table.AddColumn("Susedný Hash (Sibling)");

                for (int i = 0; i < proof.Path.Count; i++)
                {
                    var s = proof.Path[i];
                    table.AddRow(
                        (i + 1).ToString(CultureInfo.InvariantCulture),
                        s.Direction == "left" ? "[blue]LEFT[/]" : "[yellow]RIGHT[/]",
                        s.Hash
                    );
                }
                AnsiConsole.Write(table);
            }

            return isValidLocally ? 0 : 2;
        }
        catch (HttpRequestException ex)
        {
            AnsiConsole.MarkupLine($"[bold red]Chyba spojenia:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
    }

    private static async Task<int> HandleListAsync(string[] args)
    {
        string server = GetArg(args, "--server") ?? "http://localhost:8080";
        string? apiKey = GetArg(args, "--api-key");
        string limit = GetArg(args, "--limit") ?? "20";

        using var client = CreateClient(server, apiKey);
        try
        {
            var res = await client.GetAsync(new Uri($"/admin/v1/audit?limit={Uri.EscapeDataString(limit)}", UriKind.Relative)).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[bold red]Chyba servera:[/] HTTP {(int)res.StatusCode} {res.ReasonPhrase}");
                return 1;
            }

            var json = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                AnsiConsole.MarkupLine("[red]Prázdna alebo neplatná odpoveď od servera.[/]");
                return 1;
            }

            var table = new Table();
            table.Border(TableBorder.Double);
            table.AddColumn("Audit ID");
            table.AddColumn("Čas (UTC)");
            table.AddColumn("Typ");
            table.AddColumn("Licencia");
            table.AddColumn("Subjekt / Fingerprint");

            foreach (var el in doc.RootElement.EnumerateArray())
            {
                string id = el.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
                string tsStr = el.TryGetProperty("tsServer", out var tsProp) ? tsProp.GetString() ?? "" : "";
                string type = el.TryGetProperty("type", out var typeProp) ? typeProp.GetString() ?? "" : "";
                string lic = el.TryGetProperty("licenseId", out var licProp) && licProp.ValueKind != JsonValueKind.Null ? licProp.GetString() ?? "-" : "-";
                string subj = el.TryGetProperty("subject", out var subjProp) && subjProp.ValueKind != JsonValueKind.Null ? subjProp.GetString() ?? "-" : "-";

                table.AddRow(
                    $"[cyan]{id}[/]",
                    tsStr,
                    $"[yellow]{type}[/]",
                    lic,
                    subj
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

    private static async Task<int> HandleExportComplianceBundleAsync(string[] args)
    {
        string server = GetArg(args, "--server") ?? "http://localhost:8080";
        string? apiKey = GetArg(args, "--api-key");
        string outPath = GetArg(args, "--out") ?? GetArg(args, "-o") ?? $"symbolon-compliance-bundle-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.zip";

        AnsiConsole.MarkupLine("[bold blue]=== Generujem a exportujem Compliance & Audit Balíček (SOC 2, ISO 27001, NIS 2) ===[/]");

        using var client = CreateClient(server, apiKey);
        try
        {
            var res = await client.GetAsync(new Uri("/v1/compliance/bundle", UriKind.Relative)).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[bold red]Chyba servera pri exporte balíčka:[/] HTTP {(int)res.StatusCode} {res.ReasonPhrase}");
                return 1;
            }

            byte[] zipBytes = await res.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            await File.WriteAllBytesAsync(outPath, zipBytes).ConfigureAwait(false);

            AnsiConsole.MarkupLine($"[green]✓ Compliance balíček úspešne uložený:[/] [bold cyan]{Markup.Escape(outPath)}[/] ({zipBytes.Length:N0} bajtov)");

            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumn("[bold]Komponent v ZIP balíčku[/]");
            table.AddColumn("[bold]Popis / Štandard[/]");
            table.AddRow("manifest.json", "Metadáta balíčka, podpisová autorita, PQC úroveň 3");
            table.AddRow("audit_trail_merkle_verified.json", "Merkle STH log a kryptografický dôkaz nemennosti");
            table.AddRow("sbom_cyclonedx.json", "CycloneDX v1.6 Software Bill of Materials (SBOM)");
            table.AddRow("soc2_iso27001_nis2_mapping.json", "Matica bezpečnostných kontrol pre SOC 2, ISO 27001 a NIS 2");
            table.AddRow("pqc_readiness_assessment.json", "Post-Quantum Cryptography audit (ML-DSA-65 / ML-KEM)");
            table.AddRow("concurrency_trueup_report.json", "True-up report floating licencií a špičková súbežnosť");
            table.AddRow("access_and_scim_audit.json", "SCIM 2.0 provisioning a SSO identity audit logy");
            table.AddRow("checksums.sha256", "SHA-256 kontrolné súčty všetkých artefaktov");
            table.AddRow("signature.pqc.sig", "Kryptografický digitálny podpis balíčka");

            AnsiConsole.Write(table);
            return 0;
        }
        catch (HttpRequestException ex)
        {
            AnsiConsole.MarkupLine($"[bold red]Chyba spojenia:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
    }

    private static void PrintAuditHelp()
    {
        AnsiConsole.WriteLine("""
            Použitie: symbolon audit <subcommand> [options]

            Subcommands:
              verify-chain [--server <url>] [--api-key <key>] [--json]
                  Kryptografické overenie nemenného reťazca SHA-256 hashov auditného denníka.
              export-proof <auditId> [--server <url>] [--api-key <key>] [--out <file>] [--json]
                  Získanie a exportovanie Merkle Inclusion Proofu pre konkrétnu auditnú udalosť.
              export-compliance-bundle [--server <url>] [--api-key <key>] [--out <file>]
                  Automatizovaný export podpísaného compliance balíčka (SOC 2, ISO 27001, NIS 2, SBOM, PQC).
              list [--server <url>] [--api-key <key>] [--limit <n>]
                  Zobrazenie prehľadnej tabuľky posledných auditných udalostí.
            """);
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

    private static bool HasFlag(string[] args, string flag)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static int UnknownSubcommand(string sub)
    {
        AnsiConsole.MarkupLine($"[red]Neznámy príkaz pre audit:[/] {sub}");
        PrintAuditHelp();
        return 1;
    }
}
