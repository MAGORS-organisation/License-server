using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Spectre.Console;
using Symbolon.Domain.Transparency;
using Symbolon.Protocol;

namespace Symbolon.Cli.Commands;

public static class TransparencyCommands
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task<int> HandleTransparencyAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintTransparencyHelp();
            return 0;
        }

        return args[0].ToUpperInvariant() switch
        {
            "ROOT" => await HandleRootAsync(args[1..]).ConfigureAwait(false),
            "STH" => await HandleSthAsync(args[1..]).ConfigureAwait(false),
            "INCLUSION" => await HandleInclusionAsync(args[1..]).ConfigureAwait(false),
            "CONSISTENCY" => await HandleConsistencyAsync(args[1..]).ConfigureAwait(false),
            "VERIFY-CONSISTENCY" => await HandleVerifyConsistencyAsync(args[1..]).ConfigureAwait(false),
            _ => UnknownSubcommand(args[0])
        };
    }

    private static async Task<int> HandleRootAsync(string[] args)
    {
        string serverEndpoint = GetArg(args, "--server") ?? "http://localhost:8080";
        bool jsonOutput = HasFlag(args, "--json");

        using var client = CreateClient(serverEndpoint);
        try
        {
            var response = await client.GetAsync(new Uri("/v1/transparency/root", UriKind.Relative)).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]Server vrátil chybu:[/] {response.StatusCode}");
                return 1;
            }

            var rootDto = await response.Content.ReadFromJsonAsync(SymbolonProtocolJsonContext.Default.TransparencyRootResponseDto).ConfigureAwait(false);
            if (rootDto is null)
            {
                AnsiConsole.MarkupLine("[red]Prázdna odpoveď od servera.[/]");
                return 1;
            }

            if (jsonOutput)
            {
                Console.WriteLine(JsonSerializer.Serialize(rootDto, JsonOptions));
                return 0;
            }

            var panel = new Panel(
                new Markup($"""
                [bold cyan]Koreňový hash (Root):[/] [yellow]{rootDto.RootHash}[/]
                [bold cyan]Veľkosť stromu (Veľkosť):[/] [green]{rootDto.TreeSize}[/] záznamov
                [bold cyan]Časová pečiatka:[/] {rootDto.Timestamp:yyyy-MM-dd HH:mm:ss} UTC
                """))
            {
                Header = new PanelHeader("[bold green] Merkle Transparency Log - Root [/]"),
                Border = BoxBorder.Rounded
            };
            AnsiConsole.Write(panel);
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Chyba komunikácie so serverom:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
    }

    private static async Task<int> HandleSthAsync(string[] args)
    {
        string serverEndpoint = GetArg(args, "--server") ?? "http://localhost:8080";
        bool jsonOutput = HasFlag(args, "--json");

        using var client = CreateClient(serverEndpoint);
        try
        {
            var response = await client.GetAsync(new Uri("/v1/transparency/sth", UriKind.Relative)).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]Server vrátil chybu:[/] {response.StatusCode}");
                return 1;
            }

            var sthDto = await response.Content.ReadFromJsonAsync(SymbolonProtocolJsonContext.Default.SignedTreeHeadDto).ConfigureAwait(false);
            if (sthDto is null)
            {
                AnsiConsole.MarkupLine("[red]Prázdna odpoveď od servera.[/]");
                return 1;
            }

            if (jsonOutput)
            {
                Console.WriteLine(JsonSerializer.Serialize(sthDto, JsonOptions));
                return 0;
            }

            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumn("[bold]Pole[/]");
            table.AddColumn("[bold]Hodnota[/]");

            table.AddRow("Tree Size", $"[green]{sthDto.TreeSize}[/]");
            table.AddRow("Timestamp", $"{sthDto.Timestamp:yyyy-MM-dd HH:mm:ss} UTC");
            table.AddRow("Root Hash", $"[yellow]{sthDto.RootHash}[/]");
            table.AddRow("Algorithm", $"[cyan]{sthDto.Algorithm}[/]");
            table.AddRow("Key ID", sthDto.KeyId);
            table.AddRow("Signature", $"[dim]{sthDto.Signature}[/]");

            var panel = new Panel(table)
            {
                Header = new PanelHeader("[bold green] Signed Tree Head (STH - RFC 6962 / Rekor) [/]"),
                Border = BoxBorder.Rounded
            };
            AnsiConsole.Write(panel);
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Chyba pri získavaní STH:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
    }

    private static async Task<int> HandleInclusionAsync(string[] args)
    {
        string? auditId = args.FirstOrDefault(a => !a.StartsWith('-'));
        if (string.IsNullOrWhiteSpace(auditId))
        {
            auditId = GetArg(args, "--id") ?? GetArg(args, "--audit-id");
        }

        if (string.IsNullOrWhiteSpace(auditId))
        {
            AnsiConsole.MarkupLine("[red]Chyba: Zadajte audit ID záznamu (symbolon transparency inclusion <auditId>).[/]");
            return 1;
        }

        string serverEndpoint = GetArg(args, "--server") ?? "http://localhost:8080";
        bool jsonOutput = HasFlag(args, "--json");

        using var client = CreateClient(serverEndpoint);
        try
        {
            var response = await client.GetAsync(new Uri($"/v1/transparency/inclusion/{Uri.EscapeDataString(auditId)}", UriKind.Relative)).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]Server vrátil chybu pri hľadaní záznamu:[/] {response.StatusCode}");
                return 1;
            }

            var inclDto = await response.Content.ReadFromJsonAsync(SymbolonProtocolJsonContext.Default.TransparencyInclusionResponseDto).ConfigureAwait(false);
            if (inclDto is null)
            {
                AnsiConsole.MarkupLine("[red]Prázdna odpoveď od servera.[/]");
                return 1;
            }

            if (jsonOutput)
            {
                Console.WriteLine(JsonSerializer.Serialize(inclDto, JsonOptions));
                return 0;
            }

            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumn("[bold]Krok[/]");
            table.AddColumn("[bold]Smer[/]");
            table.AddColumn("[bold]Sibling Hash[/]");

            int stepIdx = 0;
            foreach (var step in inclDto.Path)
            {
                table.AddRow((++stepIdx).ToString(CultureInfo.InvariantCulture), step.Direction, $"[dim]{step.Hash}[/]");
            }

            var localSteps = inclDto.Path.Select(p => new MerkleProofStep(p.Hash, p.Direction)).ToList();
            bool localValid = MerkleTree.VerifyInclusion(
                Convert.FromHexString(inclDto.LeafHash),
                Convert.FromHexString(inclDto.RootHash),
                localSteps);

            AnsiConsole.MarkupLine($"[bold cyan]Audit ID:[/] {inclDto.AuditId}");
            AnsiConsole.MarkupLine($"[bold cyan]Index v strome:[/] {inclDto.LeafIndex} / [green]{inclDto.TreeSize}[/]");
            AnsiConsole.MarkupLine($"[bold cyan]Leaf Hash:[/] [yellow]{inclDto.LeafHash}[/]");
            AnsiConsole.MarkupLine($"[bold cyan]Root Hash:[/] [yellow]{inclDto.RootHash}[/]");
            AnsiConsole.Write(table);

            if (localValid)
            {
                AnsiConsole.MarkupLine("[bold green] Lokálne kryptografické overenie inclusion proofu: ÚSPEŠNÉ (Záznam je overený v strome).[/]");
            }
            else
            {
                AnsiConsole.MarkupLine("[bold red] Lokálne kryptografické overenie inclusion proofu ZLYHALO! Nesúlad root hashu.[/]");
                return 1;
            }

            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Chyba pri overovaní inclusion proofu:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
    }

    private static async Task<int> HandleConsistencyAsync(string[] args)
    {
        string? oldSizeStr = GetArg(args, "--old-size") ?? GetArg(args, "-m");
        string? newSizeStr = GetArg(args, "--new-size") ?? GetArg(args, "-n");

        if (!int.TryParse(oldSizeStr, out int oldSize) || !int.TryParse(newSizeStr, out int newSize))
        {
            AnsiConsole.MarkupLine("[red]Chyba: Zadať platné veľkosti stromov: --old-size <m> --new-size <n>[/]");
            return 1;
        }

        string serverEndpoint = GetArg(args, "--server") ?? "http://localhost:8080";
        bool jsonOutput = HasFlag(args, "--json");

        using var client = CreateClient(serverEndpoint);
        try
        {
            string url = $"/v1/transparency/consistency?oldSize={oldSize}&newSize={newSize}";
            var response = await client.GetAsync(new Uri(url, UriKind.Relative)).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                string err = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                AnsiConsole.MarkupLine($"[red]Server vrátil chybu:[/] {response.StatusCode} - {Markup.Escape(err)}");
                return 1;
            }

            var dto = await response.Content.ReadFromJsonAsync(SymbolonProtocolJsonContext.Default.TransparencyConsistencyResponseDto).ConfigureAwait(false);
            if (dto is null)
            {
                AnsiConsole.MarkupLine("[red]Prázdna odpoveď od servera.[/]");
                return 1;
            }

            if (jsonOutput)
            {
                Console.WriteLine(JsonSerializer.Serialize(dto, JsonOptions));
                return 0;
            }

            bool localValid = MerkleTree.VerifyConsistency(
                Convert.FromHexString(dto.OldRoot),
                Convert.FromHexString(dto.NewRoot),
                dto.OldSize,
                dto.NewSize,
                dto.Proof);

            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumn("[bold]Krok[/]");
            table.AddColumn("[bold]Proof Hash[/]");

            int stepIdx = 0;
            foreach (var hash in dto.Proof)
            {
                table.AddRow((++stepIdx).ToString(CultureInfo.InvariantCulture), $"[yellow]{hash}[/]");
            }

            AnsiConsole.MarkupLine($"[bold cyan]Stará veľkosť (m):[/] [green]{dto.OldSize}[/] | [bold cyan]Starý koreň:[/] [dim]{dto.OldRoot}[/]");
            AnsiConsole.MarkupLine($"[bold cyan]Nová veľkosť (n):[/] [green]{dto.NewSize}[/] | [bold cyan]Nový koreň:[/] [dim]{dto.NewRoot}[/]");
            AnsiConsole.Write(table);

            if (localValid)
            {
                AnsiConsole.MarkupLine("[bold green] Lokálne overenie Consistency Proofu: ÚSPEŠNÉ (Append-only integrita potvrdená).[/]");
            }
            else
            {
                AnsiConsole.MarkupLine("[bold red] Lokálne overenie Consistency Proofu: ZLYHALO! História stromu bola modifikovaná.[/]");
                return 1;
            }

            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Chyba consistency proofu:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
    }

    private static async Task<int> HandleVerifyConsistencyAsync(string[] args)
    {
        string? oldSizeStr = GetArg(args, "--old-size") ?? GetArg(args, "-m");
        string? newSizeStr = GetArg(args, "--new-size") ?? GetArg(args, "-n");
        string? oldRoot = GetArg(args, "--old-root");
        string? newRoot = GetArg(args, "--new-root");
        string? proofCsv = GetArg(args, "--proof");

        if (!int.TryParse(oldSizeStr, out int oldSize) ||
            !int.TryParse(newSizeStr, out int newSize) ||
            string.IsNullOrWhiteSpace(oldRoot) ||
            string.IsNullOrWhiteSpace(newRoot))
        {
            AnsiConsole.MarkupLine("[red]Chyba: Zadať --old-size <m>, --new-size <n>, --old-root <hex>, --new-root <hex> a voliteľne --proof <h1,h2,...>[/]");
            return 1;
        }

        var proofList = string.IsNullOrWhiteSpace(proofCsv)
            ? (IReadOnlyList<string>)Array.Empty<string>()
            : proofCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        try
        {
            bool isValid = MerkleTree.VerifyConsistency(
                Convert.FromHexString(oldRoot),
                Convert.FromHexString(newRoot),
                oldSize,
                newSize,
                proofList);

            if (isValid)
            {
                AnsiConsole.MarkupLine($"[bold green] Consistency Proof VERIFIED:[/] Strom s veľkosťou {oldSize} je skutočným predchodcom stromu {newSize}.");
                return 0;
            }
            else
            {
                AnsiConsole.MarkupLine($"[bold red] Consistency Proof INVALID:[/] Kontrola integrity zlyhala pre veľkosti {oldSize} -> {newSize}.");
                return 1;
            }
        }
        catch (FormatException ex)
        {
            AnsiConsole.MarkupLine($"[red]Chyba formátu hex reťazca:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
    }

    private static HttpClient CreateClient(string serverEndpoint)
    {
        return new HttpClient
        {
            BaseAddress = new Uri(serverEndpoint.TrimEnd('/')),
            Timeout = TimeSpan.FromSeconds(15)
        };
    }

    private static void PrintTransparencyHelp()
    {
        Console.WriteLine("""
            Použitie: symbolon transparency <subcommand> [options]

            Subcommands:
              root [--server <url>] [--json]
                  Získať aktuálny koreňový hash (Merkle root) a veľkosť stromu auditných logov.
              sth [--server <url>] [--json]
                  Získať kryptograficky podpísanú hlavičku stromu (Signed Tree Head - STH).
              inclusion <auditId> [--server <url>] [--json]
                  Získať a overiť Merkle inclusion proof pre konkrétnu auditnú udalosť.
              consistency --old-size <m> --new-size <n> [--server <url>] [--json]
                  Vyžiadať zo servera a lokálne overiť Merkle consistency proof medzi verziami m a n.
              verify-consistency --old-size <m> --new-size <n> --old-root <hex> --new-root <hex> [--proof <h1,h2...>]
                  Nezávisle kryptograficky overiť konzistenciu dvoch verzií stromu bez servera.
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
        AnsiConsole.MarkupLine($"[red]Neznámy príkaz pre transparency log:[/] {sub}");
        PrintTransparencyHelp();
        return 1;
    }
}
