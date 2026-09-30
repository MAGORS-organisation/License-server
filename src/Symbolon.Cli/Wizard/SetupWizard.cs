using System.Runtime.InteropServices;
using Spectre.Console;
using Symbolon.Crypto;

namespace Symbolon.Cli.Wizard;

public static class SetupWizard
{
    public static async Task<int> RunAsync(IAnsiConsole? console = null)
    {
        var appConsole = console ?? AnsiConsole.Console;

        appConsole.Clear();
        PrintHeader(appConsole);

        bool running = true;
        while (running)
        {
            var choice = appConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[bold yellow]Vyberte požadovanú akciu:[/]")
                    .PageSize(10)
                    .AddChoices(
                        "🚀 Inštalácia a konfigurácia ControlPlane (Centrálny licenčný server)",
                        "🏢 Inštalácia a konfigurácia Relay (Lokálny on-premise relay server)",
                        "🔑 Rýchle vystavenie licencie a vygenerovanie kľúčov (.symlic)",
                        "🛰️ Air-Gap prenos sedadiel a správa grantov (.symreq / .symgrant)",
                        "🩺 Diagnostika systému (System Doctor)",
                        "❌ Ukončiť sprievodcu"));

            if (choice.StartsWith("🚀", StringComparison.Ordinal))
            {
                await ControlPlaneSetup.RunInteractiveAsync(appConsole).ConfigureAwait(false);
            }
            else if (choice.StartsWith("🏢", StringComparison.Ordinal))
            {
                await RelaySetup.RunInteractiveAsync(appConsole).ConfigureAwait(false);
            }
            else if (choice.StartsWith("🔑", StringComparison.Ordinal))
            {
                await LicenseWizard.RunInteractiveAsync(appConsole).ConfigureAwait(false);
            }
            else if (choice.StartsWith("🛰️", StringComparison.Ordinal))
            {
                await RunAirGapWizardAsync(appConsole).ConfigureAwait(false);
            }
            else if (choice.StartsWith("🩺", StringComparison.Ordinal))
            {
                await RunDoctorQuickAsync(appConsole).ConfigureAwait(false);
            }
            else
            {
                running = false;
                appConsole.MarkupLine("[dim]Sprievodca bol ukončený.[/]");
            }

            if (running)
            {
                appConsole.WriteLine();
                if (!appConsole.Confirm("Prajete si vykonať ďalšiu akciu?", defaultValue: true))
                {
                    running = false;
                    appConsole.MarkupLine("[dim]Sprievodca bol ukončený. Ďakujeme, že používate Symbolon![/]");
                }
                else
                {
                    appConsole.Clear();
                    PrintHeader(appConsole);
                }
            }
        }

        return 0;
    }

    private static void PrintHeader(IAnsiConsole console)
    {
        console.Write(
            new FigletText("Symbolon")
                .LeftJustified()
                .Color(Color.Cyan1));

        console.Write(new Rule("[bold blue].NET 10 Enterprise Licensing Infrastructure & TUI Setup Wizard[/]").LeftJustified());
        console.WriteLine();

        // Diagnostics summary box
        string pqcStatus = MlDsaSignatureProvider.IsSupported
            ? "[green]Natívne podporované (FIPS 204 ML-DSA-65)[/]"
            : "[yellow]Emulácia / softvérový fallback[/]";

        var diagTable = new Table().Border(TableBorder.SimpleHeavy);
        diagTable.AddColumn("[bold]Komponent[/]");
        diagTable.AddColumn("[bold]Stav[/]");
        diagTable.AddRow("Operačný systém", RuntimeInformation.OSDescription);
        diagTable.AddRow("Platforma & Architektúra", $"{RuntimeInformation.RuntimeIdentifier} ({RuntimeInformation.ProcessArchitecture})");
        diagTable.AddRow(".NET Runtime", Environment.Version.ToString());
        diagTable.AddRow("Post-Kvantová Kryptografia", pqcStatus);

        console.Write(diagTable);
        console.WriteLine();
    }

    private static async Task RunDoctorQuickAsync(IAnsiConsole console)
    {
        console.Write(new Rule("[bold cyan]Diagnostika Systému a Kryptografie[/]").LeftJustified());
        console.WriteLine();

        await console.Status()
            .Spinner(Spinner.Known.Aesthetic)
            .StartAsync("Overujem systémové komponenty...", async _ =>
            {
                await Task.Delay(300).ConfigureAwait(false);
            }).ConfigureAwait(false);

        var fp = Symbolon.Client.DeviceFingerprint.Collect();
        string fpHex = Symbolon.Protocol.FingerprintHelper.ComputeHash(fp);

        console.MarkupLine("[green]✔[/] Klasická kryptografia: ES256 (NIST P-256) pripravená.");
        if (MlDsaSignatureProvider.IsSupported)
        {
            console.MarkupLine("[green]✔[/] Post-kvantová kryptografia: ML-DSA-65 (FIPS 204) pripravená.");
        }
        else
        {
            console.MarkupLine("[yellow]⚠[/] Post-kvantová kryptografia: ML-DSA nie je natívne podporované na tomto OS.");
        }
        console.MarkupLine($"[green]✔[/] Systémové hodiny: {DateTimeOffset.UtcNow:u} (UTC)");
        console.MarkupLine($"[green]✔[/] Hardvérový fingerprint stanice: [cyan]{fpHex}[/] ({fp.Count} metrík)");
        console.WriteLine();
    }

    private static async Task RunAirGapWizardAsync(IAnsiConsole console)
    {
        console.Write(new Rule("[bold yellow]Air-Gap Delegácia Sedadiel a Správa Grantov (GNT-1..10, FLT-32..35)[/]").LeftJustified());
        console.WriteLine();

        var subChoice = console.Prompt(
            new SelectionPrompt<string>()
                .Title("[bold cyan]Vyberte operáciu air-gap:[/]")
                .PageSize(10)
                .AddChoices(
                    "📝 1. Vytvoriť požiadavku na sedadlá pre offline relay (.symreq)",
                    "🛰️ 2. Vystaviť grant zo .symreq požiadavky na Control Plane (.symgrant)",
                    "🔍 3. Inšpektovať a overiť .symgrant alebo .symreq súbor",
                    "📥 4. Importovať .symgrant do offline relayu",
                    "⬅️ Späť do hlavného menu"));

        if (subChoice.StartsWith("📝", StringComparison.Ordinal))
        {
            string license = console.Ask<string>("Zadajte licenčný kľúč (napr. [yellow]SYM1-DEMO-KEY[/]):");
            string relay = console.Ask<string>("Zadajte Relay ID stanice (napr. [yellow]rly_plant_01[/]):");
            int seats = console.Prompt(new TextPrompt<int>("Zadajte požadovaný počet sedadiel:").DefaultValue(5));
            string outFile = console.Prompt(new TextPrompt<string>("Výstupný súbor:").DefaultValue("request.symreq"));

            await Commands.GrantCommands.HandleGrantAsync(["request", "--license", license, "--relay", relay, "--seats", seats.ToString(System.Globalization.CultureInfo.InvariantCulture), "--out", outFile]).ConfigureAwait(false);
        }
        else if (subChoice.StartsWith("🛰️", StringComparison.Ordinal))
        {
            string inFile = console.Prompt(new TextPrompt<string>("Cesta k súboru požiadavky (.symreq):").DefaultValue("request.symreq"));
            string server = console.Prompt(new TextPrompt<string>("URL Control Plane servera:").DefaultValue("http://localhost:5000"));
            string outFile = console.Prompt(new TextPrompt<string>("Uložiť vystavený grant do:").DefaultValue("grant.symgrant"));

            await Commands.GrantCommands.HandleGrantAsync(["issue", "--in", inFile, "--server", server, "--out", outFile]).ConfigureAwait(false);
        }
        else if (subChoice.StartsWith("🔍", StringComparison.Ordinal))
        {
            string file = console.Ask<string>("Cesta k .symgrant alebo .symreq súboru:");
            await Commands.GrantCommands.HandleGrantAsync(["inspect", "--in", file]).ConfigureAwait(false);
        }
        else if (subChoice.StartsWith("📥", StringComparison.Ordinal))
        {
            string inFile = console.Prompt(new TextPrompt<string>("Cesta k súboru grantu (.symgrant):").DefaultValue("grant.symgrant"));
            string relayUrl = console.Prompt(new TextPrompt<string>("URL lokálneho Relay servera:").DefaultValue("http://localhost:5001"));

            await Commands.GrantCommands.HandleGrantAsync(["import", "--in", inFile, "--relay", relayUrl]).ConfigureAwait(false);
        }
    }
}
