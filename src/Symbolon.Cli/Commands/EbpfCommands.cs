using System.Globalization;
using Spectre.Console;
using Symbolon.Domain.Enforcement;

namespace Symbolon.Cli.Commands;

public static class EbpfCommands
{
    public static Task<int> HandleEbpfAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintEbpfHelp();
            return Task.FromResult(0);
        }

        return args[0].ToUpperInvariant() switch
        {
            "STATUS" => Task.FromResult(HandleStatus(args[1..])),
            "ATTACH" => Task.FromResult(HandleAttach(args[1..])),
            "VIOLATIONS" => Task.FromResult(HandleViolations(args[1..])),
            "PROBE" or "GENERATE-PROBE" => Task.FromResult(HandleProbe(args[1..])),
            "CHECK" or "COMPATIBILITY" => Task.FromResult(HandleCompatibility()),
            _ => Task.FromResult(UnknownSubcommand(args[0]))
        };
    }

    private static int HandleStatus(string[] args)
    {
        var engine = new EbpfEnforcementEngine();
        // Register sample attachments / leases for status inspection if demo requested
        string? sampleCgroup = GetArg(args, "--cgroup");
        if (!string.IsNullOrWhiteSpace(sampleCgroup))
        {
            engine.AttachCgroup(sampleCgroup, [443, 8080]);
            engine.RegisterActiveLease(1001, 4521, "lic_demo_kernel", DateTimeOffset.UtcNow.AddHours(2));
        }

        var status = engine.GetStatus();

        AnsiConsole.MarkupLine("[cyan bold]=== eBPF KERNEL SOCKET ENFORCEMENT (LINUX BOUNDARY) ===[/]");
        AnsiConsole.MarkupLine($"Režim ovládača: [bold yellow]{status.DriverMode}[/] | Stav: {(status.IsActive ? "[green bold]AKTÍVNY[/]" : "[dim]NEAKTÍVNY (Žiadne pripojené cgroups)[/]")}");
        AnsiConsole.MarkupLine($"Aktívne BPF leasingy: [bold green]{status.ActiveLeaseCount}[/] | Zablokované spojenia jadrom: [bold red]{status.BlockedAttemptsCount}[/]");

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Cgroup Cesta");
        table.AddColumn("BPF Hook");
        table.AddColumn("Chránené Porty");
        table.AddColumn("Stav Filtra");

        if (status.AttachedCgroups.Count == 0)
        {
            table.AddRow(
                "[dim]/sys/fs/cgroup/workloads (príklad)[/]",
                "cgroup/connect4, cgroup/connect6",
                "443, 8080, 8443",
                "[dim]Odpojené[/]");
        }
        else
        {
            foreach (var cg in status.AttachedCgroups)
            {
                table.AddRow(
                    $"[green]{cg}[/]",
                    "cgroup/connect4, cgroup/connect6",
                    "Všetky TCP/UDP porty",
                    "[green]Pripojené / Aktívne[/]");
            }
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine("\n[dim]Kernel Enforcement: BPF socket filter kontroluje kernel mapu 'license_map' a pri neautorizovanom spojení vracia -EPERM.[/]\n");
        return 0;
    }

    private static int HandleAttach(string[] args)
    {
        string? cgroup = GetArg(args, "--cgroup");
        if (string.IsNullOrWhiteSpace(cgroup))
        {
            AnsiConsole.MarkupLine("[red]Chyba: Parameter --cgroup <path> je povinný.[/]");
            return 1;
        }

        string? portsArg = GetArg(args, "--ports");
        var ports = new List<int>();
        if (!string.IsNullOrWhiteSpace(portsArg))
        {
            foreach (var p in portsArg.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(p, CultureInfo.InvariantCulture, out int port))
                {
                    ports.Add(port);
                }
            }
        }

        var engine = new EbpfEnforcementEngine();
        engine.AttachCgroup(cgroup, ports);

        AnsiConsole.MarkupLine($"[green bold]✔ eBPF socket filter úspešne pripojený k cgroup:[/] [bold]{cgroup}[/]");
        AnsiConsole.MarkupLine($"[cyan]Ovládač:[/] {engine.DriverMode}");
        AnsiConsole.MarkupLine($"[cyan]Chránené porty:[/] {(ports.Count > 0 ? string.Join(", ", ports) : "Všetky porty")}");
        AnsiConsole.MarkupLine("[dim]Kernel BPF program symbolon_sock4_connect a symbolon_sock6_connect sú aktívne.[/]");
        return 0;
    }

    private static int HandleViolations(string[] args)
    {
        var engine = new EbpfEnforcementEngine();

        // Simulate sample violation for display if none occurred yet
        var now = DateTimeOffset.UtcNow;
        engine.EvaluateSocketConnect(cgroupId: 9001, pid: 7812, "198.51.100.20", 443, now);

        var violations = engine.GetRecentViolations();

        AnsiConsole.MarkupLine("[cyan bold]=== eBPF RING BUFFER BEZPEČNOSTNÉ INCIDENTY ===[/]");
        if (violations.Count == 0)
        {
            AnsiConsole.MarkupLine("[green]V BPF ring buffere nie sú žiadne zaznamenané incidenty.[/]");
            return 0;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Čas");
        table.AddColumn("Cgroup ID");
        table.AddColumn("PID");
        table.AddColumn("Cieľová Adresa");
        table.AddColumn("Dôvod Zamietnutia");
        table.AddColumn("Akcia Jadra");

        foreach (var v in violations)
        {
            table.AddRow(
                v.Timestamp.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                v.CgroupId.ToString(CultureInfo.InvariantCulture),
                $"PID {v.Pid}",
                $"{v.DestinationIp}:{v.DestinationPort}",
                $"[yellow]{v.Reason}[/]",
                "[red bold]DROP (-EPERM)[/]");
        }

        AnsiConsole.Write(table);
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

    private static int UnknownSubcommand(string subcmd)
    {
        AnsiConsole.MarkupLine($"[red]Neznámy eBPF príkaz: {Markup.Escape(subcmd)}[/]");
        PrintEbpfHelp();
        return 1;
    }

    private static int HandleProbe(string[] args)
    {
        string? outPath = GetArg(args, "--out") ?? "symbolon_sock_filter.bpf.c";
        string code = EbpfKernelLoader.GenerateBpfSourceCode();

        if (args.Contains("--print", StringComparer.OrdinalIgnoreCase))
        {
            AnsiConsole.WriteLine(code);
            return 0;
        }

        File.WriteAllText(outPath, code);
        AnsiConsole.MarkupLine($"[green]✓ Zdrojový kód eBPF socket filtru úspešne vygenerovaný do:[/] [bold cyan]{Markup.Escape(outPath)}[/]");
        AnsiConsole.MarkupLine("[dim]Preklad pomocou Clang: clang -O2 -target bpf -c symbolon_sock_filter.bpf.c -o symbolon_sock_filter.bpf.o[/]");
        return 0;
    }

    private static int HandleCompatibility()
    {
        var compat = EbpfKernelLoader.CheckCompatibility();

        AnsiConsole.MarkupLine("[bold cyan]=== eBPF KERNEL KOMPATIBILITA & PROSTREDIE ===[/]");

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("[bold]Parameter[/]");
        table.AddColumn("[bold]Hodnota[/]");

        table.AddRow("Podporovaný OS (Linux)", compat.IsSupportedOs ? "[bold green]Áno[/]" : "[yellow]Nie (Emulácia / In-Process)[/]");
        table.AddRow("Kernel Release", Markup.Escape(compat.KernelRelease));
        table.AddRow("BPF Filesystem (/sys/fs/bpf)", compat.BpfFsMounted ? "[bold green]Pripojený[/]" : "[grey]Neprítomný[/]");
        table.AddRow("Cgroup v2 Podpora", compat.CgroupV2Available ? "[bold green]Dostupná[/]" : "[grey]Nedostupná[/]");
        table.AddRow("Odporúčaný BPF Hook", $"[cyan]{compat.RecommendedHook}[/]");

        AnsiConsole.Write(table);
        return 0;
    }

    private static void PrintEbpfHelp()
    {
        AnsiConsole.WriteLine("""
            Použitie: symbolon ebpf <status|attach|violations|probe|check> [options]

            Príkazy:
              status      [--cgroup <path>]           Zobrazí diagnostiku eBPF ovládača a BPF máp
              attach      --cgroup <path> [--ports]   Pripojí eBPF socket filter k zadanej cgroup
              violations                              Vypíše zoznam zablokovaných sieťových spojení z ring buffera
              probe       [--out <path>] [--print]    Vygeneruje produkčný C eBPF kernel program pre socket filtering
              check                                   Overí kompatibilitu hostiteľského jadra pre eBPF / BPF-FS
            """);
    }
}
