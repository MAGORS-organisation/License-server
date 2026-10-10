#pragma warning disable CA1848, CA1031, CA1003

using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Symbolon.Client.Agent;

public enum TrayNotificationLevel
{
    Info,
    Warning,
    Error,
    Success
}

public sealed record TrayNotification(
    string Title,
    string Message,
    TrayNotificationLevel Level,
    DateTimeOffset Timestamp
);

public sealed class TrayNotificationService
{
    private readonly ILogger? _logger;
    private readonly List<TrayNotification> _history = [];
    private readonly object _lock = new();

    public IReadOnlyList<TrayNotification> History
    {
        get
        {
            lock (_lock)
            {
                return _history.ToArray();
            }
        }
    }

    public event Action<TrayNotification>? NotificationRaised;

    public TrayNotificationService(ILogger? logger = null)
    {
        _logger = logger;
    }

    public void Notify(string title, string message, TrayNotificationLevel level = TrayNotificationLevel.Info)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        var notification = new TrayNotification(title, message, level, DateTimeOffset.UtcNow);

        lock (_lock)
        {
            _history.Add(notification);
            if (_history.Count > 100)
            {
                _history.RemoveAt(0);
            }
        }

        NotificationRaised?.Invoke(notification);

        if (_logger != null && _logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("[TRAY NOTIFICATION - {Level}] {Title}: {Message}", level, title, message);
        }

        // Fire desktop notification asynchronously without blocking
        _ = Task.Run(() => DispatchDesktopNotification(notification));
    }

    public void NotifyLeaseAcquired(string leaseId, int seatNo, DateTimeOffset expiresAt)
    {
        string remStr = (expiresAt - DateTimeOffset.UtcNow).TotalMinutes.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);
        Notify(
            "Symbolon: Licencia Aktívna",
            $"Sedadlo #{seatNo} pridelené (Lease {leaseId}, platnosť {remStr} minút)",
            TrayNotificationLevel.Success
        );
    }

    public void NotifyExpiringSoon(string leaseId, TimeSpan remaining)
    {
        Notify(
            "Symbolon Výstraha: Blíži sa expirácia",
            $"Lease {leaseId} expiruje o {remaining.TotalMinutes:F0} minút. Skontrolujte sieťové spojenie.",
            TrayNotificationLevel.Warning
        );
    }

    public void NotifyNetworkFailureGrace(TimeSpan remainingGrace)
    {
        Notify(
            "Symbolon: Offline Ochranná Lehota (Grace)",
            $"Licenčný server je nedostupný. Prepnuté do offline grace módu ({remainingGrace.TotalHours:F1} hodín zostáva).",
            TrayNotificationLevel.Warning
        );
    }

    public void NotifyOfflineBorrowActive(string leaseId, int days)
    {
        Notify(
            "Symbolon: Offline Roaming Aktívny",
            $"Sedadlo bolo vypožičané na {days} dní pre prácu bez internetu (Lease {leaseId}).",
            TrayNotificationLevel.Info
        );
    }

    public void NotifyLeaseReleased(string leaseId)
    {
        Notify(
            "Symbolon: Sedadlo Uvoľnené",
            $"Lease {leaseId} bol vrátený do floating poolu pre ostatných používateľov.",
            TrayNotificationLevel.Info
        );
    }

    private static void DispatchDesktopNotification(TrayNotification notification)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                string safeTitle = notification.Title.Replace("\"", "\\\"", StringComparison.Ordinal);
                string safeMsg = notification.Message.Replace("\"", "\\\"", StringComparison.Ordinal);

                using var proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "powershell",
                        Arguments = $"-NoProfile -WindowStyle Hidden -Command \"Write-Output '{safeTitle}: {safeMsg}'\"",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    }
                };
                proc.Start();
                proc.WaitForExit(1000);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                using var proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "notify-send",
                        Arguments = $"\"{notification.Title}\" \"{notification.Message}\"",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    }
                };
                proc.Start();
                proc.WaitForExit(1000);
            }
        }
        catch
        {
            // Silent ignore if desktop notification service is not present (e.g. headless CI)
        }
    }
}
#pragma warning restore CA1848, CA1031, CA1003
