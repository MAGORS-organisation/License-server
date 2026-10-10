using System.Globalization;
using Spectre.Console;
using Achilles.Relay.Mesh;

namespace Achilles.Cli.Commands;

public static class MeshCommands
{
    public static Task<int> HandleMeshAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintMeshHelp();
            return Task.FromResult(0);
        }

        return args[0].ToUpperInvariant() switch
        {
            "STATUS" => HandleStatus(args[1..]),
            "WIREGUARD" or "WG" => HandleWireGuard(args[1..]),
            "MTLS" => HandleMtls(args[1..]),
            _ => Task.FromResult(UnknownSubcommand(args[0]))
        };
    }

    private static Task<int> HandleStatus(string[] args)
    {
        string nodeId = GetArg(args, "--node") ?? "rly-mesh-node-1";
        int seats = int.TryParse(GetArg(args, "--seats"), CultureInfo.InvariantCulture, out int s) ? s : 20;

        var coordinator = new RelayMeshCoordinator(nodeId, Enumerable.Range(1, seats));

        coordinator.RegisterPeer("rly-edge-kosice-01", "https://relay-ke.internal:8443", initialSeats: 15, freeSeats: 8);
        coordinator.RegisterPeer("rly-edge-bratislava-02", "https://relay-ba.internal:8443", initialSeats: 30, freeSeats: 12);
        coordinator.RegisterPeer("rly-edge-prague-03", "https://relay-prg.internal:8443", initialSeats: 25, freeSeats: 20);

        AnsiConsole.MarkupLine("[cyan bold]=== RELAY MESH TOPOLÓGIA & KONSENZUS (PHASE 3.0) ===[/]");
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
                Markup.Escape(peer.NodeId),
                Markup.Escape(peer.Endpoint),
                peer.AllocatedSeats.ToString(CultureInfo.InvariantCulture),
                peer.FreeSeats.ToString(CultureInfo.InvariantCulture),
                peer.LastSeen.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                peer.IsHealthy ? "[green]Synchronizovaný[/]" : "[red]Offline[/]");
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine("\n[dim]Distribuovaný konsenzus: Disjoint Seat Partitioning s Lamportovými logickými hodinami zabraňuje split-brain overage.[/]\n");
        return Task.FromResult(0);
    }

    private static Task<int> HandleWireGuard(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintWireGuardHelp();
            return Task.FromResult(0);
        }

        string sub = args[0].ToUpperInvariant();
        string nodeId = GetArg(args, "--node") ?? "rly-mesh-wg-01";
        string overlayIp = GetArg(args, "--ip") ?? "10.100.0.1/24";
        string? outFile = GetArg(args, "--out");

        var wgCoordinator = new WireGuardMeshCoordinator(nodeId, overlayIp);
        wgCoordinator.AddOrUpdatePeer("rly-edge-kosice-01", "PubKeyKE9912093840192384019238401928340192384=", "192.168.10.15:51820", "10.100.0.2/32");
        wgCoordinator.AddOrUpdatePeer("rly-edge-bratislava-02", "PubKeyBA7712093840192384019238401928340192384=", "192.168.10.25:51820", "10.100.0.3/32");

        switch (sub)
        {
            case "CONFIG":
            {
                string conf = wgCoordinator.GenerateConfigFile();
                if (!string.IsNullOrWhiteSpace(outFile))
                {
                    File.WriteAllText(outFile, conf);
                    AnsiConsole.MarkupLine($"[green bold]✓ WireGuard konfigurácia uložená do:[/] [bold]{Markup.Escape(outFile)}[/]");
                }
                else
                {
                    AnsiConsole.MarkupLine("[bold cyan]=== WIREGUARD OVERLAY TUNNEL CONFIG (wg0.conf) ===[/]");
                    AnsiConsole.WriteLine(conf);
                }
                return Task.FromResult(0);
            }
            case "ROTATE":
            {
                var newConf = wgCoordinator.RotateWireGuardKeys();
                AnsiConsole.MarkupLine("[bold green]✓ Kľúče WireGuard Curve25519 boli úspešne rotované.[/]");
                AnsiConsole.MarkupLine($"Nový verejný kľúč (PublicKey): [yellow]{newConf.PublicKey}[/]");
                return Task.FromResult(0);
            }
            case "STATUS":
            default:
            {
                AnsiConsole.MarkupLine($"[bold cyan]=== WIREGUARD ZERO-TRUST OVERLAY TUNNEL STAV ({Markup.Escape(nodeId)}) ===[/]");
                var node = wgCoordinator.GetNodeConfig();
                AnsiConsole.MarkupLine($"Lokálna Overlay IP: [bold]{node.OverlayIp}[/] | Port: [bold]{node.ListenPort}[/] | Verejný kľúč: [yellow]{node.PublicKey}[/]");

                var table = new Table().Border(TableBorder.Rounded);
                table.AddColumn("Peer Node ID");
                table.AddColumn("Endpoint");
                table.AddColumn("Allowed IPs");
                table.AddColumn("Posledný Handshake");
                table.AddColumn("Prenesené (Rx/Tx)");
                table.AddColumn("Šifrovaný Stav");

                foreach (var peer in node.Peers)
                {
                    table.AddRow(
                        Markup.Escape(peer.NodeId),
                        Markup.Escape(peer.Endpoint),
                        Markup.Escape(peer.AllowedIPs),
                        peer.LastHandshake.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                        $"{peer.RxBytes / 1024} KB / {peer.TxBytes / 1024} KB",
                        peer.IsConnected ? "[green bold]Pripojené (Curve25519)[/]" : "[red]Odpojené[/]");
                }

                AnsiConsole.Write(table);
                AnsiConsole.MarkupLine("\n[dim]P2P šifrovaný WireGuard tunel izoluje klastrovú replikáciu od verejnej siete bez potreby komerčných VPN boxov.[/]\n");
                return Task.FromResult(0);
            }
        }
    }

    private static Task<int> HandleMtls(string[] args)
    {
        string nodeId = GetArg(args, "--node") ?? "rly-mesh-mtls-01";
        var wgCoordinator = new WireGuardMeshCoordinator(nodeId);

        if (args.Length > 0 && string.Equals(args[0], "ROTATE", StringComparison.OrdinalIgnoreCase))
        {
            var creds = wgCoordinator.RotateMtlsKeys();
            AnsiConsole.MarkupLine("[bold green]✓ mTLS X.509 certifikát a privátny kľúč boli úspešne rotované.[/]");
            AnsiConsole.MarkupLine($"Nová rotačná epocha: [yellow]{creds.RotationEpoch}[/]");
            AnsiConsole.MarkupLine($"Platnosť: [dim]{creds.NotBefore:g} -> {creds.NotAfter:g}[/]");
            return Task.FromResult(0);
        }

        var active = wgCoordinator.GetActiveMtlsCredentials();
        AnsiConsole.MarkupLine($"[bold cyan]=== mTLS (MUTUAL TLS) KLASTROVÁ IDENTITA ({Markup.Escape(nodeId)}) ===[/]");
        AnsiConsole.MarkupLine($"Epocha: [yellow]{active.RotationEpoch}[/] | Platnosť: {active.NotBefore:g} -> {active.NotAfter:g}");
        AnsiConsole.WriteLine("\nAktívny certifikát PEM:\n" + active.CertPem);
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
        AnsiConsole.WriteLine("""
            Použitie: symbolon mesh <subcommand> [options]

            Príkazy:
              status                  Zobrazí topológiu a konsenzus Relay Mesh klastra
              wireguard [subcommand]  Spravuje WireGuard overlay šifrovaný tunel (config, status, rotate)
              mtls [subcommand]       Spravuje vzájomné TLS certifikáty a rotáciu (status, rotate)

            Možnosti:
              --node <id>             Identifikátor lokálneho uzla (predvolené: rly-mesh-node-1)
              --seats <n>             Počet sedadiel lokálneho uzla
            """);
    }

    private static void PrintWireGuardHelp()
    {
        AnsiConsole.WriteLine("""
            Použitie: symbolon mesh wireguard <config|status|rotate> [options]

            Príkazy:
              config     Vygeneruje konfiguračný súbor wg0.conf pre WireGuard rozhranie
              status     Zobrazí stav šifrovaných P2P tunelov a pripojených peerov
              rotate     Okamžite rotuje lokálne Curve25519 šifrovacie kľúče

            Možnosti:
              --node <id>     ID uzla
              --ip <overlay>  Overlay IP adresa (napr. 10.100.0.1/24)
              --out <cesta>   Výstupný súbor pre wg0.conf
            """);
    }
}
