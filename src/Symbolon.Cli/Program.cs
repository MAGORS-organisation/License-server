using System.Buffers.Text;
using System.Globalization;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Symbolon.Client;
using Symbolon.Crypto;
using Symbolon.Crypto.SecretSharing;
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
                "KEYS" => await HandleKeysAsync(args[1..]).ConfigureAwait(false),
                "LICENSE" => await HandleLicenseAsync(args[1..]).ConfigureAwait(false),
                "DOCTOR" => await HandleDoctorAsync(args[1..]).ConfigureAwait(false),
                "IMPORT" => await Commands.MigrationCommands.HandleImportAsync(args[1..]).ConfigureAwait(false),
                "EXPORT" => await Commands.MigrationCommands.HandleExportAsync(args[1..]).ConfigureAwait(false),
                "SBOM" => await Commands.ComplianceCommands.HandleSbomAsync(args[1..]).ConfigureAwait(false),
                "VERIFY-ARTIFACT" or "VERIFY" => await Commands.ComplianceCommands.HandleVerifyArtifactAsync(args[1..]).ConfigureAwait(false),
                "OCI" => await Commands.OciCommands.HandleOciAsync(args[1..]).ConfigureAwait(false),
                "MESH" => await Commands.MeshCommands.HandleMeshAsync(args[1..]).ConfigureAwait(false),
                "CLUSTER" => await Commands.ClusterCommands.HandleClusterAsync(args[1..]).ConfigureAwait(false),
                "EBPF" => await Commands.EbpfCommands.HandleEbpfAsync(args[1..]).ConfigureAwait(false),
                "ATTESTATION" => await Commands.AttestationCommands.HandleAttestationAsync(args[1..]).ConfigureAwait(false),
                "K8S" or "OPERATOR" => await Commands.K8sCommands.HandleK8sAsync(args[1..]).ConfigureAwait(false),
                "KMS" => await HandleKmsAsync(args[1..]).ConfigureAwait(false),
                "TOKENS" or "CREDITS" => await Commands.TokenCommands.HandleTokensAsync(args[1..]).ConfigureAwait(false),
                "FEATURES" or "ENTITLEMENTS" => await Commands.FeatureCommands.HandleFeaturesAsync(args[1..]).ConfigureAwait(false),
                "MIGRATE" => await Commands.MigrateCommands.HandleMigrateAsync(args[1..]).ConfigureAwait(false),
                "WEBHOOKS" or "WEBHOOK" => await Commands.WebhookCommands.HandleWebhooksAsync(args[1..]).ConfigureAwait(false),
                "DISCOVER" or "SERVERS" => await Commands.DiscoverCommands.HandleDiscoverAsync(args[1..]).ConfigureAwait(false),
                "REVOCATIONS" or "REVOCATION" or "CRL" => await Commands.RevocationCommands.HandleRevocationsAsync(args[1..]).ConfigureAwait(false),
                "REPORTS" or "REPORT" => await Commands.ReportCommands.HandleReportsAsync(args[1..]).ConfigureAwait(false),
                "QUEUE" => await Commands.QueueCommands.HandleQueueAsync(args[1..]).ConfigureAwait(false),
                "POLICY" or "OPTIONS" or "RULES" => await Commands.PolicyCommands.HandlePolicyAsync(args[1..]).ConfigureAwait(false),
                "MACHINE" or "FINGERPRINT" or "ACTIVATION" or "ACTIVATIONS" => await Commands.MachineCommands.HandleMachineAsync(args[1..]).ConfigureAwait(false),
                "GRANT" or "GRANTS" or "SEATGRANT" or "AIRGAP" => await Commands.GrantCommands.HandleGrantAsync(args[1..]).ConfigureAwait(false),
                "EXPERIMENTS" or "EXPERIMENT" or "ABTEST" or "AB" => await Commands.ExperimentCommands.HandleExperimentAsync(args[1..]).ConfigureAwait(false),
                "PQC" or "POSTQUANTUM" or "QUANTUM" => await Commands.PqcCommands.HandlePqcAsync(args[1..]).ConfigureAwait(false),
                "TRANSPARENCY" or "MERKLE" => await Commands.TransparencyCommands.HandleTransparencyAsync(args[1..]).ConfigureAwait(false),
                "BILLING" or "STRIPE" or "PADDLE" or "LEMON" => await Commands.BillingCommands.HandleBillingAsync(args[1..]).ConfigureAwait(false),
                "AUDIT" => await Commands.AuditCommands.HandleAuditAsync(args[1..]).ConfigureAwait(false),
                "TOP" or "MONITOR" => await Commands.TopCommand.HandleTopAsync(args[1..]).ConfigureAwait(false),
                "AGENT" or "TRAY" or "DAEMON" => await Commands.AgentCommands.HandleAgentAsync(args[1..]).ConfigureAwait(false),
                "DESKTOP" or "STUDIO" => await Commands.DesktopCommands.HandleDesktopAsync(args[1..]).ConfigureAwait(false),
                "TENANT" or "BRANDING" => await Commands.TenantCommands.HandleTenantAsync(args[1..]).ConfigureAwait(false),
                "CHAOS" or "RESILIENCE" => await Commands.ChaosCommands.HandleChaosAsync(args[1..]).ConfigureAwait(false),
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

    private static Task<int> HandleKmsAsync(string[] args) => Commands.KmsCommands.HandleKmsAsync(args);

    private static async Task<int> HandleKeysAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            Console.WriteLine("Použitie: symbolon keys <generate|export-jwks|split|combine|envelope|hierarchy|hsm> [options]");
            return 0;
        }

        return args[0].ToUpperInvariant() switch
        {
            "GENERATE" => HandleKeysGenerate(args[1..]),
            "EXPORT-JWKS" => HandleKeysExportJwks(args[1..]),
            "SPLIT" => HandleKeysSplit(args[1..]),
            "COMBINE" => HandleKeysCombine(args[1..]),
            "ENVELOPE" => HandleKeysEnvelope(args[1..]),
            "HIERARCHY" => HandleKeysHierarchy(args[1..]),
            "HSM" or "PKCS11" => await Commands.KmsCommands.HandleStatusAsync(args[1..]).ConfigureAwait(false),
            _ => UnknownCommand(args[0])
        };
    }

    private static int HandleKeysEnvelope(string[] args) => Commands.KmsCommands.HandleEnvelopeEncrypt(args);

    private static int HandleKeysHierarchy(string[] args) => Commands.KmsCommands.HandleHierarchyVerify();

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

    private static int HandleKeysSplit(string[] args)
    {
        string? inPath = GetArg(args, "--in");
        string? thresholdStr = GetArg(args, "--threshold") ?? GetArg(args, "-k") ?? "3";
        string? sharesStr = GetArg(args, "--shares") ?? GetArg(args, "-n") ?? "5";
        string? outDir = GetArg(args, "--out-dir");
        string? format = GetArg(args, "--format") ?? "token";

        if (string.IsNullOrWhiteSpace(inPath) || !File.Exists(inPath))
        {
            Console.Error.WriteLine("Chyba: Parameter --in <subor_kluca> je povinný a súbor musí existovať.");
            return 1;
        }

        if (!byte.TryParse(thresholdStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte k) || k < 2)
        {
            Console.Error.WriteLine("Chyba: Prah (-k / --threshold) musí byť celé číslo >= 2.");
            return 1;
        }

        if (!byte.TryParse(sharesStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte n) || n < k)
        {
            Console.Error.WriteLine($"Chyba: Počet podielov (-n / --shares) musí byť celé číslo >= {k} a <= 255.");
            return 1;
        }

        byte[] secretBytes = File.ReadAllBytes(inPath);
        var shares = ShamirSecretSharing.Split(secretBytes, k, n);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[OK] Kľúč '{Path.GetFileName(inPath)}' úspešne rozdelený na {n} podielov s prahom k={k} (Shamir's Secret Sharing).");
        Console.ResetColor();

        bool isPem = string.Equals(format, "pem", StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(outDir))
        {
            Directory.CreateDirectory(outDir);
            for (int i = 0; i < shares.Length; i++)
            {
                string shareFileName = Path.Combine(outDir, $"share_{shares[i].Index}.{(isPem ? "pem" : "share")}");
                string content = isPem ? shares[i].ToPem() : shares[i].ToToken();
                File.WriteAllText(shareFileName, content);
                Console.WriteLine($"  -> Zapísaný podiel {shares[i].Index}: {shareFileName}");
            }
        }
        else
        {
            Console.WriteLine("Vygenerované podiely tajomstva:");
            for (int i = 0; i < shares.Length; i++)
            {
                Console.WriteLine($"--- Podiel #{shares[i].Index} ---");
                Console.WriteLine(isPem ? shares[i].ToPem() : shares[i].ToToken());
            }
        }

        return 0;
    }

    private static int HandleKeysCombine(string[] args)
    {
        string? sharesParam = GetArg(args, "--shares");
        string? inDir = GetArg(args, "--in-dir");
        string? outPath = GetArg(args, "--out");

        var collectedTokens = new List<string>();

        if (!string.IsNullOrWhiteSpace(sharesParam))
        {
            var parts = sharesParam.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var part in parts)
            {
                if (File.Exists(part))
                {
                    collectedTokens.Add(File.ReadAllText(part).Trim());
                }
                else
                {
                    collectedTokens.Add(part);
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(inDir) && Directory.Exists(inDir))
        {
            foreach (var file in Directory.GetFiles(inDir, "*.*"))
            {
                if (file.EndsWith(".share", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".pem", StringComparison.OrdinalIgnoreCase))
                {
                    collectedTokens.Add(File.ReadAllText(file).Trim());
                }
            }
        }

        if (collectedTokens.Count == 0)
        {
            Console.Error.WriteLine("Chyba: Je potrebné zadať aspoň k podielov pomocou --shares <subory_alebo_tokeny> alebo --in-dir <adresar>.");
            return 1;
        }

        var shares = new List<SecretShare>();
        foreach (var tokenStr in collectedTokens)
        {
            if (SecretShare.TryParse(tokenStr, out var share) && share is not null)
            {
                shares.Add(share);
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Varovanie: Reťazec nemožno rozparsovať ako platný podiel tajomstva: {tokenStr[..Math.Min(20, tokenStr.Length)]}...");
                Console.ResetColor();
            }
        }

        byte[] recoveredSecret;
        try
        {
            recoveredSecret = ShamirSecretSharing.Combine(shares);
        }
        catch (Exception ex) when (ex is CryptographicException or InvalidOperationException or ArgumentException)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"[CHYBA] Rekonštrukcia zlyhala: {ex.Message}");
            Console.ResetColor();
            return 1;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[OK] Kľúč úspešne zrekonštruovaný z {shares.Count} platných podielov (Kontrolný súčet SHA-256 OVERENÝ).");
        Console.ResetColor();

        if (!string.IsNullOrWhiteSpace(outPath))
        {
            File.WriteAllBytes(outPath, recoveredSecret);
            Console.WriteLine($"  -> Zrekonštruovaný kľúč zapísaný do: {outPath}");
        }
        else
        {
            Console.WriteLine("Zrekonštruované dáta (UTF-8 / Hex):");
            try
            {
                Console.WriteLine(Encoding.UTF8.GetString(recoveredSecret));
            }
            catch (DecoderFallbackException)
            {
                Console.WriteLine(Convert.ToHexString(recoveredSecret));
            }
        }

        return 0;
    }

    private static async Task<int> HandleLicenseAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            Console.WriteLine("Použitie: symbolon license <keygen|issue|inspect|borrow|return|inspect-borrow> [options]");
            return 0;
        }

        return args[0].ToUpperInvariant() switch
        {
            "KEYGEN" => HandleLicenseKeygen(args[1..]),
            "ISSUE" => HandleLicenseIssue(args[1..]),
            "INSPECT" => HandleLicenseInspect(args[1..]),
            "BORROW" => await HandleLicenseBorrowAsync(args[1..]).ConfigureAwait(false),
            "RETURN" => await HandleLicenseReturnAsync(args[1..]).ConfigureAwait(false),
            "INSPECT-BORROW" => HandleLicenseInspectBorrow(args[1..]),
            _ => UnknownCommand(args[0])
        };
    }

    private static async Task<int> HandleLicenseBorrowAsync(string[] args)
    {
        string? serverUrl = GetArg(args, "--server") ?? "http://localhost:5000";
        string? leaseId = GetArg(args, "--lease") ?? (args.Length > 0 && !args[0].StartsWith('-') ? args[0] : null);
        string? daysStr = GetArg(args, "--days") ?? "7";
        string? outPath = GetArg(args, "--out");
        string? outKeyPath = GetArg(args, "--out-key");

        if (string.IsNullOrWhiteSpace(leaseId))
        {
            Console.Error.WriteLine("Chýba parameter <lease-id> alebo --lease <lease-id>.");
            return 1;
        }

        if (!int.TryParse(daysStr, CultureInfo.InvariantCulture, out int days) || days < 1 || days > 30)
        {
            Console.Error.WriteLine("Parameter --days musí byť v rozsahu 1 až 30.");
            return 1;
        }

        using var http = new HttpClient { BaseAddress = new Uri(serverUrl) };
        var dto = new BorrowRequestDto(days);
        var response = await http.PostAsJsonAsync(
            new Uri($"v1/leases/{leaseId}/borrow", UriKind.Relative),
            dto,
            SymbolonProtocolJsonContext.Default.BorrowRequestDto).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"Zlyhanie zapožičania sedadla: HTTP {(int)response.StatusCode}");
            Console.ResetColor();
            return 1;
        }

        var result = await response.Content.ReadFromJsonAsync(
            SymbolonProtocolJsonContext.Default.BorrowResponseDto).ConfigureAwait(false);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[OK] Sedadlo pre lease {leaseId} úspešne zapožičané na {days} dní (FLT-17..FLT-20).");
        Console.ResetColor();
        if (result is not null)
        {
            Console.WriteLine($"  Borrowed Until: {result.BorrowedUntil:yyyy-MM-dd HH:mm:ss 'UTC'}");
            Console.WriteLine($"  Offline Token:  {result.Token[..Math.Min(32, result.Token.Length)]}...");

            if (!string.IsNullOrWhiteSpace(result.Symlease))
            {
                string symleaseFile = outPath ?? $"{leaseId}.symlease";
                File.WriteAllText(symleaseFile, result.Symlease);
                Console.WriteLine($"  Symlease File:  {symleaseFile}");
            }

            if (!string.IsNullOrWhiteSpace(result.PossessionKey))
            {
                string keyFile = outKeyPath ?? $"{leaseId}.key";
                File.WriteAllText(keyFile, result.PossessionKey);
                Console.WriteLine($"  Possession Key: {keyFile}");
            }
        }

        return 0;
    }

    private static async Task<int> HandleLicenseReturnAsync(string[] args)
    {
        string? serverUrl = GetArg(args, "--server") ?? "http://localhost:5000";
        string? leaseId = GetArg(args, "--lease") ?? (args.Length > 0 && !args[0].StartsWith('-') ? args[0] : null);
        string? symleasePath = GetArg(args, "--symlease");
        string? keyPath = GetArg(args, "--key");

        if (string.IsNullOrWhiteSpace(leaseId))
        {
            Console.Error.WriteLine("Chýba parameter <lease-id> alebo --lease <lease-id>.");
            return 1;
        }

        using var http = new HttpClient { BaseAddress = new Uri(serverUrl) };

        if (!string.IsNullOrWhiteSpace(symleasePath) && !string.IsNullOrWhiteSpace(keyPath))
        {
            if (!File.Exists(symleasePath))
            {
                Console.Error.WriteLine($"Chyba: Súbor symlease '{symleasePath}' neexistuje.");
                return 1;
            }

            if (!File.Exists(keyPath))
            {
                Console.Error.WriteLine($"Chyba: Súbor kľúča '{keyPath}' neexistuje.");
                return 1;
            }

            string symleasePem = File.ReadAllText(symleasePath).Trim();
            string keyContent = File.ReadAllText(keyPath).Trim();

            var challengeResponse = await http.PostAsync(
                new Uri($"v1/leases/{leaseId}/return-challenge", UriKind.Relative),
                null).ConfigureAwait(false);

            if (!challengeResponse.IsSuccessStatusCode)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine($"Zlyhanie získania výzvy na vrátenie: HTTP {(int)challengeResponse.StatusCode}");
                Console.ResetColor();
                return 1;
            }

            var challenge = await challengeResponse.Content.ReadFromJsonAsync(
                SymbolonProtocolJsonContext.Default.ReturnChallengeResponseDto).ConfigureAwait(false);

            if (challenge is null)
            {
                Console.Error.WriteLine("Chyba: Prázdna odpoveď výzvy zo servera.");
                return 1;
            }

            string signature = ProofOfPossessionEngine.SignChallenge(challenge.Nonce, keyContent);

            var returnDto = new EarlyReturnRequestDto
            {
                Symlease = symleasePem,
                Nonce = challenge.Nonce,
                Signature = signature
            };

            var returnResponse = await http.PostAsJsonAsync(
                new Uri($"v1/leases/{leaseId}/return", UriKind.Relative),
                returnDto,
                SymbolonProtocolJsonContext.Default.EarlyReturnRequestDto).ConfigureAwait(false);

            if (!returnResponse.IsSuccessStatusCode)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine($"Zlyhanie predčasného vrátenia sedadla: HTTP {(int)returnResponse.StatusCode}");
                Console.ResetColor();
                return 1;
            }

            var returnResult = await returnResponse.Content.ReadFromJsonAsync(
                SymbolonProtocolJsonContext.Default.EarlyReturnResponseDto).ConfigureAwait(false);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[OK] Zapožičané sedadlo {leaseId} bolo úspešne predčasne vrátené pomocou kryptografického dôkazu držby (FLT-21).");
            Console.ResetColor();
            if (returnResult is not null)
            {
                Console.WriteLine($"  Returned At: {returnResult.ReturnedAt:yyyy-MM-dd HH:mm:ss 'UTC'}");
            }
            return 0;
        }

        // Plain DELETE attempt without proof-of-possession
        var plainResponse = await http.DeleteAsync(new Uri($"v1/leases/{leaseId}", UriKind.Relative)).ConfigureAwait(false);

        if (plainResponse.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"[403 Forbidden] Sedadlo {leaseId} je zapožičané na offline roaming.");
            Console.Error.WriteLine("Pre predčasné vrátenie sa vyžaduje kryptografický dôkaz držby: zadajte --symlease <subor> a --key <subor> (FLT-22).");
            Console.ResetColor();
            return 1;
        }

        if (!plainResponse.IsSuccessStatusCode)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"Zlyhanie vrátenia sedadla: HTTP {(int)plainResponse.StatusCode}");
            Console.ResetColor();
            return 1;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[OK] Sedadlo {leaseId} bolo úspešne uvoľnené.");
        Console.ResetColor();
        return 0;
    }

    private static int HandleLicenseInspectBorrow(string[] args)
    {
        string? filePath = args.Length > 0 && !args[0].StartsWith('-') ? args[0] : GetArg(args, "--file");
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            Console.Error.WriteLine("Chyba: Zadať platný súbor offline roamingu (.symlease)");
            return 1;
        }

        string pem = File.ReadAllText(filePath);
        if (!PemArmor.TryUnwrap(pem, "SYMBOLON LEASE", out byte[]? rawJson))
        {
            Console.Error.WriteLine("Chyba: Súbor neobsahuje platnú PEM obálku SYMBOLON LEASE.");
            return 1;
        }

        var jwsDoc = JsonSerializer.Deserialize(rawJson, SymbolonJsonContext.Default.JwsGeneralJson);
        if (jwsDoc is null)
        {
            Console.Error.WriteLine("Chyba: Neplatný JWS JSON dokument.");
            return 1;
        }

        byte[] payloadBytes = Base64Url.DecodeFromChars(jwsDoc.Payload);
        var claims = JsonSerializer.Deserialize(payloadBytes, SymbolonJsonContext.Default.SymleaseClaims);

        if (claims is null)
        {
            Console.Error.WriteLine("Chyba: Neplatné claims v symlease.");
            return 1;
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=== SYMBOLON OFFLINE ROAMING LEASE INSPECTION ===");
        Console.ResetColor();
        Console.WriteLine($"Lease ID (Jti):       {claims.Jti}");
        Console.WriteLine($"Licencia (Sub):       {claims.Sub}");
        Console.WriteLine($"Vydavateľ (Iss):      {claims.Iss}");
        Console.WriteLine($"Hardvérový odtlačok:  {claims.Fp}");
        Console.WriteLine($"Číslo sedadla:        #{claims.Seat}");
        Console.WriteLine($"Entitlements:         {string.Join(", ", claims.Ent)}");
        Console.WriteLine($"Doba zapožičania:     {claims.Borrow.Days} dní");
        Console.WriteLine($"Zapožičané dňa:       {DateTimeOffset.FromUnixTimeSeconds(claims.Borrow.BorrowedAt):u}");
        Console.WriteLine($"Platné do (Exp):      {DateTimeOffset.FromUnixTimeSeconds(claims.Exp):u}");
        Console.WriteLine($"Possession Key JWK:   {claims.Borrow.PossessionKeyJwk[..Math.Min(32, claims.Borrow.PossessionKeyJwk.Length)]}...");
        Console.WriteLine($"Prítomné podpisy:     {jwsDoc.Signatures.Count}");

        for (int i = 0; i < jwsDoc.Signatures.Count; i++)
        {
            var sig = jwsDoc.Signatures[i];
            string kid = "unknown";
            string alg = "unknown";
            try
            {
                byte[] hdrBytes = Base64Url.DecodeFromChars(sig.Protected);
                var hdr = JsonSerializer.Deserialize(hdrBytes, SymbolonJsonContext.Default.JwsProtectedHeader);
                if (hdr is not null)
                {
                    kid = hdr.Kid;
                    alg = hdr.Alg;
                }
            }
            catch (Exception)
            {
                // ignore parsing failure for display
            }
            Console.WriteLine($"  Podpis #{i + 1}: kid='{kid}', alg='{alg}'");
        }

        return 0;
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
        string? jwksOut = GetArg(args, "--jwks-out");
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

        if (jwksOut is not null)
        {
            var jwks = new JsonWebKeySetDto
            {
                Keys = [ecKey.ExportPublicJwk(), pqKey.ExportPublicJwk()]
            };
            File.WriteAllText(jwksOut, JsonSerializer.Serialize(jwks, SymbolonJsonContext.Default.JsonWebKeySetDto));
        }

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

    private static Task<int> HandleDoctorAsync(string[] args) => Commands.DoctorCommands.HandleDoctorAsync(args);

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
              keys split --in <key-file> -k <prax> -n <podielov> [--out-dir <dir>]
              keys combine --shares <s1,s2,...> [--out <file>]
              keys envelope [--in <key>] [--passphrase <p>] [--out <file>] Zašifrovanie privátneho kľúča AES-256-GCM obálkou
              keys hierarchy                            Overenie 3-úrovňovej hierarchie kľúčov (Root ➜ Product ➜ Lease)
              license keygen [--prefix SYM]
              license issue --customer <id> --seats <n> [--out file]
              license inspect <file.symlic>
              license borrow <leaseId> --days <n> [--out <f.symlease>] [--out-key <f.key>] [--server <url>]
              license return <leaseId> --symlease <f.symlease> --key <f.key> [--server <url>]
              license inspect-borrow <f.symlease>
              doctor [--server <url>] [--relay <url>] [--pqc] [--strict] [--json]
              import --file <cesta> --policy <policy-id> [--format <keygen|csv>] [--dry-run]
              export --out <cesta> [--format <json|csv>]
              sbom [--out <cesta>]                      Vygeneruje CycloneDX v1.6 SBOM v JSON formáte
              verify-artifact <cesta> [--checksum <sha256>]  Vypočíta a overí integritu súboru (SHA-256/512)
              oci pack|verify [options]                 Vytvorenie a verifikácia OCI Cryptographic License Bundles
              mesh status [--node <id>]                 Zobrazenie konsenzu a topológie Relay Mesh klastra
              cluster status|sync [options]             Multi-regiónová geo-replikácia, vektorové hodiny a CRDT status
              ebpf status|attach|violations [options]   eBPF kernel socket enforcement a BPF map monitoring
              attestation verify [options]              Verifikácia hardvérovej TPM 2.0 / Enclave citácie
              k8s crd|export-license|generate-cluster   Kubernetes Operator CRD a GitOps licenčné manifesty
              kms status [--provider <env|azure|aws|pkcs11>] Stav Cloud KMS / HSM a správa hardvérových kľúčov
              tokens wallets|create|credit|rates|balance   Kreditové peňaženky a metered pay-as-you-go licencie
              features list|create|suites|grant|usage   Granulárne moduly, balíčky a živé merače konkurencie
              migrate flexnet|options|log|keygen        Migračný nástroj a transpiler z FlexNet a Keygen.sh
              webhooks list|create|delete|test|deliveries Outbound webhooky, Slack/Teams alerty a DLQ replay
              discover | servers [resolve]              Zero-Config detekcia serverov cez UDP broadcast a riešenie SYMBOLON_LICENSE_SERVER
              revocations fetch|inspect|revoke|list     Kryptografické revokačné zoznamy (.symrl), delta sync a CRL správa
              reports concurrency|true-up|denials|verify-audit Enterprise audit výkazy, peak concurrency (FLT-38), true-up a overenie integrity
              queue list|status|cancel|promote          Prioritný licenčný rad (FLT-31), monitoring ticketov a auto-wait
              policy rules get|set|test                 Options file pravidlá (FLT-23, FLT-24, FLT-25), rezervácie, zákazy a kvóty
              machine fingerprint|activate|deactivate|test-match|list Hardvérový fingerprint, fuzzy node-lock matching a aktivácie
              grant request|issue|inspect|import       Delegované air-gap sedadlá (.symgrant, .symreq, GNT-1..10)
              experiments list|create|start|report|sim A/B testovanie a experimentačný engine (AB-1..15, Z-test, p-value)
              pqc scan|profile|kem|verify              Post-Quantum Era suite, FIPS 203 ML-KEM, FIPS 204 ML-DSA, CNSA 2.0 readiness
              transparency root|sth|inclusion|consistency Merkle Transparency Log (RFC 6962, Sigstore Rekor, append-only proofs)
              billing webhook|test|verify-sig          Inbound billing gateway (Stripe, LemonSqueezy, Paddle webhook sync)
              audit verify-chain|export-proof|list      Kryptograficky tamper-proof audit hash chain a Merkle notárske dôkazy (§10.8)
              top [--server <url>] [--interval <sec>]   Živý Retro FoxPro / Novell NetWare TUI real-time monitor sedadiel a klastra
              agent start|status|acquire|release|borrow Klientsky desktop a CLI tray agent (IPC démon na 127.0.0.1:8189)
            """);
    }
}
