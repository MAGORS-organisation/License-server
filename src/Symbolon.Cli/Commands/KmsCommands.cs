using Spectre.Console;
using System.Security.Cryptography;
using Symbolon.Crypto;
using Symbolon.Crypto.Hierarchy;
using Symbolon.Crypto.Kms;

namespace Symbolon.Cli.Commands;

public static class KmsCommands
{
    public static Task<int> HandleKmsAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "STATUS" or "status")
        {
            string providerName = GetArg(args, "-p") ?? GetArg(args, "--provider") ?? "envelope";
            return HandleStatusAsync(providerName);
        }

        return Task.FromResult(UnknownSubcommand(args[0]));
    }

    public static Task<int> HandleStatusAsync(string providerName)
    {
        AnsiConsole.MarkupLine("[bold blue]=== Symbolon Cloud KMS & Hardware Security Module (HSM) Status ===[/]");

        IKmsProvider provider = providerName.ToLowerInvariant() switch
        {
            "azure-kv" => new AzureKeyVaultKmsProvider("https://symbolon-prod-vault.vault.azure.net"),
            "aws-kms" => new AwsKmsProvider("eu-central-1"),
            "pkcs11" => new MockHardwareHsmProvider(0, "Symbolon-HSM-Partition-01"),
            _ => new EncryptedEnvelopeKmsProvider("symbolon_cli_master_key_2026!", "envelope://local-cli")
        };

        using (provider)
        {
            var health = provider.CheckHealthAsync().GetAwaiter().GetResult();
            var keys = provider.ListKeysAsync().GetAwaiter().GetResult();

            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumn("[cyan]Parameter[/]");
            table.AddColumn("[green]Hodnota[/]");

            table.AddRow("Poskytovateľ (Provider)", $"[bold]{provider.ProviderType}[/]");
            table.AddRow("Stav Pripojenia", health.IsHealthy ? "[green]● ONLINE / HEALTHY[/]" : "[red]● OFFLINE[/]");
            table.AddRow("Podrobnosti", health.Details);
            table.AddRow("Spravované Kľúče", keys.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));

            AnsiConsole.Write(table);

            if (keys.Count > 0)
            {
                var keysTable = new Table().Border(TableBorder.Simple);
                keysTable.AddColumn("Key ID");
                keysTable.AddColumn("Algoritmus");
                keysTable.AddColumn("Umiestnenie (Location)");
                keysTable.AddColumn("Stav");

                foreach (var k in keys)
                {
                    keysTable.AddRow(k.KeyId, k.Algorithm, k.KeyLocation, k.State);
                }
                AnsiConsole.Write(keysTable);
            }
        }

        return Task.FromResult(0);
    }

    public static int HandleEnvelopeEncrypt(string[] args)
    {
        string? inputPath = GetArg(args, "--in") ?? GetArg(args, "-i");
        string? outputPath = GetArg(args, "--out") ?? GetArg(args, "-o");
        string? passphrase = GetArg(args, "--passphrase") ?? GetArg(args, "-p");
        string kid = GetArg(args, "--kid") ?? GetArg(args, "-k") ?? "sym-envelope-key-01";

        passphrase ??= AnsiConsole.Prompt(new TextPrompt<string>("Zadajte [green]heslo[/] pre zašifrovanie obálky:").Secret());

        byte[] pkcs8Bytes;
        if (!string.IsNullOrWhiteSpace(inputPath) && File.Exists(inputPath))
        {
            pkcs8Bytes = File.ReadAllBytes(inputPath);
        }
        else
        {
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            pkcs8Bytes = ecdsa.ExportPkcs8PrivateKey();
            AnsiConsole.MarkupLine("[yellow]Nebol zadaný existujúci kľúč, vygenerovaný nový NIST P-256 kľúč.[/]");
        }

        var envelope = EncryptedEnvelopeKeyStore.Encrypt(
            pkcs8Bytes: pkcs8Bytes,
            passphrase: passphrase,
            kid: kid,
            alg: "ES256",
            kty: "EC");

        string pem = EncryptedEnvelopeKeyStore.ToPem(envelope);

        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            File.WriteAllText(outputPath, pem);
            AnsiConsole.MarkupLine($"[green]✓ Zašifrovaná obálka bola uložená do: {outputPath}[/]");
        }
        else
        {
            AnsiConsole.WriteLine(pem);
        }

        return 0;
    }

    public static int HandleHierarchyVerify()
    {
        AnsiConsole.MarkupLine("[bold blue]=== Overenie 3-Úrovňovej Hierarchie Kľúčov (Root ➜ Product ➜ Lease) ===[/]");

        using var rootKey = Es256SignatureProvider.GenerateKey("sym-root-2026");
        var rootJwk = rootKey.ExportPublicJwk();

        using var productKey = Es256SignatureProvider.GenerateKey("prd-acme-2026-09-es");
        var productJwk = productKey.ExportPublicJwk();

        using var leaseKey = Es256SignatureProvider.GenerateKey("lse-node-01");
        var leaseJwk = leaseKey.ExportPublicJwk();

        var now = DateTimeOffset.UtcNow;
        var rootCert = KeyHierarchyEngine.IssueCertificate(
            rootKey, KeyTierRole.Root, rootJwk, KeyTierRole.Root, now.AddDays(-1), now.AddYears(10));

        var productCert = KeyHierarchyEngine.IssueCertificate(
            rootKey, KeyTierRole.Root, productJwk, KeyTierRole.Product, now.AddDays(-1), now.AddYears(2));

        var leaseCert = KeyHierarchyEngine.IssueCertificate(
            productKey, KeyTierRole.Product, leaseJwk, KeyTierRole.Lease, now.AddHours(-1), now.AddDays(30));

        var chain = new KeyHierarchyChain(rootCert, productCert, leaseCert);
        var result = KeyHierarchyEngine.VerifyChain(chain, rootJwk);

        var tree = new Tree("[bold green]Root Key Anchor[/] (" + rootJwk.Kid + ")");
        var productNode = tree.AddNode("[bold cyan]Product Authority[/] (" + productJwk.Kid + ") [dim]Platnosť: 2 roky[/]");
        productNode.AddNode("[bold yellow]Lease Key (Leaf)[/] (" + leaseJwk.Kid + ") [dim]Platnosť: 30 dní[/]");

        AnsiConsole.Write(tree);

        if (result.IsValid)
        {
            AnsiConsole.MarkupLine("[bold green]✓ Celý reťazec dôvery bol úspešne overený. Digitálne podpisy a časové prekryvy sú platné.[/]");
            return 0;
        }

        AnsiConsole.MarkupLine($"[bold red]✗ Overenie zlyhalo: {result.FailureReason}[/]");
        return 1;
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
        AnsiConsole.MarkupLine($"[bold red]Neznámy príkaz:[/] {sub}");
        return 1;
    }
}
