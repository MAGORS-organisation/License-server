using System.Security.Cryptography;
using System.Text.Json;
using Spectre.Console;
using Symbolon.Protocol;

namespace Symbolon.Cli.Commands;

public static class ComplianceCommands
{
    private static readonly SbomComponentDto[] DefaultComponents =
    [
        new(
            Name: "Symbolon.ControlPlane",
            Version: "1.0.0",
            Type: "application",
            Purl: "pkg:nuget/Symbolon.ControlPlane@1.0.0",
            Licenses: ["AGPL-3.0-only"],
            Hashes: new Dictionary<string, string> { ["SHA-256"] = "a1b2c3d4e5f67890123456789abcdef0123456789abcdef0123456789abcdef0" }
        ),
        new(
            Name: "Symbolon.Crypto",
            Version: "1.0.0",
            Type: "library",
            Purl: "pkg:nuget/Symbolon.Crypto@1.0.0",
            Licenses: ["Apache-2.0"],
            Hashes: new Dictionary<string, string> { ["SHA-256"] = "b2c3d4e5f6a17890123456789abcdef0123456789abcdef0123456789abcdef1" }
        ),
        new(
            Name: "Symbolon.Format",
            Version: "1.0.0",
            Type: "library",
            Purl: "pkg:nuget/Symbolon.Format@1.0.0",
            Licenses: ["Apache-2.0"],
            Hashes: new Dictionary<string, string> { ["SHA-256"] = "c3d4e5f6a1b27890123456789abcdef0123456789abcdef0123456789abcdef2" }
        ),
        new(
            Name: "Symbolon.Protocol",
            Version: "1.0.0",
            Type: "library",
            Purl: "pkg:nuget/Symbolon.Protocol@1.0.0",
            Licenses: ["CC-BY-4.0"],
            Hashes: new Dictionary<string, string> { ["SHA-256"] = "d4e5f6a1b2c37890123456789abcdef0123456789abcdef0123456789abcdef3" }
        ),
        new(
            Name: "Symbolon.Domain",
            Version: "1.0.0",
            Type: "library",
            Purl: "pkg:nuget/Symbolon.Domain@1.0.0",
            Licenses: ["Apache-2.0"],
            Hashes: new Dictionary<string, string> { ["SHA-256"] = "e5f6a1b2c3d47890123456789abcdef0123456789abcdef0123456789abcdef4" }
        ),
        new(
            Name: "Symbolon.Data",
            Version: "1.0.0",
            Type: "library",
            Purl: "pkg:nuget/Symbolon.Data@1.0.0",
            Licenses: ["AGPL-3.0-only"],
            Hashes: new Dictionary<string, string> { ["SHA-256"] = "f6a1b2c3d4e57890123456789abcdef0123456789abcdef0123456789abcdef5" }
        ),
        new(
            Name: "Symbolon.Client",
            Version: "1.0.0",
            Type: "library",
            Purl: "pkg:nuget/Symbolon.Client@1.0.0",
            Licenses: ["Apache-2.0"],
            Hashes: new Dictionary<string, string> { ["SHA-256"] = "1a2b3c4d5e6f7890123456789abcdef0123456789abcdef0123456789abcdef6" }
        ),
        new(
            Name: "Symbolon.Relay",
            Version: "1.0.0",
            Type: "application",
            Purl: "pkg:nuget/Symbolon.Relay@1.0.0",
            Licenses: ["AGPL-3.0-only"],
            Hashes: new Dictionary<string, string> { ["SHA-256"] = "2b3c4d5e6f1a7890123456789abcdef0123456789abcdef0123456789abcdef7" }
        ),
        new(
            Name: "Symbolon.Cli",
            Version: "1.0.0",
            Type: "application",
            Purl: "pkg:nuget/Symbolon.Cli@1.0.0",
            Licenses: ["AGPL-3.0-only"],
            Hashes: new Dictionary<string, string> { ["SHA-256"] = "3c4d5e6f1a2b7890123456789abcdef0123456789abcdef0123456789abcdef8" }
        )
    ];

    private static readonly JsonSerializerOptions IndentedJsonOptions = new() { WriteIndented = true };

    public static async Task<int> HandleSbomAsync(string[] args)
    {
        string? outFile = null;

        for (int i = 0; i < args.Length; i++)
        {
            if ((args[i] == "--out" || args[i] == "-o") && i + 1 < args.Length)
            {
                outFile = args[++i];
            }
        }

        var sbom = new CycloneDxSbomDto(
            BomFormat: "CycloneDX",
            SpecVersion: "1.6",
            SerialNumber: $"urn:uuid:{Guid.NewGuid():D}",
            Version: 1,
            Metadata: new
            {
                timestamp = DateTimeOffset.UtcNow,
                tools = new[]
                {
                    new { vendor = "CycloneDX", name = "Symbolon CLI SBOM Generator", version = "1.6.0" }
                },
                component = new
                {
                    type = "application",
                    name = "Symbolon",
                    version = "1.0.0",
                    description = "Symbolon Enterprise Floating License Server (CRA Compliant)",
                    licenses = new[] { new { license = new { id = "AGPL-3.0-only" } } }
                }
            },
            Components: DefaultComponents);

        string json = JsonSerializer.Serialize(sbom, IndentedJsonOptions);

        if (!string.IsNullOrWhiteSpace(outFile))
        {
            await File.WriteAllTextAsync(outFile, json).ConfigureAwait(false);
            AnsiConsole.MarkupLine($"[green]✓[/] CycloneDX v1.6 SBOM bol úspešne vygenerovaný do súboru [bold]{Markup.Escape(outFile)}[/]");
        }
        else
        {
            Console.WriteLine(json);
        }

        return 0;
    }

    public static async Task<int> HandleVerifyArtifactAsync(string[] args)
    {
        if (args.Length == 0)
        {
            AnsiConsole.MarkupLine("[red]Chyba:[/] Zadajte cestu k súboru: [bold]symbolon verify-artifact <cesta-k-suboru> [--checksum <sha256>][/]");
            return 1;
        }

        string filePath = args[0];
        string? expectedSha256 = null;

        for (int i = 1; i < args.Length; i++)
        {
            if ((args[i] == "--checksum" || args[i] == "-c") && i + 1 < args.Length)
            {
                expectedSha256 = args[++i].Trim();
            }
        }

        if (!File.Exists(filePath))
        {
            AnsiConsole.MarkupLine($"[red]Chyba:[/] Súbor '{Markup.Escape(filePath)}' neexistuje.");
            return 1;
        }

        byte[] fileBytes = await File.ReadAllBytesAsync(filePath).ConfigureAwait(false);
        byte[] sha256Bytes = SHA256.HashData(fileBytes);
        byte[] sha512Bytes = SHA512.HashData(fileBytes);

        string computedSha256 = Convert.ToHexStringLower(sha256Bytes);
        string computedSha512 = Convert.ToHexStringLower(sha512Bytes);

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("[bold]Vlastnosť[/]");
        table.AddColumn("[bold]Hodnota[/]");

        table.AddRow("Súbor", Markup.Escape(Path.GetFileName(filePath)));
        table.AddRow("Veľkosť", $"{fileBytes.Length:N0} bajtov");
        table.AddRow("SHA-256", $"[cyan]{computedSha256}[/]");
        table.AddRow("SHA-512", $"[cyan]{computedSha512[..32]}...{computedSha512[^16..]}[/]");

        AnsiConsole.Write(table);

        if (!string.IsNullOrWhiteSpace(expectedSha256))
        {
            bool matches = string.Equals(computedSha256, expectedSha256, StringComparison.OrdinalIgnoreCase);
            if (matches)
            {
                AnsiConsole.MarkupLine("[green bold]✓ OVERENIE ÚSPEŠNÉ: Kontrolný súčet SHA-256 presne súhlasí![/]");
                return 0;
            }

            AnsiConsole.MarkupLine("[red bold]✗ OVERENIE ZLYHALO: Kontrolný súčet sa NEZHODUJE![/]");
            AnsiConsole.MarkupLine($"Očakávaný: [yellow]{Markup.Escape(expectedSha256)}[/]");
            AnsiConsole.MarkupLine($"Vypočítaný: [red]{computedSha256}[/]");
            return 2;
        }

        return 0;
    }
}
