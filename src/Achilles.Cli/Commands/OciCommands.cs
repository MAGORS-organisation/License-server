using System.Security.Cryptography;
using System.Text.Json;
using Spectre.Console;
using Achilles.Format.Oci;

namespace Achilles.Cli.Commands;

public static class OciCommands
{
    public static async Task<int> HandleOciAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintOciHelp();
            return 0;
        }

        return args[0].ToUpperInvariant() switch
        {
            "PACK" => await HandlePackAsync(args[1..]).ConfigureAwait(false),
            "VERIFY" => await HandleVerifyAsync(args[1..]).ConfigureAwait(false),
            _ => UnknownSubcommand(args[0])
        };
    }

    private static async Task<int> HandlePackAsync(string[] args)
    {
        string? licensePath = GetArg(args, "--license");
        string? jwksPath = GetArg(args, "--jwks");
        string? customer = GetArg(args, "--customer") ?? "CUST-DEFAULT";
        string? product = GetArg(args, "--product") ?? "acme-app";
        string? outPath = GetArg(args, "--out");

        if (string.IsNullOrWhiteSpace(licensePath) || !File.Exists(licensePath))
        {
            AnsiConsole.MarkupLine("[red]Chyba: Zadať platnú cestu k licencii (--license <file.symlic>)[/]");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(jwksPath) || !File.Exists(jwksPath))
        {
            AnsiConsole.MarkupLine("[red]Chyba: Zadať platnú cestu k JWKS kľúčom (--jwks <file.jwks>)[/]");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(outPath))
        {
            outPath = "symbolon-bundle.tar";
        }

        string licenseContent = await File.ReadAllTextAsync(licensePath).ConfigureAwait(false);
        string jwksContent = await File.ReadAllTextAsync(jwksPath).ConfigureAwait(false);

        try
        {
            byte[] tarBytes = OciLicenseBundle.CreateBundle(licenseContent, jwksContent, customer, product);
            await File.WriteAllBytesAsync(outPath, tarBytes).ConfigureAwait(false);

            AnsiConsole.MarkupLine($"[green]✔ OCI balíček úspešne vytvorený:[/] [bold]{outPath}[/]");
            
            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumn("Vlastnosť");
            table.AddColumn("Hodnota");
            table.AddRow("Veľkosť archívu", $"{tarBytes.Length:N0} bajtov");
            table.AddRow("SHA-256 Digest", $"sha256:{Convert.ToHexStringLower(SHA256.HashData(tarBytes))}");
            table.AddRow("Zákazník", customer);
            table.AddRow("Produkt", product);
            table.AddRow("OCI Media Type", OciLicenseBundle.OciManifestMediaType);
            AnsiConsole.Write(table);

            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Zlyhanie pri balení OCI:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
    }

    private static async Task<int> HandleVerifyAsync(string[] args)
    {
        if (args.Length == 0 || !File.Exists(args[0]))
        {
            AnsiConsole.MarkupLine("[red]Chyba: Zadať platný OCI archív (symbolon oci verify <bundle.tar> [--fingerprint <fp>])[/]");
            return 1;
        }

        string bundlePath = args[0];
        string? expectedFp = GetArg(args, "--fingerprint");

        byte[] tarBytes = await File.ReadAllBytesAsync(bundlePath).ConfigureAwait(false);
        var result = OciLicenseBundle.VerifyBundle(tarBytes, expectedFp);

        if (result.IsValid)
        {
            AnsiConsole.MarkupLine("[green bold]✔ OCI BALÍČEK JE KRYPTOGRAFICKY PLATNÝ[/]");
            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumn("Parameter");
            table.AddColumn("Hodnota");

            if (result.LicenseDetails is not null)
            {
                table.AddRow("Zákazník", result.LicenseDetails.Customer ?? "—");
                table.AddRow("Produkt", result.LicenseDetails.Product ?? "—");
                table.AddRow("Typ Licencie", result.LicenseDetails.LicenseType ?? "—");
                table.AddRow("Sedadlá", result.LicenseDetails.MaxSeats?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "Neobmedzené");
                table.AddRow("Expirácia", result.LicenseDetails.ExpiresAt.HasValue
                    ? DateTimeOffset.FromUnixTimeSeconds(result.LicenseDetails.ExpiresAt.Value).ToString("u", System.Globalization.CultureInfo.InvariantCulture)
                    : "Perpetual");
            }
            AnsiConsole.Write(table);
            return 0;
        }
        else
        {
            AnsiConsole.MarkupLine($"[red bold]✖ OCI BALÍČEK JE NEPLATNÝ:[/] {Markup.Escape(result.FailureReason ?? "Neznáma chyba")}");
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
        AnsiConsole.MarkupLine($"[red]Neznámy OCI príkaz: {Markup.Escape(subcmd)}[/]");
        PrintOciHelp();
        return 1;
    }

    private static void PrintOciHelp()
    {
        AnsiConsole.MarkupLine("""
            [bold]Použitie:[/] symbolon oci <pack|verify> [options]

            Príkazy:
              pack    --license <file.symlic> --jwks <file.jwks> [--customer <id>] [--product <id>] [--out <bundle.tar>]
              verify  <bundle.tar> [--fingerprint <sha256:hex>]
            """);
    }
}
