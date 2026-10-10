using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using Microsoft.AspNetCore.Http;
using Achilles.Protocol;

namespace Achilles.ControlPlane.Observability;

/// <summary>
/// Zberateľ živých systémových metrík licenčného servera (CPU, RAM, DISK, NET, IP a operátor).
/// </summary>
public static class TelemetryCollector
{
    private static DateTimeOffset _lastCpuCheck = DateTimeOffset.UtcNow;
    private static TimeSpan _lastProcessorTime = Process.GetCurrentProcess().TotalProcessorTime;
    private static readonly object _syncLock = new();
    private static readonly DateTimeOffset _serverStartTime = DateTimeOffset.UtcNow;
    private static long _lastNetworkBytes;
    private static DateTimeOffset _lastNetworkCheck = DateTimeOffset.UtcNow;

    public static ServerTelemetryDto Collect(HttpContext context, long activeSeats)
    {
        ArgumentNullException.ThrowIfNull(context);

        double cpuPercent = GetCpuUsage();
        double memoryMb = GetMemoryMb();
        double diskMb = GetDiskMb();
        string networkRate = GetNetworkThroughput();

        string ip = context.Connection.LocalIpAddress?.ToString() ?? "127.0.0.1";
        if (ip == "::1")
        {
            ip = "127.0.0.1";
        }

        int port = context.Connection.LocalPort;
        if (port == 0)
        {
            port = 8080;
        }

        string currentUser = "admin:super";
        if (context.User.Identity?.IsAuthenticated == true && !string.IsNullOrEmpty(context.User.Identity.Name))
        {
            currentUser = context.User.Identity.Name;
        }
        else if (context.Request.Headers.TryGetValue("X-Api-Key", out var apiKeyVal) && !string.IsNullOrWhiteSpace(apiKeyVal))
        {
            string keyStr = apiKeyVal.ToString();
            currentUser = keyStr.Length > 12 ? string.Concat(keyStr.AsSpan(0, 12), "…") : keyStr;
        }

        return new ServerTelemetryDto(
            CpuPercent: cpuPercent,
            MemoryMb: memoryMb,
            DiskMb: diskMb,
            NetworkActivity: networkRate,
            ServerIp: $"{ip}:{port}",
            ServerPort: port,
            CurrentUser: currentUser,
            UptimeSeconds: (long)(DateTimeOffset.UtcNow - _serverStartTime).TotalSeconds,
            ActiveSeats: activeSeats);
    }

    private static double GetCpuUsage()
    {
        lock (_syncLock)
        {
            var now = DateTimeOffset.UtcNow;
            using var proc = Process.GetCurrentProcess();
            var currentTotal = proc.TotalProcessorTime;
            var elapsedMs = (now - _lastCpuCheck).TotalMilliseconds;

            if (elapsedMs < 200)
            {
                return 0.0;
            }

            var usedMs = (currentTotal - _lastProcessorTime).TotalMilliseconds;
            var percent = (usedMs / (Environment.ProcessorCount * elapsedMs)) * 100.0;

            _lastCpuCheck = now;
            _lastProcessorTime = currentTotal;

            return Math.Round(Math.Clamp(percent, 0.0, 100.0), 1);
        }
    }

    private static double GetMemoryMb()
    {
        using var proc = Process.GetCurrentProcess();
        proc.Refresh();
        return Math.Round(proc.WorkingSet64 / (1024.0 * 1024.0), 1);
    }

    private static double GetDiskMb()
    {
        long totalBytes = 0;
        try
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            foreach (var file in dir.GetFiles("*.db*", SearchOption.TopDirectoryOnly))
            {
                totalBytes += file.Length;
            }
        }
        catch (IOException)
        {
            // fallback ignore
        }

        if (totalBytes == 0)
        {
            try
            {
                var f = new FileInfo("achilles_controlplane.db");
                if (!f.Exists)
                {
                    f = new FileInfo("symbolon_controlplane.db");
                }

                if (f.Exists)
                {
                    totalBytes = f.Length;
                }
            }
            catch (IOException)
            {
                // fallback ignore
            }
        }

        return Math.Round(totalBytes / (1024.0 * 1024.0), 2);
    }

    private static string GetNetworkThroughput()
    {
        lock (_syncLock)
        {
            long currentBytes = 0;
            try
            {
                foreach (var iface in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (iface.OperationalStatus == OperationalStatus.Up && iface.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    {
                        var stats = iface.GetIPStatistics();
                        currentBytes += stats.BytesReceived + stats.BytesSent;
                    }
                }
            }
            catch (NetworkInformationException)
            {
                // fallback
            }

            var now = DateTimeOffset.UtcNow;
            var elapsed = (now - _lastNetworkCheck).TotalSeconds;
            if (elapsed <= 0 || _lastNetworkBytes == 0)
            {
                _lastNetworkBytes = currentBytes;
                _lastNetworkCheck = now;
                return "0.0 KB/s";
            }

            var diffBytes = currentBytes - _lastNetworkBytes;
            _lastNetworkBytes = currentBytes;
            _lastNetworkCheck = now;

            if (diffBytes < 0)
            {
                diffBytes = 0;
            }

            double rateKb = (diffBytes / elapsed) / 1024.0;
            if (rateKb > 1024.0)
            {
                return $"{Math.Round(rateKb / 1024.0, 2)} MB/s";
            }

            return $"{Math.Round(rateKb, 1)} KB/s";
        }
    }
}
