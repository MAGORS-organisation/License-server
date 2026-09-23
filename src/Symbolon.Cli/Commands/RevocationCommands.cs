using System.Buffers.Text;
using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Spectre.Console;
using Symbolon.Crypto;
using Symbolon.Format;

namespace Symbolon.Cli.Commands;

public static class RevocationCommands
{
    public static async Task<int> HandleRevocationsAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintRevocationsHelp();
            return 0;
        }

        return args[0].ToUpperInvariant() switch
        {
            "FETCH" => await HandleFetchAsync(args[1..]).ConfigureAwait(false),
            "INSPECT" => HandleInspect(args[1..]),
            "REVOKE" => await HandleRevokeAsync(args[1..]).ConfigureAwait(false),
            "LIST" => await HandleListAsync(args[1..]).ConfigureAwait(false),
            _ => UnknownSubcommand(args[0])
        };
    }

    private static async Task<int> HandleFetchAsync(string[] args)
    {
        string serverEndpoint = GetArg(args, "--server") ?? "http://localhost:8080";
        string? outFile = GetArg(args, "--out");
        string? sinceStr = GetArg(args, "--since");

        AnsiConsole.MarkupLine("[bold cyan]=== Symbolon Revocation List (.symrl) Synchronizácia ===[/]");

        using var client = CreateClient(serverEndpoint);
        string relativeUrl = !string.IsNullOrWhiteSpace(sinceStr) && long.TryParse(sinceStr, CultureInfo.InvariantCulture, out long since)
            ? $"/v1/revocations/latest?since={since}"
            : "/v1/revocations/latest.symrl";

        try
        {
            var response = await client.GetAsync(new Uri(relativeUrl, UriKind.Relative)).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]Server vrátil chybu: {response.StatusCode}[/]");
                return 1;
            }

            string pemContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            // Parse and display summary
            if (PemArmor.TryUnwrap(pemContent, "SYMBOLON REVOCATION LIST", out byte[]? unwrapped))
            {
                var doc = JsonSerializer.Deserialize(unwrapped, SymbolonJsonContext.Default.JwsGeneralJson);
                if (doc is not null)
                {
                    byte[] payloadBytes = Base64Url.DecodeFromChars(doc.Payload);
                    var claims = JsonSerializer.Deserialize(payloadBytes, SymbolonJsonContext.Default.RevocationListClaims);

                    if (claims is not null)
                    {
                        var infoTable = new Table().Border(TableBorder.Rounded);
                        infoTable.AddColumn("[grey]Vlastnosť[/]");
                        infoTable.AddColumn("[white]Hodnota[/]");

                        infoTable.AddRow("Vydavateľ (iss)", claims.Iss);
                        infoTable.AddRow("Sekvencia (seq)", $"[bold green]#{claims.Symrl.Seq}[/]");
                        infoTable.AddRow("Typ zoznamu", claims.Symrl.Full ? "[yellow]FULL CRL[/]" : $"[cyan]DELTA (od #{claims.Symrl.Since})[/]");
                        infoTable.AddRow("Vydané (iat)", DateTimeOffset.FromUnixTimeSeconds(claims.Iat).ToString("u", CultureInfo.InvariantCulture));
                        infoTable.AddRow("Platnosť do (exp)", DateTimeOffset.FromUnixTimeSeconds(claims.Exp).ToString("u", CultureInfo.InvariantCulture));
                        infoTable.AddRow("Počet revokovaných položiek", claims.Symrl.Revoked.Count.ToString(CultureInfo.InvariantCulture));
                        infoTable.AddRow("Podpisy", string.Join(", ", doc.Signatures.Select(s => s.Protected.Length > 0 ? "JWS sig" : "")));

                        AnsiConsole.Write(infoTable);
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(outFile))
            {
                await File.WriteAllTextAsync(outFile, pemContent, Encoding.UTF8).ConfigureAwait(false);
                AnsiConsole.MarkupLine($"[green]✓ Revokačný zoznam bol úspešne uložený do:[/] [bold white]{outFile}[/]");
            }
            else
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[grey]Tip: Pre uloženie do súboru použite parameter --out <cesta.symrl>[/]");
            }

            return 0;
        }
        catch (HttpRequestException ex)
        {
            AnsiConsole.MarkupLine($"[red]Chyba sieťového spojenia:[/] {ex.Message}");
            return 1;
        }
    }

    private static int HandleInspect(string[] args)
    {
        if (args.Length == 0 || args[0].StartsWith('-'))
        {
            AnsiConsole.MarkupLine("[red]Chyba: Zadajte cestu k súboru .symrl.[/]");
            return 1;
        }

        string filePath = args[0];
        if (!File.Exists(filePath))
        {
            AnsiConsole.MarkupLine($"[red]Súbor neexistuje:[/] {filePath}");
            return 1;
        }

        string pemContent = File.ReadAllText(filePath, Encoding.UTF8);
        if (!PemArmor.TryUnwrap(pemContent, "SYMBOLON REVOCATION LIST", out byte[]? unwrapped))
        {
            AnsiConsole.MarkupLine("[red]Súbor neobsahuje platný PEM pancier SYMBOLON REVOCATION LIST.[/]");
            return 1;
        }

        var doc = JsonSerializer.Deserialize(unwrapped, SymbolonJsonContext.Default.JwsGeneralJson);
        if (doc is null)
        {
            AnsiConsole.MarkupLine("[red]Neplatný JWS General JSON dokument.[/]");
            return 1;
        }

        byte[] payloadBytes;
        try
        {
            payloadBytes = Base64Url.DecodeFromChars(doc.Payload);
        }
        catch (FormatException)
        {
            AnsiConsole.MarkupLine("[red]Neplatný Base64URL payload.[/]");
            return 1;
        }

        var claims = JsonSerializer.Deserialize(payloadBytes, SymbolonJsonContext.Default.RevocationListClaims);
        if (claims is null)
        {
            AnsiConsole.MarkupLine("[red]Nepodarilo sa rozparsovať claims revokačného zoznamu.[/]");
            return 1;
        }

        AnsiConsole.MarkupLine($"[bold blue]=== Inšpekcia Revokačného Zoznamu:[/] [white]{Path.GetFileName(filePath)}[/] [bold blue]===[/]");

        var metaTable = new Table().Border(TableBorder.Rounded);
        metaTable.AddColumn("[grey]Pole[/]");
        metaTable.AddColumn("[white]Hodnota[/]");

        metaTable.AddRow("Vydavateľ (iss)", claims.Iss);
        metaTable.AddRow("Sekvencia (seq)", $"[bold green]#{claims.Symrl.Seq}[/]");
        metaTable.AddRow("Formát", claims.Symrl.Full ? "[yellow]FULL CRL[/]" : $"[cyan]DELTA (since: #{claims.Symrl.Since})[/]");
        metaTable.AddRow("Verzia schémy", claims.Symrl.V.ToString(CultureInfo.InvariantCulture));
        metaTable.AddRow("Vydané (iat)", DateTimeOffset.FromUnixTimeSeconds(claims.Iat).ToString("u", CultureInfo.InvariantCulture));
        metaTable.AddRow("Expirácia (exp)", DateTimeOffset.FromUnixTimeSeconds(claims.Exp).ToString("u", CultureInfo.InvariantCulture));
        metaTable.AddRow("Počet podpisov", doc.Signatures.Count.ToString(CultureInfo.InvariantCulture));

        for (int i = 0; i < doc.Signatures.Count; i++)
        {
            var sig = doc.Signatures[i];
            try
            {
                byte[] hdrBytes = Base64Url.DecodeFromChars(sig.Protected);
                var hdr = JsonSerializer.Deserialize(hdrBytes, SymbolonJsonContext.Default.JwsProtectedHeader);
                if (hdr is not null)
                {
                    metaTable.AddRow($"Podpis #{i + 1}", $"[cyan]{hdr.Alg}[/] | kid: [white]{hdr.Kid}[/] | typ: {hdr.Typ}");
                }
            }
            catch
            {
                metaTable.AddRow($"Podpis #{i + 1}", "[red]Neplatná hlavička[/]");
            }
        }

        AnsiConsole.Write(metaTable);
        AnsiConsole.WriteLine();

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("[cyan]Typ (t)[/]");
        table.AddColumn("[white]Identifikátor (id)[/]");
        table.AddColumn("[yellow]Dôvod (reason)[/]");
        table.AddColumn("[grey]Čas revokácie (at)[/]");

        foreach (var item in claims.Symrl.Revoked)
        {
            string typeColor = item.T switch
            {
                "license" => "[red]license[/]",
                "machine" => "[magenta]machine[/]",
                "kid" => "[yellow]kid (kľúč)[/]",
                "relay" => "[blue]relay[/]",
                "lease" => "[orange1]lease[/]",
                _ => $"[grey]{item.T}[/]"
            };

            string dateStr = DateTimeOffset.FromUnixTimeSeconds(item.At).ToString("u", CultureInfo.InvariantCulture);
            table.AddRow(typeColor, item.Id, item.Reason ?? "[grey]neuvedený[/]", dateStr);
        }

        AnsiConsole.Write(table);
        return 0;
    }

    private static async Task<int> HandleRevokeAsync(string[] args)
    {
        string? subjectType = GetArg(args, "--type");
        string? subjectId = GetArg(args, "--id");
        string? reason = GetArg(args, "--reason") ?? "Administratívne zneplatnenie";
        string serverEndpoint = GetArg(args, "--server") ?? "http://localhost:8080";
        string? apiKey = GetArg(args, "--key");

        if (string.IsNullOrWhiteSpace(subjectType) || string.IsNullOrWhiteSpace(subjectId))
        {
            AnsiConsole.MarkupLine("[red]Chyba: Parametre --type <license|machine|kid|relay|lease> a --id <id> sú povinné.[/]");
            return 1;
        }

        AnsiConsole.MarkupLine($"[bold yellow]Odvolávam oprávnenie pre:[/] [cyan]{subjectType}[/] [white]{subjectId}[/]");

        using var client = CreateClient(serverEndpoint, apiKey);

        var payload = new
        {
            subjectType = subjectType.ToLowerInvariant(),
            subjectId = subjectId,
            reason = reason
        };

        try
        {
            var response = await client.PostAsJsonAsync(
                new Uri("/admin/v1/revocations", UriKind.Relative),
                payload).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                string err = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                AnsiConsole.MarkupLine($"[red]Server odmietol požiadavku ({response.StatusCode}):[/] {err}");
                return 1;
            }

            var result = await response.Content.ReadFromJsonAsync<AdminRevocationResponseDto>().ConfigureAwait(false);
            AnsiConsole.MarkupLine($"[green]✓ Entita úspešne revokovaná![/]");
            if (result is not null)
            {
                AnsiConsole.MarkupLine($"  Sekvencia revokácie: [bold green]#{result.Sequence}[/]");
                AnsiConsole.MarkupLine($"  Dôvod: [white]{result.Reason}[/]");
                AnsiConsole.MarkupLine($"  Čas: [grey]{result.RevokedAt:u}[/]");
            }

            return 0;
        }
        catch (HttpRequestException ex)
        {
            AnsiConsole.MarkupLine($"[red]Chyba spojenia so serverom:[/] {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> HandleListAsync(string[] args)
    {
        string serverEndpoint = GetArg(args, "--server") ?? "http://localhost:8080";
        string? apiKey = GetArg(args, "--key");

        AnsiConsole.MarkupLine("[bold blue]=== Symbolon Aktívny Zoznam Revokácií (Enterprise CRL) ===[/]");

        using var client = CreateClient(serverEndpoint, apiKey);

        try
        {
            var response = await client.GetAsync(new Uri("/admin/v1/revocations", UriKind.Relative)).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]Server vrátil kód: {response.StatusCode}[/]");
                return 1;
            }

            var list = await response.Content.ReadFromJsonAsync<List<AdminRevocationItemDto>>().ConfigureAwait(false) ?? [];

            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumn("[bold green]Seq[/]");
            table.AddColumn("[cyan]Typ[/]");
            table.AddColumn("[white]Identifikátor[/]");
            table.AddColumn("[yellow]Dôvod[/]");
            table.AddColumn("[grey]Dátum revokácie[/]");

            foreach (var r in list)
            {
                string typeColor = r.SubjectType switch
                {
                    "license" => "[red]license[/]",
                    "machine" => "[magenta]machine[/]",
                    "kid" => "[yellow]kid[/]",
                    "relay" => "[blue]relay[/]",
                    "lease" => "[orange1]lease[/]",
                    _ => $"[grey]{r.SubjectType}[/]"
                };

                table.AddRow($"#{r.Sequence}", typeColor, r.SubjectId, r.Reason ?? "[grey]neuvedený[/]", r.RevokedAt.ToString("u", CultureInfo.InvariantCulture));
            }

            AnsiConsole.Write(table);
            return 0;
        }
        catch (HttpRequestException ex)
        {
            AnsiConsole.MarkupLine($"[red]Chyba sieťového spojenia:[/] {ex.Message}");
            return 1;
        }
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

    private static void PrintRevocationsHelp()
    {
        Console.WriteLine("""
            Použitie: symbolon revocations <subcommand> [options]

            Subcommands:
              fetch [--server <url>] [--out <file.symrl>] [--since <seq>]  Stiahnutie .symrl revokačného zoznamu zo servera
              inspect <file.symrl>                                          Inšpekcia kryptografického zoznamu .symrl
              revoke --type <typ> --id <id> [--reason <dôvod>] [options]    Okamžitá revokácia licencie/stroja/kľúča
              list [--server <url>] [--key <admin_api_key>]                 Zoznam revokácií zo serverovej databázy

            Možnosti pre revoke:
              --type <license|machine|kid|relay|lease>   Typ revokovaného subjektu (povinné)
              --id <identifikátor>                       ID entity, licenčný kľúč, fingerprint alebo kid (povinné)
              --reason <text>                            Dôvod zneplatnenia (voliteľné)
              --server <url>                             Endpoint licenčného servera (default: http://localhost:8080)
              --key <kľúč>                               Admin API kľúč (ak je vyžadovaný)
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
        AnsiConsole.MarkupLine($"[red]Neznámy príkaz pre revokácie:[/] {sub}");
        PrintRevocationsHelp();
        return 1;
    }
}

public sealed record AdminRevocationResponseDto(string SubjectType, string SubjectId, string? Reason, long Sequence, DateTimeOffset RevokedAt);
public sealed record AdminRevocationItemDto(string Id, string SubjectType, string SubjectId, string? Reason, long Sequence, DateTimeOffset RevokedAt);
