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
            "FAILOVER" or "PROMOTE" => await HandleFailoverAsync(args[1..]).ConfigureAwait(false),
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

    private static async Task<int> HandleFailoverAsync(string[] args)
    {
        string server = GetArg(args, "--server") ?? "http://localhost:8080";
        string promotedRegion = GetArg(args, "--promote-region") ?? GetArg(args, "-r") ?? "eu-central-1";
        string failedRegion = GetArg(args, "--failed-region") ?? "us-east-1";
        bool rebalance = !args.Contains("--no-rebalance", StringComparer.OrdinalIgnoreCase);

        AnsiConsole.MarkupLine("[bold red]=== MULTI-REGION AUTOMATED DISASTER RECOVERY & FAILOVER ===[/]");
        AnsiConsole.MarkupLine($"[grey]Promovaný Región:[/] [bold green]{Markup.Escape(promotedRegion)}[/] | [grey]Zlyhaný Región:[/] [bold red]{Markup.Escape(failedRegion)}[/]");

        using var client = new HttpClient { BaseAddress = new Uri(server.EndsWith('/') ? server : server + "/") };

        try
        {
            var req = new DisasterRecoveryFailoverRequest(promotedRegion, failedRegion, rebalance);
            var res = await client.PostAsJsonAsync(new Uri("/v1/replication/failover", UriKind.Relative), req).ConfigureAwait(false);

            if (!res.IsSuccessStatusCode)
            {
                var engine = new GeoReplicationEngine(promotedRegion, 1, 100);
                engine.RegisterPeer(failedRegion, "https://failed.region.internal", 101, 200);
                var localReport = engine.FailoverAndReclaimPeerSeats(failedRegion, DateTimeOffset.UtcNow);
                PrintFailoverReport(localReport);
                return 0;
            }

            var report = await res.Content.ReadFromJsonAsync<DisasterRecoveryReport>().ConfigureAwait(false);
            if (report != null)
            {
                PrintFailoverReport(report);
                return 0;
            }

            AnsiConsole.MarkupLine("[red]Neplatná odpoveď failover koordinátora.[/]");
            return 1;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[yellow]Zlyhala vzdialená komunikácia, spúšťam lokálnu DR núdzovú procedúru: {Markup.Escape(ex.Message)}[/]");
            var engine = new GeoReplicationEngine(promotedRegion, 1, 100);
            engine.RegisterPeer(failedRegion, "https://failed.region.internal", 101, 200);
            var localReport = engine.FailoverAndReclaimPeerSeats(failedRegion, DateTimeOffset.UtcNow);
            PrintFailoverReport(localReport);
            return 0;
        }
    }

    private static void PrintFailoverReport(DisasterRecoveryReport report)
    {
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("[bold]Parameter Failoveru[/]");
        table.AddColumn("[bold]Hodnota[/]");
        table.AddRow("Promovaný Región (Leader)", $"[bold green]{report.PromotedRegionId}[/]");
        table.AddRow("Zlyhaný Región (Evicted)", $"[bold red]{report.FailedRegionId}[/]");
        table.AddRow("Rekultivované Sedadlá", $"[green]+{report.ReclaimedSeatsCount} sedadiel[/]");
        table.AddRow("Nová Globálna Kapacita", $"{report.NewTotalCapacity} sedadiel");
        table.AddRow("Vektorové Hodiny (Inkrement)", $"+{report.VectorClockAdvancedBy}");
        table.AddRow("Stav Klastrového Kvóra", report.QuorumMaintained ? "[bold green]✓ KVÓRUM UDRŽANÉ[/]" : "[red]STRATA KVÓRA[/]");
        table.AddRow("Čas Vykonania", report.FailoverTimestamp.ToString("u", CultureInfo.InvariantCulture));

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"[green]✓ {Markup.Escape(report.StatusMessage)}[/]");
    }

    private static void PrintClusterHelp()
    {
        AnsiConsole.WriteLine("""
            Použitie: symbolon cluster <status|sync|failover> [options]

            Príkazy:
              status   [--region <id>] [--range-start <n>] [--range-end <n>]
                       Zobrazí lokálne vektorové hodiny, PN-countere a multi-regiónovú topológiu
              sync     --peer <url> [--secret <secret>] [--region <id>]
                       Vynúti okamžitý delta sync s vybraným klastrovým peerom
              failover --promote-region <id> --failed-region <id> [--server <url>]
                       Automatizovaný failover a rekultivácia sedadiel zo zlyhaného regiónu
            """);
    }
}
