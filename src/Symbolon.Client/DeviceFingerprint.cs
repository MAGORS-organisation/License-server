using System.Diagnostics;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Symbolon.Protocol;

#pragma warning disable CA1031 // Hardware component collection must safely degrade across diverse OS/hardware without crashing

namespace Symbolon.Client;

/// <summary>
/// Multi-component device fingerprinting, container isolation and anti-virtualization engine
/// implementing FPR-1 through FPR-4 and FPR-10 through FPR-14 (spec/08-fingerprint.md).
/// </summary>
public static class DeviceFingerprint
{
    private static readonly object UuidLock = new();

    /// <summary>
    /// Checks whether the current process is running inside a container, Kubernetes pod, or cloud instance
    /// according to FPR-10 and FPR-11.
    /// </summary>
    public static bool IsContainerOrCloud()
    {
        // 1. Container marker files
        if (File.Exists("/.dockerenv") || File.Exists("/run/.containerenv"))
        {
            return true;
        }

        // 2. Linux /proc/1/cgroup inspection
        try
        {
            if (File.Exists("/proc/1/cgroup"))
            {
                string cgroup = File.ReadAllText("/proc/1/cgroup");
                if (cgroup.Contains("docker", StringComparison.OrdinalIgnoreCase) ||
                    cgroup.Contains("containerd", StringComparison.OrdinalIgnoreCase) ||
                    cgroup.Contains("kubepods", StringComparison.OrdinalIgnoreCase) ||
                    cgroup.Contains("lxc", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch
        {
            // Ignore access errors on non-Linux or restricted systems
        }

        // 3. Container and Cloud environment variables (FPR-11)
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST")) ||
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("container")) ||
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER")) ||
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AWS_EXECUTION_ENV")) ||
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ECS_CONTAINER_METADATA_URI")) ||
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AZURE_CONTAINER_APP_NAME")) ||
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GOOGLE_CLOUD_PROJECT")))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Collects machine hardware components according to FPR-1, FPR-2, FPR-3, and FPR-12.
    /// If containerized or in cloud, hardware scanning is skipped, a persisted volume UUID is used,
    /// and a warning is logged recommending floating licenses with short TTL (FPR-12, FPR-13).
    /// </summary>
    public static IReadOnlyDictionary<string, string> Collect(
        string? licenseSalt = null,
        string? customVolumePath = null,
        Action<string>? warningLogger = null)
    {
        var components = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // FPR-10 & FPR-12: If container or cloud instance detected, DO NOT use hardware!
        if (IsContainerOrCloud())
        {
            // FPR-13: Log recommendation to use floating license instead of node-lock
            warningLogger?.Invoke(
                "Containerized or cloud environment detected. Hardware node-locking is an anti-pattern in containers. Recommended: floating license with short lease TTL (FPR-13).");

            // FPR-12: Use persisted random UUID in volume or cloud instance ID
            string persistedUuid = GetOrCreatePersistedContainerUuid(customVolumePath);
            components[FingerprintComponentKeys.MachineId] = persistedUuid;
            components["isContainer"] = "true";

            if (!string.IsNullOrWhiteSpace(licenseSalt))
            {
                components[FingerprintComponentKeys.Host] = FingerprintHelper.PseudonymizeHost(Environment.MachineName, licenseSalt);
            }

            return FingerprintHelper.FilterValidComponents(components);
        }

        // FPR-1: Collect hardware components
        // 1. machineId
        string? machineId = CollectMachineId();
        if (!string.IsNullOrWhiteSpace(machineId))
        {
            components[FingerprintComponentKeys.MachineId] = machineId;
        }

        // 2. cpu
        string? cpu = CollectCpuInfo();
        if (!string.IsNullOrWhiteSpace(cpu))
        {
            components[FingerprintComponentKeys.Cpu] = cpu;
        }

        // 3. board
        string? board = CollectBoardSerial();
        if (!string.IsNullOrWhiteSpace(board))
        {
            components[FingerprintComponentKeys.Board] = board;
        }

        // 4. disk
        string? disk = CollectDiskSerial();
        if (!string.IsNullOrWhiteSpace(disk))
        {
            components[FingerprintComponentKeys.Disk] = disk;
        }

        // 5. mac
        string? mac = CollectMacAddress();
        if (!string.IsNullOrWhiteSpace(mac))
        {
            components[FingerprintComponentKeys.Mac] = mac;
        }

        // 6. host (FPR-2: MUST NOT be transmitted in plaintext; pseudonymized if salt provided)
        string rawHost = Environment.MachineName;
        if (!string.IsNullOrWhiteSpace(rawHost))
        {
            if (!string.IsNullOrWhiteSpace(licenseSalt))
            {
                components[FingerprintComponentKeys.Host] = FingerprintHelper.PseudonymizeHost(rawHost, licenseSalt);
            }
            else
            {
                components[FingerprintComponentKeys.Host] = rawHost;
            }
        }

        // Environmental metadata (optional non-conflicting components)
        components["os"] = RuntimeInformation.OSDescription;
        components["arch"] = RuntimeInformation.OSArchitecture.ToString();

        // FPR-3: Filter out placeholders, empty strings, "unknown", zeros
        return FingerprintHelper.FilterValidComponents(components);
    }

    private static string GetOrCreatePersistedContainerUuid(string? customPath)
    {
        lock (UuidLock)
        {
            string path;
            if (!string.IsNullOrWhiteSpace(customPath))
            {
                path = customPath;
            }
            else
            {
                string baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Symbolon");
                Directory.CreateDirectory(baseDir);
                path = Path.Combine(baseDir, "container_instance_uuid.txt");
            }

            try
            {
                if (File.Exists(path))
                {
                    string existing = File.ReadAllText(path).Trim();
                    if (!string.IsNullOrWhiteSpace(existing) && Guid.TryParse(existing, out _))
                    {
                        return existing;
                    }
                }

                string newUuid = Guid.NewGuid().ToString("D");
                string dir = Path.GetDirectoryName(path)!;
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(path, newUuid);
                return newUuid;
            }
            catch
            {
                // Fallback if filesystem write is restricted in container
                return "uuid-" + Guid.NewGuid().ToString("N");
            }
        }
    }

    private static string? CollectMachineId()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                // Windows: HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid
                using var proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "reg",
                        Arguments = @"query HKLM\SOFTWARE\Microsoft\Cryptography /v MachineGuid",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                proc.Start();
                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(1000);

                foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.Contains("MachineGuid", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 3)
                        {
                            return parts[^1].Trim();
                        }
                    }
                }
            }
            else if (OperatingSystem.IsLinux())
            {
                // Linux: /etc/machine-id or /var/lib/dbus/machine-id
                if (File.Exists("/etc/machine-id"))
                {
                    string id = File.ReadAllText("/etc/machine-id").Trim();
                    if (!string.IsNullOrWhiteSpace(id)) return id;
                }
                if (File.Exists("/var/lib/dbus/machine-id"))
                {
                    string id = File.ReadAllText("/var/lib/dbus/machine-id").Trim();
                    if (!string.IsNullOrWhiteSpace(id)) return id;
                }
            }
            else if (OperatingSystem.IsMacOS())
            {
                using var proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "ioreg",
                        Arguments = "-rd1 -c IOPlatformExpertDevice",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                proc.Start();
                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(1000);

                foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.Contains("IOPlatformUUID", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = line.Split('=');
                        if (parts.Length == 2)
                        {
                            return parts[1].Trim(' ', '"', ';');
                        }
                    }
                }
            }
        }
        catch
        {
            // Ignore collection exceptions and fallback to machine name
        }

        return Environment.MachineName;
    }

    private static string? CollectCpuInfo()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                string? envCpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");
                if (!string.IsNullOrWhiteSpace(envCpu))
                {
                    return envCpu.Trim();
                }
            }
            else if (OperatingSystem.IsLinux() && File.Exists("/proc/cpuinfo"))
            {
                foreach (var line in File.ReadAllLines("/proc/cpuinfo"))
                {
                    if (line.StartsWith("model name", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = line.Split(':');
                        if (parts.Length == 2)
                        {
                            return parts[1].Trim();
                        }
                    }
                }
            }
        }
        catch
        {
            // Ignore
        }

        return Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture) + "-cores";
    }

    private static string? CollectBoardSerial()
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                if (File.Exists("/sys/class/dmi/id/board_serial"))
                {
                    string s = File.ReadAllText("/sys/class/dmi/id/board_serial").Trim();
                    if (!string.IsNullOrWhiteSpace(s)) return s;
                }
                if (File.Exists("/sys/class/dmi/id/product_serial"))
                {
                    string s = File.ReadAllText("/sys/class/dmi/id/product_serial").Trim();
                    if (!string.IsNullOrWhiteSpace(s)) return s;
                }
            }
        }
        catch
        {
            // Ignore
        }

        return null;
    }

    private static string? CollectDiskSerial()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var drives = DriveInfo.GetDrives();
                var sysDrive = drives.FirstOrDefault(d => d.IsReady && d.RootDirectory.FullName.StartsWith("C:", StringComparison.OrdinalIgnoreCase));
                if (sysDrive is not null)
                {
                    return sysDrive.VolumeLabel + "-" + sysDrive.DriveFormat;
                }
            }
        }
        catch
        {
            // Ignore
        }

        return null;
    }

    private static string? CollectMacAddress()
    {
        try
        {
            var nics = NetworkInterface.GetAllNetworkInterfaces();
            var firstNic = nics.FirstOrDefault(n =>
                n.OperationalStatus == OperationalStatus.Up &&
                n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                n.NetworkInterfaceType != NetworkInterfaceType.Tunnel &&
                !n.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase) &&
                !n.Description.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase) &&
                !n.Description.Contains("VMware", StringComparison.OrdinalIgnoreCase) &&
                !n.Description.Contains("WSL", StringComparison.OrdinalIgnoreCase) &&
                n.GetPhysicalAddress().GetAddressBytes().Length == 6);

            if (firstNic is not null)
            {
                byte[] bytes = firstNic.GetPhysicalAddress().GetAddressBytes();
                return string.Join(":", bytes.Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));
            }
        }
        catch
        {
            // Ignore
        }

        return null;
    }
}
