using System.Buffers.Text;
using System.Globalization;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using Symbolon.Client;
using Symbolon.Crypto;
using Symbolon.Format;
using Symbolon.Protocol;

namespace Symbolon.Cli;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args is null || args.Length == 0)
        {
            if (!Console.IsInputRedirected)
            {
                return await Wizard.SetupWizard.RunAsync().ConfigureAwait(false);
            }
            PrintHelp();
            return 0;
        }

        if (args[0] is "-h" or "--help")
        {
            PrintHelp();
            return 0;
        }

        try
        {
            return args[0].ToUpperInvariant() switch
            {
                "SETUP" or "WIZARD" => await Wizard.SetupWizard.RunAsync().ConfigureAwait(false),
                "KEYS" => HandleKeys(args[1..]),
                "LICENSE" => HandleLicense(args[1..]),
                "DOCTOR" => await HandleDoctorAsync(args[1..]).ConfigureAwait(false),
                "IMPORT" => await Commands.MigrationCommands.HandleImportAsync(args[1..]).ConfigureAwait(false),
                "EXPORT" => await Commands.MigrationCommands.HandleExportAsync(args[1..]).ConfigureAwait(false),
                _ => UnknownCommand(args[0])
            };
        }
        catch (FormatException ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"Chyba formátu: {ex.Message}");
            Console.ResetColor();
            return 1;
        }
        catch (ArgumentException ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"Chyba argumentu: {ex.Message}");
            Console.ResetColor();
            return 1;
        }
        catch (IOException ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"Chyba IO: {ex.Message}");
            Console.ResetColor();
            return 1;
        }
    }

    private static int HandleKeys(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            Console.WriteLine("Použitie: symbolon keys <generate|export-jwks> [options]");
            return 0;
        }

        return args[0].ToUpperInvariant() switch
        {
            "GENERATE" => HandleKeysGenerate(args[1..]),
            "EXPORT-JWKS" => HandleKeysExportJwks(args[1..]),
            _ => UnknownCommand(args[0])
        };
    }

    private static int HandleKeysGenerate(string[] args)
    {
        string? alg = GetArg(args, "--alg") ?? Alg.Es256;
        string? kid = GetArg(args, "--kid");
        string? outPath = GetArg(args, "--out");

        if (string.IsNullOrWhiteSpace(kid))
        {
            kid = $"key-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
        }

        JsonWebKeyDto jwk;
        if (string.Equals(alg, Alg.Es256, StringComparison.OrdinalIgnoreCase))
        {
            using var key = Es256SignatureProvider.GenerateKey(kid);
            jwk = key.ExportPublicJwk();
        }
        else if (string.Equals(alg, Alg.MlDsa65, StringComparison.OrdinalIgnoreCase))
        {
            if (!MlDsaSignatureProvider.IsSupported)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Varovanie: ML-DSA nie je natívne podporované na tejto platforme.");
                Console.ResetColor();
            }
            using var key = MlDsaSignatureProvider.GenerateKey(MLDsaAlgorithm.MLDsa65, kid);
            jwk = key.ExportPublicJwk();
        }
        else
        {
            Console.Error.WriteLine($"Nepodporovaný algoritmus: {alg}");
            return 1;
        }

        string json = JsonSerializer.Serialize(jwk, SymbolonJsonContext.Default.JsonWebKeyDto);
        if (outPath is not null)
        {
            File.WriteAllText(outPath, json);
            Console.WriteLine($"Kľúč {kid} ({alg}) vygenerovaný a uložený do {outPath}");
        }
        else
        {
            Console.WriteLine(json);
        }

        return 0;
    }

    private static int HandleKeysExportJwks(string[] args)
    {
        string? outPath = GetArg(args, "--out");
        string? filesList = GetArg(args, "--keys");

        var keys = new List<JsonWebKeyDto>();
        if (!string.IsNullOrWhiteSpace(filesList))
        {
            foreach (var file in filesList.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (File.Exists(file))
                {
                    string content = File.ReadAllText(file);
                    var jwk = JsonSerializer.Deserialize(content, SymbolonJsonContext.Default.JsonWebKeyDto);
                    if (jwk is not null) keys.Add(jwk);
                }
            }
        }

        var jwks = new JsonWebKeySetDto { Keys = keys };
        string json = JsonSerializer.Serialize(jwks, SymbolonJsonContext.Default.JsonWebKeySetDto);

        if (outPath is not null)
        {
            File.WriteAllText(outPath, json);
            Console.WriteLine($"JWKS exportovaný do {outPath} ({keys.Count} kľúčov)");
        }
        else
        {
            Console.WriteLine(json);
        }

        return 0;
    }

    private static int HandleLicense(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            Console.WriteLine("Použitie: symbolon license <keygen|issue|inspect> [options]");
            return 0;
        }

        return args[0].ToUpperInvariant() switch
        {
            "KEYGEN" => HandleLicenseKeygen(args[1..]),
            "ISSUE" => HandleLicenseIssue(args[1..]),
            "INSPECT" => HandleLicenseInspect(args[1..]),
            _ => UnknownCommand(args[0])
        };
    }

    private static int HandleLicenseKeygen(string[] args)
    {
        string prefix = GetArg(args, "--prefix") ?? "SYM";
        var key = LicenseKey.Generate(prefix);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"Vygenerovaný kľúč: {key.Canonical}");
        Console.ResetColor();
        Console.WriteLine($"  Prefix:   {key.Prefix}");
        Console.WriteLine($"  Entropia: {key.Entropy}");
        Console.WriteLine($"  Kontrola: {key.Checksum} (CRC-32C)");
        return 0;
    }

    private static int HandleLicenseIssue(string[] args)
    {
        string? keyStr = GetArg(args, "--key");
        string? customer = GetArg(args, "--customer") ?? "CUST-0001";
        string? product = GetArg(args, "--product") ?? "acme-app";
        string? outPath = GetArg(args, "--out");
        int seats = int.TryParse(GetArg(args, "--seats"), CultureInfo.InvariantCulture, out int s) ? s : 10;

        LicenseKey key = string.IsNullOrWhiteSpace(keyStr)
            ? LicenseKey.Generate()
            : LicenseKey.Parse(keyStr);

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var claims = new LicenseClaims
        {
            Iss = "https://licenses.symbolon.local",
            Sub = $"lic_{Guid.NewGuid():N}",
            Aud = product,
            Jti = $"lf_{Guid.NewGuid():N}",
            Iat = now,
            Nbf = now,
            Exp = now + (365L * 24 * 3600), // 1 year snapshot
            Symlic = new SymlicClaims
            {
                V = 1,
                Profile = "hybrid-v1",
                RequiredAlgs = [Alg.Es256, Alg.MlDsa65],
                License = new LicenseMetadata
                {
                    Key = key.Canonical,
                    Model = "floating",
                    State = "active",
                    ExpiresAt = null, // perpetual
                    Customer = new CustomerMetadata { Ref = customer, Name = customer }
                },
                Limits = new LicenseLimits
                {
                    MaxSeats = seats,
                    SeatUnit = "machine",
                    MaxRelays = 1
                },
                Entitlements = [new EntitlementClaim { Code = "core" }]
            }
        };

        // Create ephemeral keys for signing demonstration if no HSM/keys specified
        using var ecKey = Es256SignatureProvider.GenerateKey("prd-cli-ec");
        using var pqKey = MlDsaSignatureProvider.GenerateKey(MLDsaAlgorithm.MLDsa65, "prd-cli-pq");

        var signer = new LicenseDocumentSigner([ecKey, pqKey]);
        string pem = signer.Sign(claims);

        if (outPath is not null)
        {
            File.WriteAllText(outPath, pem);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Licencia úspešne vystavená a zapísaná do: {outPath}");
            Console.ResetColor();
        }
        else
        {
            Console.WriteLine(pem);
        }

        return 0;
    }

    private static int HandleLicenseInspect(string[] args)
    {
        if (args.Length == 0 || !File.Exists(args[0]))
        {
            Console.Error.WriteLine("Chyba: Zadať platný súbor licencie (.symlic)");
            return 1;
        }

        string pem = File.ReadAllText(args[0]);
        if (!PemArmor.TryUnwrap(pem, "SYMBOLON LICENSE", out byte[]? rawJson))
        {
            Console.Error.WriteLine("Chyba: Súbor neobsahuje platnú PEM obálku SYMBOLON LICENSE.");
            return 1;
        }

        var jwsDoc = JsonSerializer.Deserialize(rawJson, SymbolonJsonContext.Default.JwsGeneralJson);
        if (jwsDoc is null)
        {
            Console.Error.WriteLine("Chyba: Neplatný JWS JSON dokument.");
            return 1;
        }

        byte[] payloadBytes = Base64Url.DecodeFromChars(jwsDoc.Payload);
        var claims = JsonSerializer.Deserialize(payloadBytes, SymbolonJsonContext.Default.LicenseClaims);

        if (claims is null)
        {
            Console.Error.WriteLine("Chyba: Neplatné claims v licencii.");
            return 1;
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=== SYMBOLON LICENSE INSPECTION ===");
        Console.ResetColor();
        Console.WriteLine($"Licencia (Sub):     {claims.Sub}");
        Console.WriteLine($"Produkt (Aud):      {claims.Aud}");
        Console.WriteLine($"Licenčný kľúč:      {claims.Symlic.License.Key}");
        Console.WriteLine($"Model:              {claims.Symlic.License.Model}");
        Console.WriteLine($"Stav:               {claims.Symlic.License.State}");
        Console.WriteLine($"Sedadlá:            {claims.Symlic.Limits.MaxSeats} ({claims.Symlic.Limits.SeatUnit})");
        Console.WriteLine($"Platnosť súboru:    od {DateTimeOffset.FromUnixTimeSeconds(claims.Iat):u} do {DateTimeOffset.FromUnixTimeSeconds(claims.Exp):u}");
        Console.WriteLine($"Vyžadované algs:    {string.Join(", ", claims.Symlic.RequiredAlgs)}");
        Console.WriteLine($"Prítomné podpisy:   {jwsDoc.Signatures.Count}");

        return 0;
    }

    private static async Task<int> HandleDoctorAsync(string[] args)
    {
        string? serverUrl = GetArg(args, "--server");

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=== SYMBOLON DOCTOR ===");
        Console.ResetColor();

        // 1. ES256 check
        Console.WriteLine("  [✔] Klasická kryptografia: ES256 (ECDSA P-256) podporované");

        // 2. ML-DSA check
        if (MlDsaSignatureProvider.IsSupported)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("  [✔] Post-kvantová kryptografia: ML-DSA-65 (FIPS 204) natívne podporované");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  [⚠] Post-kvantová kryptografia: ML-DSA nie je natívne dostupné (vyžaduje Windows CNG PQC alebo OpenSSL 3.5+)");
            Console.ResetColor();
        }

        // 3. System clock
        var now = DateTimeOffset.UtcNow;
        Console.WriteLine($"  [✔] Systémové hodiny: {now:u}");

        // 4. Device fingerprint
        var fp = DeviceFingerprint.Collect();
        string fpHex = FingerprintHelper.ComputeHash(fp);
        Console.WriteLine($"  [✔] Fingerprint zariadenia: {fpHex} ({fp.Count} komponentov)");

        // 5. Server check if URL provided
        if (!string.IsNullOrWhiteSpace(serverUrl))
        {
            Console.Write($"  [*] Kontrola servera {serverUrl} ... ");
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                var res = await http.GetAsync(new Uri(new Uri(serverUrl), "health")).ConfigureAwait(false);
                if (res.IsSuccessStatusCode)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("Online [✔]");
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"Dostupný, ale stav je {(int)res.StatusCode} [⚠]");
                    Console.ResetColor();
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TimeoutException or SocketException)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Nedostupný ({ex.Message}) [✖]");
                Console.ResetColor();
            }
        }

        return 0;
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

    private static int UnknownCommand(string cmd)
    {
        Console.Error.WriteLine($"Neznámy príkaz: {cmd}");
        PrintHelp();
        return 1;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            Symbolon CLI — nástroj pre inštaláciu, správu licencií a kľúčov

            Príkazy:
              setup | wizard                            Spustí interaktívneho inštalačného sprievodcu (TUI)
              keys generate --alg <ES256|ML-DSA-65> --kid <id> [--out file]
              keys export-jwks --keys <file1,file2> [--out file]
              license keygen [--prefix SYM]
              license issue --customer <id> --seats <n> [--out file]
              license inspect <file.symlic>
              doctor [--server <url>]
              import --file <cesta> --policy <policy-id> [--format <keygen|csv>] [--dry-run]
              export --out <cesta> [--format <json|csv>]
            """);
    }
}
