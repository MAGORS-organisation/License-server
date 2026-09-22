using Spectre.Console;
using Symbolon.Relay.Mesh;

namespace Symbolon.Cli.Commands;

public static class MeshCommands
{
    public static Task<int> HandleMeshAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintMeshHelp();
            return Task.FromResult(0);
        }

        return args[0].ToUpperInvariant() switch
        {
            "STATUS" => HandleStatus(args[1..]),
            _ => Task.FromResult(UnknownSubcommand(args[0]))
        };
    }

    private static Task<int> HandleStatus(string[] args)
    {
        string nodeId = GetArg(args, "--node") ?? "rly-mesh-node-1";
        int seats = int.TryParse(GetArg(args, "--seats"), System.Globalization.CultureInfo.InvariantCulture, out int s) ? s : 20;

        var coordinator = new RelayMeshCoordinator(nodeId, Enumerable.Range(1, seats));

        // Sample mesh peers for simulation / topology inspection
        coordinator.RegisterPeer("rly-edge-kosice-01", "https://relay-ke.internal:8443", initialSeats: 15, freeSeats: 8);
        coordinator.RegisterPeer("rly-edge-bratislava-02", "https://relay-ba.internal:8443", initialSeats: 30, freeSeats: 12);
        coordinator.RegisterPeer("rly-edge-prague-03", "https://relay-prg.internal:8443", initialSeats: 25, freeSeats: 20);

        AnsiConsole.MarkupLine($"[cyan bold]=== RELAY MESH TOPOLÓGIA & KONSENZUS (PHASE 3.0) ===[/]");
        AnsiConsole.MarkupLine($"Lokálny uzol: [bold]{coordinator.LocalNodeId}[/] | Lamportove hodiny: [yellow]{coordinator.CurrentClock}[/]");

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Node ID");
        table.AddColumn("Endpoint");
        table.AddColumn("Pridelené");
        table.AddColumn("Voľné");
        table.AddColumn("Posledný Heartbeat");
        table.AddColumn("Stav Konsenzu");

        table.AddRow(
            $"[green]{coordinator.LocalNodeId} (lokálny)[/]",
            "https://localhost:8443",
            $"{seats}",
            $"{seats}",
            "Práve teraz",
            "[green]Líder / Aktívny[/]");

        foreach (var peer in coordinator.GetPeers())
        {
            table.AddRow(
                peer.NodeId,
                peer.Endpoint,
                peer.AllocatedSeats.ToString(System.Globalization.CultureInfo.InvariantCulture),
                peer.FreeSeats.ToString(System.Globalization.CultureInfo.InvariantCulture),
                peer.LastSeen.ToLocalTime().ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
                peer.IsHealthy ? "[green]Synchronizovaný[/]" : "[red]Offline[/]");
        }

        AnsiConsole.Write(table);

        AnsiConsole.MarkupLine("\n[dim]Distribuovaný konsenzus: Disjoint Seat Partitioning s Lamportovými logickými hodinami zabraňuje split-brain overage pri výpadku konektivity.[/]\n");
        return Task.FromResult(0);
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
        AnsiConsole.MarkupLine($"[red]Neznámy Mesh príkaz: {Markup.Escape(subcmd)}[/]");
        PrintMeshHelp();
        return 1;
    }

    private static void PrintMeshHelp()
    {
        AnsiConsole.MarkupLine("""
            [bold]Použitie:[/] symbolon mesh <status> [options]

            Príkazy:
              status  [--node <id>] [--seats <n>]   Zobrazí topológiu a konsenzus Relay Mesh klastra
            """);
    }
}
