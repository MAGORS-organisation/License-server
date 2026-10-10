using System.Text.Json;
using Spectre.Console;
using Achilles.Crypto;
using Achilles.Format.Attestation;

namespace Achilles.Cli.Commands;

public static class AttestationCommands
{
    public static async Task<int> HandleAttestationAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintAttestationHelp();
            return 0;
        }

        return args[0].ToUpperInvariant() switch
        {
            "VERIFY" => await HandleVerifyAsync(args[1..]).ConfigureAwait(false),
            _ => UnknownSubcommand(args[0])
        };
    }

    private static async Task<int> HandleVerifyAsync(string[] args)
    {
        string? quotePath = GetArg(args, "--quote");
        string? keyPath = GetArg(args, "--key");
        string? nonce = GetArg(args, "--nonce");
        string? expectedPcr = GetArg(args, "--pcr");

        if (string.IsNullOrWhiteSpace(quotePath) || !File.Exists(quotePath))
        {
            AnsiConsole.MarkupLine("[red]Chyba: Zadať platnú cestu k citácii (--quote <quote.json>)[/]");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(keyPath) || !File.Exists(keyPath))
        {
            AnsiConsole.MarkupLine("[red]Chyba: Zadať platnú cestu k verejnému AIK kľúču (--key <aik.jwk>)[/]");
            return 1;
        }

        string quoteJson = await File.ReadAllTextAsync(quotePath).ConfigureAwait(false);
        string keyJson = await File.ReadAllTextAsync(keyPath).ConfigureAwait(false);

        HardwareAttestationQuote? quote;
        try
        {
            quote = JsonSerializer.Deserialize<HardwareAttestationQuote>(quoteJson);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Chyba pri parsovaní citácie:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }

        if (quote is null)
        {
            AnsiConsole.MarkupLine("[red]Chyba: Súbor neobsahuje platnú HardwareAttestationQuote štruktúru.[/]");
            return 1;
        }

        JsonWebKeyDto? jwk;
        try
        {
            jwk = JsonSerializer.Deserialize<JsonWebKeyDto>(keyJson);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Chyba pri načítaní verejného kľúča:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }

        if (jwk is null)
        {
            AnsiConsole.MarkupLine("[red]Chyba: Neplatný verejný kľúč JWK.[/]");
            return 1;
        }

        using var verifier = Es256SignatureProvider.ImportJwk(jwk);
        string challengeNonce = nonce ?? quote.Nonce;

        var result = TpmQuoteVerifier.VerifyQuote(quote, verifier, challengeNonce, expectedPcr);

        if (result.IsValid)
        {
            AnsiConsole.MarkupLine("[green bold]✔ HARDVÉROVÁ CITÁCIA ENCLAVE/TPM 2.0 JE KRYPTOGRAFICKY PLATNÁ[/]");
            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumn("Parameter");
            table.AddColumn("Hodnota");
            table.AddRow("Typ Enclave", quote.EnclaveType.ToString());
            table.AddRow("AIK ID", quote.AikId);
            table.AddRow("Anti-Replay Nonce", quote.Nonce);
            table.AddRow("PCR Registre", string.Join(", ", quote.PcrIndices));
            table.AddRow("PCR Digest", quote.PcrDigest);
            table.AddRow("Algoritmus podpisu", quote.SignatureAlgorithm);
            table.AddRow("Časová pečiatka", quote.Timestamp.ToString("u", System.Globalization.CultureInfo.InvariantCulture));
            AnsiConsole.Write(table);
            return 0;
        }
        else
        {
            AnsiConsole.MarkupLine($"[red bold]✖ HARDVÉROVÁ CITÁCIA JE NEPLATNÁ:[/] {Markup.Escape(result.FailureReason ?? "Neznáma chyba")}");
            return 1;
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

    private static int UnknownSubcommand(string subcmd)
    {
        AnsiConsole.MarkupLine($"[red]Neznámy Attestation príkaz: {Markup.Escape(subcmd)}[/]");
        PrintAttestationHelp();
        return 1;
    }

    private static void PrintAttestationHelp()
    {
        AnsiConsole.MarkupLine("""
            [bold]Použitie:[/] symbolon attestation verify [options]

            Príkazy:
              verify  --quote <file.json> --key <aik.jwk> [--nonce <str>] [--pcr <digest>]
            """);
    }
}
