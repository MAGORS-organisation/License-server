using System.Buffers.Text;
using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Spectre.Console;
using Symbolon.Crypto;
using Symbolon.Format;
using Symbolon.Protocol;

namespace Symbolon.Cli.Commands;

public static class GrantCommands
{
    public static async Task<int> HandleGrantAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintGrantHelp();
            return 0;
        }

        return args[0].ToUpperInvariant() switch
        {
            "REQUEST" or "REQ" => await HandleRequestAsync(args[1..]).ConfigureAwait(false),
            "ISSUE" => await HandleIssueAsync(args[1..]).ConfigureAwait(false),
            "INSPECT" or "SHOW" => HandleInspect(args[1..]),
            "IMPORT" => await HandleImportAsync(args[1..]).ConfigureAwait(false),
            _ => UnknownSubcommand(args[0])
        };
    }

    private static async Task<int> HandleRequestAsync(string[] args)
    {
        string? licenseKey = null;
        string? relayId = null;
        int seats = 1;
        long lastSeq = 0;
        string? usageDigest = null;
        string? outFile = null;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] is "--license" or "-l" && i + 1 < args.Length)
            {
                licenseKey = args[++i];
            }
            else if (args[i] is "--relay" or "-r" && i + 1 < args.Length)
            {
                relayId = args[++i];
            }
            else if (args[i] is "--seats" or "-s" && i + 1 < args.Length && int.TryParse(args[++i], CultureInfo.InvariantCulture, out int sVal))
            {
                seats = sVal;
            }
            else if (args[i] is "--last-seq" && i + 1 < args.Length && long.TryParse(args[++i], CultureInfo.InvariantCulture, out long seqVal))
            {
                lastSeq = seqVal;
            }
            else if (args[i] is "--usage-digest" or "-d" && i + 1 < args.Length)
            {
                usageDigest = args[++i];
            }
            else if (args[i] is "--out" or "-o" && i + 1 < args.Length)
            {
                outFile = args[++i];
            }
        }

        if (string.IsNullOrWhiteSpace(licenseKey) || string.IsNullOrWhiteSpace(relayId))
        {
            AnsiConsole.MarkupLine("[red]Error: --license and --relay are required.[/]");
            return 1;
        }

        usageDigest ??= $"sha256:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"audit-empty:{licenseKey}")))}";
        string nonce = $"nonce_{Guid.NewGuid():N}";
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        using var relayKey = Es256SignatureProvider.GenerateKey(relayId);

        var claims = new AirGapRequestClaims
        {
            Iss = relayId,
            Sub = licenseKey,
            Jti = $"req_{Guid.NewGuid():N}",
            Iat = now,
            Exp = now + 7200,
            Symreq = new AirGapRequestPayload
            {
                V = 1,
                RelayId = relayId,
                LicenseKey = licenseKey,
                RequestedSeats = seats,
                LastSeq = lastSeq,
                UsageDigest = usageDigest,
                Nonce = nonce
            }
        };

        var signer = new AirGapRequestSigner(relayKey);
        string pem = signer.Sign(claims);

        if (!string.IsNullOrWhiteSpace(outFile))
        {
            await File.WriteAllTextAsync(outFile, pem).ConfigureAwait(false);
            AnsiConsole.MarkupLine($"[green]✓ Signed grant request (.symreq) written to:[/] {outFile}");
        }
        else
        {
            AnsiConsole.WriteLine(pem);
        }

        return 0;
    }

    private static async Task<int> HandleIssueAsync(string[] args)
    {
        string? inFile = null;
        string? serverUrl = null;
        string? outFile = null;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] is "--in" or "-i" && i + 1 < args.Length)
            {
                inFile = args[++i];
            }
            else if (args[i] is "--server" or "-s" && i + 1 < args.Length)
            {
                serverUrl = args[++i];
            }
            else if (args[i] is "--out" or "-o" && i + 1 < args.Length)
            {
                outFile = args[++i];
            }
        }

        if (string.IsNullOrWhiteSpace(inFile) || !File.Exists(inFile))
        {
            AnsiConsole.MarkupLine("[red]Error: --in <file.symreq> is required and must exist.[/]");
            return 1;
        }

        string requestPem = await File.ReadAllTextAsync(inFile).ConfigureAwait(false);
        serverUrl ??= "http://localhost:5000";

        using var http = new HttpClient { BaseAddress = new Uri(serverUrl) };
        var postRes = await http.PostAsJsonAsync("/v1/offline/requests", new OfflineAirGapRequestDto
        {
            RequestPem = requestPem
        }).ConfigureAwait(false);

        if (!postRes.IsSuccessStatusCode)
        {
            string err = await postRes.Content.ReadAsStringAsync().ConfigureAwait(false);
            AnsiConsole.MarkupLine($"[red]Server rejected grant request ({postRes.StatusCode}):[/] {err}");
            return 1;
        }

        var resp = await postRes.Content.ReadFromJsonAsync<OfflineAirGapResponseDto>().ConfigureAwait(false);
        if (resp is null)
        {
            AnsiConsole.MarkupLine("[red]Failed to parse server response.[/]");
            return 1;
        }

        AnsiConsole.MarkupLine($"[green]✓ Seat Grant issued successfully![/]");
        AnsiConsole.MarkupLine($"[bold]Grant ID:[/] {resp.GrantId}");
        AnsiConsole.MarkupLine($"[bold]Relay:[/] {resp.RelayId}");
        AnsiConsole.MarkupLine($"[bold]Seats:[/] {resp.Seats} (Range: [[{resp.SeatFrom}..{resp.SeatTo}]])");
        AnsiConsole.MarkupLine($"[bold]Seq:[/] {resp.Seq} (Supersedes: {resp.Supersedes?.ToString(CultureInfo.InvariantCulture) ?? "none"})");
        AnsiConsole.MarkupLine($"[bold]Expires:[/] {resp.ExpiresAt:u}");

        if (!string.IsNullOrWhiteSpace(outFile))
        {
            await File.WriteAllTextAsync(outFile, resp.SymgrantPem).ConfigureAwait(false);
            AnsiConsole.MarkupLine($"[green]✓ .symgrant file saved to:[/] {outFile}");
        }

        return 0;
    }

    private static int HandleInspect(string[] args)
    {
        string? inFile = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] is "--in" or "-i" && i + 1 < args.Length)
            {
                inFile = args[++i];
            }
        }

        if (string.IsNullOrWhiteSpace(inFile) || !File.Exists(inFile))
        {
            AnsiConsole.MarkupLine("[red]Error: --in <file> is required and must exist.[/]");
            return 1;
        }

        string content = File.ReadAllText(inFile);

        if (content.Contains("SYMBOLON SEAT GRANT", StringComparison.Ordinal))
        {
            return InspectSeatGrant(content);
        }
        else if (content.Contains("SYMBOLON GRANT REQUEST", StringComparison.Ordinal))
        {
            return InspectGrantRequest(content);
        }
        else
        {
            AnsiConsole.MarkupLine("[red]Error: Unrecognized Symbolon artifact format in file.[/]");
            return 1;
        }
    }

    private static int InspectSeatGrant(string pem)
    {
        if (!PemArmor.TryUnwrap(pem, "SYMBOLON SEAT GRANT", out byte[]? docBytes) || docBytes is null)
        {
            AnsiConsole.MarkupLine("[red]Failed to unwrap PEM armor for SYMBOLON SEAT GRANT.[/]");
            return 1;
        }

        var doc = JsonSerializer.Deserialize<JwsGeneralJson>(docBytes);
        if (doc is null)
        {
            AnsiConsole.MarkupLine("[red]Failed to parse JWS General JSON.[/]");
            return 1;
        }

        byte[] payloadBytes = Base64Url.DecodeFromChars(doc.Payload);
        var claims = JsonSerializer.Deserialize<SeatGrantDocumentClaims>(payloadBytes);
        if (claims is null)
        {
            AnsiConsole.MarkupLine("[red]Failed to parse SeatGrantDocumentClaims.[/]");
            return 1;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("[bold]Property[/]");
        table.AddColumn("[bold]Value[/]");

        table.AddRow("Artifact Type", "[cyan].symgrant (JWS General JSON)[/]");
        table.AddRow("Issuer (iss)", claims.Iss);
        table.AddRow("License (sub)", claims.Sub);
        table.AddRow("Relay (aud)", claims.Aud);
        table.AddRow("Grant ID (jti)", claims.Jti);
        table.AddRow("Seats Delegated", $"[green]{claims.Symgrant.Seats}[/]");
        table.AddRow("Seat Range", $"[[{claims.Symgrant.SeatRange[0]}..{claims.Symgrant.SeatRange[1]}]]");

        int rangeCount = claims.Symgrant.SeatRange[1] - claims.Symgrant.SeatRange[0] + 1;
        bool rangeValid = rangeCount == claims.Symgrant.Seats;
        table.AddRow("Disjunction Check (GNT-3)", rangeValid ? "[green]PASS (Count == Seats)[/]" : "[red]FAIL (Mismatch!)[/]");

        table.AddRow("Sequence (seq)", claims.Symgrant.Seq.ToString(CultureInfo.InvariantCulture));
        table.AddRow("Supersedes", claims.Symgrant.Supersedes?.ToString(CultureInfo.InvariantCulture) ?? "[grey]none[/]");
        table.AddRow("Not Before", DateTimeOffset.FromUnixTimeSeconds(claims.Nbf).ToString("u", CultureInfo.InvariantCulture));
        table.AddRow("Expires At", DateTimeOffset.FromUnixTimeSeconds(claims.Exp).ToString("u", CultureInfo.InvariantCulture));
        table.AddRow("Signatures Count", doc.Signatures?.Count.ToString(CultureInfo.InvariantCulture) ?? "0");

        AnsiConsole.Write(new Panel(table).Header("[bold yellow]SYMBOLON SEAT GRANT INSPECTION[/]"));
        return 0;
    }

    private static int InspectGrantRequest(string pem)
    {
        if (!PemArmor.TryUnwrap(pem, "SYMBOLON GRANT REQUEST", out byte[]? docBytes) || docBytes is null)
        {
            AnsiConsole.MarkupLine("[red]Failed to unwrap PEM armor for SYMBOLON GRANT REQUEST.[/]");
            return 1;
        }

        var doc = JsonSerializer.Deserialize<JwsGeneralJson>(docBytes);
        if (doc is null)
        {
            AnsiConsole.MarkupLine("[red]Failed to parse JWS General JSON.[/]");
            return 1;
        }

        byte[] payloadBytes = Base64Url.DecodeFromChars(doc.Payload);
        var claims = JsonSerializer.Deserialize<AirGapRequestClaims>(payloadBytes);
        if (claims is null)
        {
            AnsiConsole.MarkupLine("[red]Failed to parse AirGapRequestClaims.[/]");
            return 1;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("[bold]Property[/]");
        table.AddColumn("[bold]Value[/]");

        table.AddRow("Artifact Type", "[cyan].symreq (JWS General JSON)[/]");
        table.AddRow("Relay (iss)", claims.Iss);
        table.AddRow("License Key (sub)", claims.Sub);
        table.AddRow("Request ID (jti)", claims.Jti);
        table.AddRow("Requested Seats", $"[green]{claims.Symreq.RequestedSeats}[/]");
        table.AddRow("Last Sequence", claims.Symreq.LastSeq.ToString(CultureInfo.InvariantCulture));
        table.AddRow("Usage Digest", claims.Symreq.UsageDigest);
        table.AddRow("Nonce", claims.Symreq.Nonce);
        table.AddRow("Expires At", DateTimeOffset.FromUnixTimeSeconds(claims.Exp).ToString("u", CultureInfo.InvariantCulture));

        AnsiConsole.Write(new Panel(table).Header("[bold yellow]SYMBOLON GRANT REQUEST INSPECTION[/]"));
        return 0;
    }

    private static async Task<int> HandleImportAsync(string[] args)
    {
        string? inFile = null;
        string? relayUrl = null;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] is "--in" or "-i" && i + 1 < args.Length)
            {
                inFile = args[++i];
            }
            else if (args[i] is "--relay" or "-r" && i + 1 < args.Length)
            {
                relayUrl = args[++i];
            }
        }

        if (string.IsNullOrWhiteSpace(inFile) || !File.Exists(inFile))
        {
            AnsiConsole.MarkupLine("[red]Error: --in <file.symgrant> is required and must exist.[/]");
            return 1;
        }

        string grantPem = await File.ReadAllTextAsync(inFile).ConfigureAwait(false);
        relayUrl ??= "http://localhost:5001";

        using var http = new HttpClient { BaseAddress = new Uri(relayUrl) };
        var postRes = await http.PostAsJsonAsync("/v1/offline/grants/import", new
        {
            grantPem
        }).ConfigureAwait(false);

        if (!postRes.IsSuccessStatusCode)
        {
            string err = await postRes.Content.ReadAsStringAsync().ConfigureAwait(false);
            AnsiConsole.MarkupLine($"[red]Relay rejected grant import ({postRes.StatusCode}):[/] {err}");
            return 1;
        }

        AnsiConsole.MarkupLine("[green]✓ Seat Grant successfully imported into Relay floating pool![/]");
        return 0;
    }

    private static void PrintGrantHelp()
    {
        AnsiConsole.MarkupLine(@"[bold]Symbolon Delegated Seat Grant & Air-Gap Tools[/] (GNT-1..10, FLT-32..35)

[bold]Použitie:[/]
  symbolon grant <príkaz> [[možnosti]]

[bold]Príkazy:[/]
  [cyan]request[/]  Vytvorí a podpíše požiadavku na sedadlá (.symreq)
  [cyan]issue[/]    Odošle .symreq na Control Plane a získa .symgrant
  [cyan]inspect[/]  Zobrazí podrobné informácie a invarianty .symgrant alebo .symreq súboru
  [cyan]import[/]   Importuje .symgrant do bežiaceho offline relayu

[bold]Príklady:[/]
  symbolon grant request --license SYM1-DEMO-KEY --relay rly_factory_01 --seats 5 --out req.symreq
  symbolon grant issue --in req.symreq --server http://cp.symbolon.internal --out grant.symgrant
  symbolon grant inspect --in grant.symgrant
  symbolon grant import --in grant.symgrant --relay http://localhost:5001
");
    }

    private static int UnknownSubcommand(string sub)
    {
        AnsiConsole.MarkupLine($"[red]Neznámy podpríkaz grantu: {sub}[/]");
        PrintGrantHelp();
        return 1;
    }
}
