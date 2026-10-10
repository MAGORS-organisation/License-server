using System.Globalization;
using System.Security.Cryptography;
using Spectre.Console;
using Achilles.Crypto;
using Achilles.Format;
using Achilles.Protocol;

namespace Achilles.Cli.Wizard;

public sealed record LicenseWizardOptions
{
    public required string OutputPath { get; init; }
    public string CustomerName { get; init; } = "CUST-0001";
    public string ProductCode { get; init; } = "cad-pro";
    public int Seats { get; init; } = 10;
    public string Model { get; init; } = "floating"; // floating | perpetual | subscription | node-locked
    public int ValidityDays { get; init; } = 365; // 0 = perpetual
}

public sealed record LicenseWizardResult
{
    public required string OutputPath { get; init; }
    public required string LicenseKey { get; init; }
    public required string CustomerName { get; init; }
    public required string ProductCode { get; init; }
    public required int Seats { get; init; }
    public required string Model { get; init; }
    public required string PemContent { get; init; }
}

public static class LicenseWizard
{
    public static LicenseWizardResult Issue(LicenseWizardOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var key = LicenseKey.Generate("SYM");
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        long? exp = options.ValidityDays > 0
            ? now + (options.ValidityDays * 24L * 3600)
            : null;

        string? expIso = exp.HasValue
            ? DateTimeOffset.FromUnixTimeSeconds(exp.Value).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
            : null;

        var claims = new LicenseClaims
        {
            Iss = "https://licenses.symbolon.local",
            Sub = $"lic_{Guid.NewGuid():N}",
            Aud = options.ProductCode,
            Jti = $"lf_{Guid.NewGuid():N}",
            Iat = now,
            Nbf = now,
            Exp = exp ?? (now + (100L * 365 * 24 * 3600)), // 100 years if perpetual snapshot
            Symlic = new SymlicClaims
            {
                V = 1,
                Profile = "hybrid-v1",
                RequiredAlgs = [Alg.Es256, Alg.MlDsa65],
                License = new LicenseMetadata
                {
                    Key = key.Canonical,
                    Model = options.Model,
                    State = "active",
                    ExpiresAt = expIso,
                    Customer = new CustomerMetadata
                    {
                        Ref = options.CustomerName.ToUpperInvariant().Replace(" ", "-", StringComparison.Ordinal),
                        Name = options.CustomerName
                    }
                },
                Limits = new LicenseLimits
                {
                    MaxSeats = options.Seats,
                    SeatUnit = "machine",
                    MaxRelays = 2
                },
                Entitlements = [new EntitlementClaim { Code = "core" }]
            }
        };

        using var ecKey = Es256SignatureProvider.GenerateKey("sym-lic-ec");
        using var pqKey = MlDsaSignatureProvider.GenerateKey(MLDsaAlgorithm.MLDsa65, "sym-lic-pq");

        var signer = new LicenseDocumentSigner([ecKey, pqKey]);
        string pem = signer.Sign(claims);

        string? dir = Path.GetDirectoryName(options.OutputPath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(options.OutputPath, pem);

        return new LicenseWizardResult
        {
            OutputPath = Path.GetFullPath(options.OutputPath),
            LicenseKey = key.Canonical,
            CustomerName = options.CustomerName,
            ProductCode = options.ProductCode,
            Seats = options.Seats,
            Model = options.Model,
            PemContent = pem
        };
    }

    public static async Task<bool> RunInteractiveAsync(IAnsiConsole console)
    {
        ArgumentNullException.ThrowIfNull(console);

        console.Write(new Rule("[bold cyan]Rýchly Sprievodca Vystavením Licencie[/]").LeftJustified());
        console.WriteLine();

        string customer = console.Prompt(
            new TextPrompt<string>("Názov zákazníka / spoločnosti:")
                .DefaultValue("Acme Systems s.r.o."));

        string product = console.Prompt(
            new TextPrompt<string>("Kód produktu (Product ID):")
                .DefaultValue("cad-pro"));

        int seats = console.Prompt(
            new TextPrompt<int>("Počet plávajúcich sedadiel (Seats):")
                .DefaultValue(10)
                .Validate(s => s >= 1 ? ValidationResult.Success() : ValidationResult.Error("Počet sedadiel musí byť aspoň 1")));

        string model = console.Prompt(
            new SelectionPrompt<string>()
                .Title("Licenčný model:")
                .AddChoices("floating", "perpetual", "subscription", "node-locked"));

        int days = console.Prompt(
            new TextPrompt<int>("Platnosť licencie v dňoch (0 = trvalá/perpetual):")
                .DefaultValue(365)
                .Validate(d => d >= 0 ? ValidationResult.Success() : ValidationResult.Error("Platnosť nemôže byť záporná")));

        string outFile = console.Prompt(
            new TextPrompt<string>("Cesta pre uloženie .symlic súboru:")
                .DefaultValue($"./license-{product}.symlic"));

        LicenseWizardResult result = null!;
        await console.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Generujem Crockford Base32 kľúč s CRC-32C a podpisujem JWS súbor...", _ =>
            {
                result = Issue(new LicenseWizardOptions
                {
                    OutputPath = outFile,
                    CustomerName = customer,
                    ProductCode = product,
                    Seats = seats,
                    Model = model,
                    ValidityDays = days
                });
                return Task.CompletedTask;
            });

        console.WriteLine();
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("[bold]Parameter[/]");
        table.AddColumn("[bold]Hodnota[/]");
        table.AddRow("Licenčný kľúč", $"[bold green]{result.LicenseKey}[/]");
        table.AddRow("Zákazník", $"[white]{result.CustomerName}[/]");
        table.AddRow("Produkt", $"[white]{result.ProductCode}[/]");
        table.AddRow("Počet sedadiel", $"[cyan]{result.Seats}[/]");
        table.AddRow("Model", $"[cyan]{result.Model.ToUpperInvariant()}[/]");
        table.AddRow("Uložený súbor", $"[yellow]{result.OutputPath}[/]");
        table.AddRow("Podpisy", "[green]Hybridné (ES256 + ML-DSA-65 PQC)[/]");

        console.Write(new Panel(table)
            .Header("[bold green]✔ Licencia úspešne vystavená[/]")
            .Padding(1, 1));

        console.MarkupLine($"Licenčný súbor bol zapísaný do: [yellow]{result.OutputPath}[/]");
        console.MarkupLine("Zákazník ho môže vložiť do svojej aplikácie alebo aktivovať pomocou klientskeho SDK.");
        console.WriteLine();

        return true;
    }
}
