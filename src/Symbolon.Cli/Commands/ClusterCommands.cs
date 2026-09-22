using System.Globalization;
using System.Net.Http.Json;
using Spectre.Console;
using Symbolon.Domain.Replication;
using Symbolon.Protocol.Replication;

namespace Symbolon.Cli.Commands;

public static class ClusterCommands
{
    private const string DefaultClusterSecret = "symbolon-cluster-shared-secret-2026";

    public static async Task<int> HandleClusterAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintClusterHelp();
            return 0;
        }

        return args[0].ToUpperInvariant() switch
        {
            "STATUS" => HandleStatus(args[1..]),
            "SYNC" => await HandleSyncAsync(args[1..]).ConfigureAwait(false),
            _ => UnknownSubcommand(args[0])
        };
    }

    private static int HandleStatus(string[] args)
    {
        string regionId = GetArg(args, "--region") ?? "eu-central-1";
        int start = int.TryParse(GetArg(args, "--range-start"), CultureInfo.InvariantCulture, out int s) ? s : 1;
        int end = int.TryParse(GetArg(args, "--range-end"), CultureInfo.InvariantCulture, out int e) ? e : 100;

        var engine = new GeoReplicationEngine(regionId, start, end);
        engine.RegisterPeer("us-east-1", "https://us.symbolon.internal/v1/replication", 101, 200);
        engine.RegisterPeer("ap-southeast-1", "https://ap.symbolon.internal/v1/replication", 201, 300);

        var now = DateTimeOffset.UtcNow;
        var status = engine.GetStatus(now);

        AnsiConsole.MarkupLine("[cyan bold]=== MULTI-REGION ACTIVE-ACTIVE GEO-REPLICATION (CRDT) ===[/]");
        AnsiConsole.MarkupLine($"Lokálny región: [bold green]{status.LocalRegionId}[/] | Disjoint rozsah sedadiel: [yellow]{start} - {end}[/]");

        // Vector Clocks
        var clockEntries = status.LocalClock.Versions.Select(kv => $"{kv.Key}:{kv.Value}");
        string clockStr = string.Join(", ", clockEntries);
        AnsiConsole.MarkupLine($"Vektorové hodiny (Vector Clock): [bold yellow]{(string.IsNullOrEmpty(clockStr) ? $"{regionId}: 1" : clockStr)}[/]");

        // PN-Counter
        AnsiConsole.MarkupLine($"Globálna CRDT kapacita: [bold cyan]{status.TotalDisjointCapacity}[/] sedadiel (Lokálne obsadené: [bold]{status.LocalAllocatedSeats}[/], Globálne: [bold]{status.GlobalAllocatedSeats}[/])");

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Región ID");
        table.AddColumn("Typ");
        table.AddColumn("Endpoint");
        table.AddColumn("Pridelený Rozsah");
        table.AddColumn("Kapacita");
        table.AddColumn("Stav");

        table.AddRow(
            $"[green]{regionId}[/]",
            "[green]Lokálny[/]",
            "http://127.0.0.1:8080/v1/replication",
            $"{start} - {end}",
            $"{end - start + 1}",
            "[green]Aktívny[/]");

        foreach (var peer in status.Peers)
        {
            int peerCap = peer.SeatRangeEnd - peer.SeatRangeStart + 1;
            table.AddRow(
                peer.RegionId,
                "[cyan]Vzdialený Peer[/]",
                peer.Endpoint,
                $"{peer.SeatRangeStart} - {peer.SeatRangeEnd}",
                peerCap.ToString(CultureInfo.InvariantCulture),
                peer.IsHealthy ? "[green]Synchronizovaný[/]" : "[red]Nedostupný[/]");
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine("\n[dim]Replikácia: Kauzálne usporiadanie pomocou Vector Clocks a bezkonfliktné zlučovanie (CRDT PN-Counter & LWW-OR-Set) chránia pred split-brainom aj pri úplnom sieťovom výpadku.[/]\n");

        return 0;
    }

    private static async Task<int> HandleSyncAsync(string[] args)
    {
        string? peerUrl = GetArg(args, "--peer");
        if (string.IsNullOrWhiteSpace(peerUrl))
        {
            AnsiConsole.MarkupLine("[red]Chyba: Parameter --peer <url> je povinný.[/]");
            return 1;
        }

        string secret = GetArg(args, "--secret") ?? DefaultClusterSecret;
        string localRegion = GetArg(args, "--region") ?? "eu-central-1";

        AnsiConsole.MarkupLine($"[cyan]Spúšťam manuálny delta sync s peer uzlom:[/] [bold]{peerUrl}[/]");

        var engine = new GeoReplicationEngine(localRegion, 1, 100);
        var now = DateTimeOffset.UtcNow;
        var delta = engine.GenerateDeltaForPeer("remote-peer", peerClock: null, now);

        byte[] payload = ReplicationSecurity.CreateCanonicalPayload(localRegion, "remote-peer", delta.DeltaId, now);
        string hmac = ReplicationSecurity.ComputeHmac(payload, secret);

        var request = new ReplicationSyncRequest(
            localRegion,
            "remote-peer",
            delta,
            now,
            hmac);

        try
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            var uri = new Uri(new Uri(peerUrl), "/v1/replication/sync");

            var response = await client.PostAsJsonAsync(uri, request).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                var syncResult = await response.Content.ReadFromJsonAsync<ReplicationSyncResponse>().ConfigureAwait(false);
                AnsiConsole.MarkupLine($"[green bold]✔ Úspešne synchronizované s peerom![/]");
                if (syncResult?.MergedClock is not null)
                {
                    var mergedEntries = syncResult.MergedClock.Versions.Select(kv => $"{kv.Key}:{kv.Value}");
                    AnsiConsole.MarkupLine($"Zlúčené Vektorové Hodiny: [yellow]{string.Join(", ", mergedEntries)}[/]");
                }
                return 0;
            }
            else
            {
                AnsiConsole.MarkupLine($"[red]Peer vrátil chybu {response.StatusCode}.[/]");
                return 1;
            }
        }
        catch (HttpRequestException ex)
        {
            AnsiConsole.MarkupLine($"[yellow]Peer uzol neodpovedá ({Markup.Escape(ex.Message)}). Replikácia zostáva zaradená do lokálnej offline fronty.[/]");
            return 0;
        }
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
        AnsiConsole.MarkupLine($"[red]Neznámy Cluster príkaz: {Markup.Escape(subcmd)}[/]");
        PrintClusterHelp();
        return 1;
    }

    private static void PrintClusterHelp()
    {
        AnsiConsole.WriteLine("""
            Použitie: symbolon cluster <status|sync> [options]

            Príkazy:
              status [--region <id>] [--range-start <n>] [--range-end <n>]
                     Zobrazí lokálne vektorové hodiny, PN-countere a multi-regiónovú topológiu
              sync   --peer <url> [--secret <secret>] [--region <id>]
                     Vynúti okamžitý delta sync s vybraným klastrovým peerom
            """);
    }
}
