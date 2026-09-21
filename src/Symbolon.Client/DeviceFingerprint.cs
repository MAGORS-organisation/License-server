using System.Runtime.InteropServices;

namespace Symbolon.Client;

/// <summary>
/// Collects environmental and device components for machine fingerprinting.
/// </summary>
public static class DeviceFingerprint
{
    public static IReadOnlyDictionary<string, string> Collect()
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["machineId"] = Environment.MachineName,
            ["os"] = RuntimeInformation.OSDescription,
            ["arch"] = RuntimeInformation.OSArchitecture.ToString(),
            ["user"] = Environment.UserName,
            ["processorCount"] = Environment.ProcessorCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
    }
}
