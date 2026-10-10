using System.Globalization;
using System.Text.Json;
using Spectre.Console;
using Achilles.Domain.Chaos;

namespace Achilles.Cli.Commands;

public static class ChaosCommands
{
    private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };

    public static async Task<int> HandleChaosAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintChaosHelp();
            return 0;
        }

        return args[0].ToUpperInvariant() switch
        {
            "RUN" or "EXECUTE" => await HandleRunAsync(args[1..]).ConfigureAwait(false),
            _ => UnknownSubcommand(args[0])
        };
    }

    private static async Task<int> HandleRunAsync(string[] args)
    {
        string? scenario = GetArg(args, "--scenario");
        string? durationStr = GetArg(args, "--duration");
        string format = GetArg(args, "--format") ?? "table";
        int durationSec = int.TryParse(durationStr, CultureInfo.InvariantCulture, out int d) ? d : 3;

        var runner = new ChaosMonkeyRunner();

        if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(scenario))
            {
                var single = await runner.RunScenarioAsync(scenario, durationSec).ConfigureAwait(false);
                Console.WriteLine(JsonSerializer.Serialize(single, s_jsonOptions));
                return single.Success ? 0 : 1;
            }

            var scorecard = await runner.RunAllScenariosAsync().ConfigureAwait(false);
            Console.WriteLine(JsonSerializer.Serialize(scorecard, s_jsonOptions));
            return scorecard.Passed ? 0 : 1;
        }

        AnsiConsole.MarkupLine("[bold red]=== SYMBOLON CHAOS MONKEY & RESILIENCE RUNNER (Phase 22) ===[/]");
        AnsiConsole.MarkupLine("[dim]Spúšťam automatizovanú injekciu porúch (partition, clock skew, HSM failover, seat storm)...[/]\n");

        if (!string.IsNullOrWhiteSpace(scenario))
        {
            var single = await runner.RunScenarioAsync(scenario, durationSec).ConfigureAwait(false);
            PrintSingleResult(single);
            return single.Success ? 0 : 1;
        }

        var fullScorecard = await runner.RunAllScenariosAsync().ConfigureAwait(false);
        PrintScorecard(fullScorecard);
        return fullScorecard.Passed ? 0 : 1;
    }

    private static void PrintScorecard(ResilienceScorecard scorecard)
    {
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Chaos Scenár");
        table.AddColumn("Výsledok");
        table.AddColumn("Skóre");
        table.AddColumn("Trvanie");
        table.AddColumn("Degradácia");
        table.AddColumn("Detaily");

        foreach (var r in scorecard.Results)
        {
            string statusBadge = r.Success ? "[green bold]ÚSPECH[/]" : "[red bold]ZLYHANIE[/]";
            string scoreColor = r.Score >= 80 ? "green" : (r.Score >= 50 ? "yellow" : "red");

            table.AddRow(
                Markup.Escape(r.ScenarioName),
                statusBadge,
                $"[{scoreColor}]{r.Score.ToString("F1", CultureInfo.InvariantCulture)}%[/]",
                $"{r.DurationMs} ms",
                $"{r.DegradationPercent.ToString("F1", CultureInfo.InvariantCulture)}%",
                Markup.Escape(r.Details));
        }

        AnsiConsole.Write(table);

        string panelColor = scorecard.Passed ? "green" : "red";
        var panel = new Panel(
            new Markup($"[bold]Celkové skóre odolnosti (Resilience Score):[/] [{panelColor} bold]{scorecard.OverallScore.ToString("F1", CultureInfo.InvariantCulture)}% / 100%[/]\n" +
                       $"[bold]Hodnotenie pripravenosti:[/] {Markup.Escape(scorecard.ReadinessAssessment)}"))
            .Header("[bold]Resilience Scorecard Výsledok[/]")
            .BorderColor(scorecard.Passed ? Color.Green : Color.Red)
            .RoundedBorder();

        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    private static void PrintSingleResult(ChaosScenarioResult r)
    {
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Scenár");
        table.AddColumn("Stav");
        table.AddColumn("Skóre");
        table.AddColumn("Trvanie");
        table.AddColumn("Detaily");

        table.AddRow(
            Markup.Escape(r.ScenarioName),
            r.Success ? "[green bold]ÚSPECH[/]" : "[red bold]ZLYHANIE[/]",
            $"{r.Score.ToString("F1", CultureInfo.InvariantCulture)}%",
            $"{r.DurationMs} ms",
            Markup.Escape(r.Details));

        AnsiConsole.Write(table);
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
        AnsiConsole.MarkupLine($"[red]Neznámy Chaos príkaz: {Markup.Escape(subcmd)}[/]");
        PrintChaosHelp();
        return 1;
    }

    private static void PrintChaosHelp()
    {
        AnsiConsole.WriteLine("""
            Použitie: symbolon chaos run [options]

            Príkazy:
              run    Spustí testy odolnosti voči výpadkom (injekcia porúch) a vygeneruje Resilience Scorecard.

            Možnosti:
              --scenario <meno>   Spustí konkrétny scenár: PARTITION, CLOCK_SKEW, HSM, STORM (predvolené: všetky)
              --duration <sek>    Trvanie testovania scenára (predvolené: 3 sekundy)
              --format <typ>      Výstupný formát: table alebo json (predvolené: table)
            """);
    }
}
