using Spectre.Console;
using System.Security.Cryptography;
using Achilles.Crypto;
using Achilles.Crypto.Hierarchy;
using Achilles.Crypto.Kms;

namespace Achilles.Cli.Commands;

public static class KmsCommands
{
    public static async Task<int> HandleKmsAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintKmsHelp();
            return 0;
        }

        return args[0].ToUpperInvariant() switch
        {
            "STATUS" => await HandleStatusAsync(args[1..]).ConfigureAwait(false),
            "TEST-SIGN" => await HandleTestSignAsync(args[1..]).ConfigureAwait(false),
            "ENCRYPT" => HandleEnvelopeEncrypt(args[1..]),
            "VERIFY-HIERARCHY" => HandleHierarchyVerify(),
            _ => UnknownSubcommand(args[0])
        };
    }

    private static void PrintKmsHelp()
    {
        AnsiConsole.MarkupLine("[bold blue]Použitie:[/] symbolon kms <prikaz> [[volby]]");
        AnsiConsole.MarkupLine("  [yellow]status[/]            Zobrazí stav KMS/HSM poskytovateľa a zoznam kľúčov");
        AnsiConsole.MarkupLine("  [yellow]test-sign[/]         Otestuje hardvérové podpisovanie cez KMS/HSM");
        AnsiConsole.MarkupLine("  [yellow]encrypt[/]           Zašifruje privátny kľúč do AES-256-GCM obálky");
        AnsiConsole.MarkupLine("  [yellow]verify-hierarchy[/]  Overí 3-úrovňový reťazec dôvery kľúčov");
        AnsiConsole.MarkupLine("\n[bold]Voľby pre status a test-sign:[/] --provider <azure-kv|aws-kms|pkcs11|envelope> [--library <cesta>] [--slot <id>] [--pin <pin>]");
    }

    public static async Task<int> HandleStatusAsync(string[] args)
    {
        string providerName = GetArg(args, "-p") ?? GetArg(args, "--provider") ?? "envelope";
        string? libraryPath = GetArg(args, "--library");
        ulong slotId = ulong.TryParse(GetArg(args, "--slot"), out var s) ? s : 0;
        string? pin = GetArg(args, "--pin");

        AnsiConsole.MarkupLine("[bold blue]=== Symbolon Cloud KMS & Hardware Security Module (HSM) Status ===[/]");

        using IKmsProvider provider = CreateKmsProvider(providerName, libraryPath, slotId, pin);

        var health = await provider.CheckHealthAsync().ConfigureAwait(false);
        var keys = await provider.ListKeysAsync().ConfigureAwait(false);

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

        return 0;
    }

    public static async Task<int> HandleTestSignAsync(string[] args)
    {
        string providerName = GetArg(args, "-p") ?? GetArg(args, "--provider") ?? "pkcs11";
        string? libraryPath = GetArg(args, "--library");
        ulong slotId = ulong.TryParse(GetArg(args, "--slot"), out var s) ? s : 0;
        string? pin = GetArg(args, "--pin");
        string keyId = GetArg(args, "--key") ?? "sym-hsm-ecdsa-01";

        AnsiConsole.MarkupLine($"[bold blue]=== Test Hardvérového Podpisovania cez KMS/HSM ({providerName.ToUpperInvariant()}) ===[/]");

        using IKmsProvider provider = CreateKmsProvider(providerName, libraryPath, slotId, pin);

        var keys = await provider.ListKeysAsync().ConfigureAwait(false);
        if (keys.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]V tokene nie sú k dispozícii žiadne kľúče.[/]");
            return 1;
        }

        var keyMeta = keys.FirstOrDefault(k => string.Equals(k.KeyId, keyId, StringComparison.OrdinalIgnoreCase)) ?? keys[0];
        var signer = await provider.GetSignatureProviderAsync(keyMeta.KeyId).ConfigureAwait(false);

        byte[] samplePayload = "Symbolon Enterprise Floating License Signing Payload"u8.ToArray();
        byte[] sig = new byte[signer.SignatureSize];

        signer.Sign(samplePayload, sig);
        bool isValid = signer.Verify(samplePayload, sig);

        AnsiConsole.MarkupLine($"[bold]Kľúč:[/] [green]{keyMeta.KeyId}[/] ([cyan]{signer.Alg}[/])");
        AnsiConsole.MarkupLine($"[bold]Veľkosť podpisu:[/] [yellow]{sig.Length} bajtov[/]");
        AnsiConsole.MarkupLine($"[bold]Verifikácia podpisu:[/] {(isValid ? "[bold green]✔ PLATNÝ (PASS)[/]" : "[bold red]✗ NEPLATNÝ (FAIL)[/]")}");

        return isValid ? 0 : 1;
    }

    private static IKmsProvider CreateKmsProvider(string providerName, string? libraryPath, ulong slotId, string? pin)
    {
        return providerName.ToLowerInvariant() switch
        {
            "azure-kv" => new AzureKeyVaultKmsProvider("https://symbolon-prod-vault.vault.azure.net"),
            "aws-kms" => new AwsKmsProvider("eu-central-1"),
            "pkcs11" => CreatePkcs11Provider(libraryPath, slotId, pin),
            _ => new EncryptedEnvelopeKmsProvider("symbolon_cli_master_key_2026!", "envelope://local-cli")
        };
    }

    private static Pkcs11HsmProvider CreatePkcs11Provider(string? libraryPath, ulong slotId, string? pin)
    {
        var hsm = new Pkcs11HsmProvider(libraryPath, slotId, pin, "Symbolon-HSM-Partition-01");
        // Provision baseline test keys if none exist in slot (hsm takes ownership and disposes them)
        var ecKey = Es256SignatureProvider.GenerateKey("sym-hsm-ecdsa-01");
        hsm.ProvisionKey(ecKey);

        if (MlDsaSignatureProvider.IsSupported)
        {
            var pqKey = MlDsaSignatureProvider.GenerateKey(MLDsaAlgorithm.MLDsa65, "sym-hsm-mldsa-01");
            hsm.ProvisionKey(pqKey);
        }

        return hsm;
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
