#pragma warning disable CA1031, CA1848, CA1873, CA1849, CA1024, CA1054, CA1056

using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Symbolon.Protocol;

namespace Symbolon.Client.Agent;

public sealed class SymbolonAgentDaemon : IAsyncDisposable, IDisposable
{
    private readonly int _port;
    private readonly Uri? _serverUri;
    private readonly string? _initialLicenseKey;
    private readonly ILogger? _logger;
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private Task? _listenerTask;
    private SymbolonClient? _client;
    private SeatLease? _currentLease;
    private string? _licenseKey;
    private string? _lastError;
    private readonly string _machineId;
    private bool _disposed;

    public int Port => _port;
    public bool IsRunning => _listener.IsListening && !_cts.IsCancellationRequested;
    public string MachineId => _machineId;
    public TrayNotificationService Notifications { get; } = new();

    public SymbolonAgentDaemon(int port = 8189, Uri? serverUri = null, string? licenseKey = null, ILogger? logger = null)
    {
        _port = port <= 0 ? 8189 : port;
        _serverUri = serverUri;
        _initialLicenseKey = licenseKey;
        _licenseKey = licenseKey;
        _logger = logger;
        _machineId = Environment.MachineName;

        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            _listener.Start();
            if (_logger != null && _logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Symbolon Agent daemon started on http://127.0.0.1:{Port}/", _port);
            }
            _listenerTask = Task.Run(ListenLoopAsync);
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            if (_logger != null && _logger.IsEnabled(LogLevel.Error))
            {
                _logger.LogError(ex, "Failed to start Symbolon Agent daemon on port {Port}", _port);
            }
            throw;
        }
    }

    private async Task ListenLoopAsync()
    {
        while (!_cts.IsCancellationRequested && _listener.IsListening)
        {
            try
            {
                var context = await _listener.GetContextAsync().ConfigureAwait(false);
                _ = Task.Run(() => HandleRequestAsync(context));
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        var req = context.Request;
        var res = context.Response;

        res.Headers.Add("Access-Control-Allow-Origin", "*");
        res.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
        res.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization");

        if (string.Equals(req.HttpMethod, "OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            res.StatusCode = (int)HttpStatusCode.NoContent;
            res.Close();
            return;
        }

        string path = req.Url?.AbsolutePath.TrimEnd('/') ?? string.Empty;

        try
        {
            if (string.Equals(req.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
            {
                switch (path.ToUpperInvariant())
                {
                    case "/HEALTH":
                        await WriteJsonAsync(res, HttpStatusCode.OK, new { status = "ok", agent = "symbolon-agent", version = "2.4" }).ConfigureAwait(false);
                        return;

                    case "" or "/V1/STATUS":
                        var status = GetStatus();
                        await WriteJsonAsync(res, HttpStatusCode.OK, status, SymbolonProtocolJsonContext.Default.AgentStatusDto).ConfigureAwait(false);
                        return;

                    case "/V1/TOKEN":
                        string? token = _currentLease?.Token ?? _currentLease?.Symlease;
                        if (!string.IsNullOrWhiteSpace(token))
                        {
                            await WriteJsonAsync(res, HttpStatusCode.OK, new { token, leaseId = _currentLease?.LeaseId, seat = _currentLease?.SeatNo }).ConfigureAwait(false);
                        }
                        else
                        {
                            await WriteJsonAsync(res, HttpStatusCode.NotFound, new { error = "No active lease held." }).ConfigureAwait(false);
                        }
                        return;
                }
            }
            else if (string.Equals(req.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
            {
                switch (path.ToUpperInvariant())
                {
                    case "/V1/ACQUIRE":
                        string? reqKey = null;
                        if (req.HasEntityBody)
                        {
                            using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
                            string body = await reader.ReadToEndAsync().ConfigureAwait(false);
                            if (!string.IsNullOrWhiteSpace(body))
                            {
                                try
                                {
                                    using var doc = JsonDocument.Parse(body);
                                    if (doc.RootElement.TryGetProperty("licenseKey", out var elem))
                                    {
                                        reqKey = elem.GetString();
                                    }
                                }
                                catch
                                {
                                    // Ignored, fallback to default key
                                }
                            }
                        }

                        var acqResult = await AcquireAsync(reqKey).ConfigureAwait(false);
                        await WriteJsonAsync(res, acqResult.Success ? HttpStatusCode.OK : HttpStatusCode.BadRequest, acqResult, SymbolonProtocolJsonContext.Default.AgentActionResponseDto).ConfigureAwait(false);
                        return;

                    case "/V1/RELEASE":
                        var relResult = await ReleaseAsync().ConfigureAwait(false);
                        await WriteJsonAsync(res, HttpStatusCode.OK, relResult, SymbolonProtocolJsonContext.Default.AgentActionResponseDto).ConfigureAwait(false);
                        return;

                    case "/V1/BORROW":
                        int days = 7;
                        if (req.HasEntityBody)
                        {
                            using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
                            string body = await reader.ReadToEndAsync().ConfigureAwait(false);
                            if (!string.IsNullOrWhiteSpace(body))
                            {
                                try
                                {
                                    using var doc = JsonDocument.Parse(body);
                                    if (doc.RootElement.TryGetProperty("days", out var elem) && elem.TryGetInt32(out int parsedDays))
                                    {
                                        days = Math.Clamp(parsedDays, 1, 30);
                                    }
                                }
                                catch
                                {
                                    // Ignored, default 7
                                }
                            }
                        }

                        var borrowResult = await BorrowAsync(days).ConfigureAwait(false);
                        await WriteJsonAsync(res, borrowResult.Success ? HttpStatusCode.OK : HttpStatusCode.BadRequest, borrowResult, SymbolonProtocolJsonContext.Default.AgentActionResponseDto).ConfigureAwait(false);
                        return;

                    case "/V1/STOP":
                        await WriteJsonAsync(res, HttpStatusCode.OK, new AgentActionResponseDto(true, "Agent daemon stopping..."), SymbolonProtocolJsonContext.Default.AgentActionResponseDto).ConfigureAwait(false);
                        await _cts.CancelAsync().ConfigureAwait(false);
                        return;
                }
            }

            res.StatusCode = (int)HttpStatusCode.NotFound;
            await WriteJsonAsync(res, HttpStatusCode.NotFound, new { error = $"Endpoint '{path}' not found on Symbolon Agent." }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (_logger != null && _logger.IsEnabled(LogLevel.Error))
            {
                _logger.LogError(ex, "Error processing agent request {Path}", path);
            }
            _lastError = ex.Message;
            try
            {
                await WriteJsonAsync(res, HttpStatusCode.InternalServerError, new { error = ex.Message }).ConfigureAwait(false);
            }
            catch
            {
                // Ignored
            }
        }
    }

    public async Task<AgentActionResponseDto> AcquireAsync(string? licenseKey = null)
    {
        string? effectiveKey = licenseKey ?? _licenseKey ?? _initialLicenseKey;
        if (string.IsNullOrWhiteSpace(effectiveKey))
        {
            return new AgentActionResponseDto(false, "Chýba licenčný kľúč (licenseKey).");
        }

        _licenseKey = effectiveKey;

        try
        {
            if (_client == null)
            {
                var options = new SymbolonClientOptions
                {
                    LicenseKey = effectiveKey,
                    ProductCode = "SYMBOLON-AGENT",
                    ServerUri = _serverUri ?? new Uri("http://localhost:8080"),
                    AutoDiscover = true,
                    MachineId = _machineId
                };
                _client = new SymbolonClient(options, _logger);
            }

            if (_currentLease != null && _currentLease.Acquired)
            {
                return new AgentActionResponseDto(true, $"Lease {_currentLease.LeaseId} je už aktívny.", _currentLease.LeaseId);
            }

            _currentLease = await _client.AcquireSeatAsync().ConfigureAwait(false);
            if (_currentLease.Acquired)
            {
                _lastError = null;
                Notifications.NotifyLeaseAcquired(_currentLease.LeaseId ?? "unknown", _currentLease.SeatNo, DateTimeOffset.UtcNow.AddMinutes(10));
                return new AgentActionResponseDto(true, $"Lease {_currentLease.LeaseId} úspešne získaný.", _currentLease.LeaseId);
            }

            _lastError = _currentLease.Reason ?? "Lease denied by server.";
            return new AgentActionResponseDto(false, $"Získanie licencie zlyhalo: {_lastError}");
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            return new AgentActionResponseDto(false, $"Chyba pri získavaní lease: {ex.Message}");
        }
    }

    public async Task<AgentActionResponseDto> ReleaseAsync()
    {
        if (_currentLease == null)
        {
            return new AgentActionResponseDto(true, "Žiadny aktívny lease nebol držaný.");
        }

        string leaseId = _currentLease.LeaseId ?? "unknown";
        try
        {
            await _currentLease.DisposeAsync().ConfigureAwait(false);
            _currentLease = null;
            Notifications.NotifyLeaseReleased(leaseId);
            return new AgentActionResponseDto(true, $"Lease {leaseId} uvoľnený.", leaseId);
        }
        catch (Exception ex)
        {
            _currentLease = null;
            return new AgentActionResponseDto(false, $"Chyba pri uvoľnení lease {leaseId}: {ex.Message}", leaseId);
        }
    }

    public async Task<AgentActionResponseDto> BorrowAsync(int days = 7)
    {
        if (_currentLease == null || !_currentLease.Acquired)
        {
            var acq = await AcquireAsync().ConfigureAwait(false);
            if (!acq.Success)
            {
                return new AgentActionResponseDto(false, $"Nemožno vypožičať (borrow): najprv je potrebné získať lease. ({acq.Message})");
            }
        }

        var lease = _currentLease;
        if (lease == null)
        {
            return new AgentActionResponseDto(false, "Lease nie je k dispozícii.");
        }

        try
        {
            bool borrowed = await lease.BorrowAsync(days).ConfigureAwait(false);
            if (borrowed)
            {
                Notifications.NotifyOfflineBorrowActive(lease.LeaseId ?? "unknown", days);
                return new AgentActionResponseDto(true, $"Sedadlo úspešne vypožičané na {days} dní offline.", lease.LeaseId);
            }
            return new AgentActionResponseDto(false, "Offline borrow bolo zamietnuté serverom.");
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            return new AgentActionResponseDto(false, $"Offline borrow zlyhalo: {ex.Message}");
        }
    }

    public AgentStatusDto GetStatus()
    {
        string statusText = "idle";
        DateTimeOffset? expiresAt = null;

        var lease = _currentLease;
        if (lease != null)
        {
            statusText = lease.State switch
            {
                SeatState.Active => "active",
                SeatState.GracePeriod => "grace",
                SeatState.Borrowed => "borrowed",
                _ => "idle"
            };
        }

        return new AgentStatusDto(
            statusText,
            _licenseKey,
            lease?.LeaseId,
            lease?.SeatNo,
            expiresAt,
            _machineId,
            true,
            _lastError,
            DateTimeOffset.UtcNow
        );
    }

    private static async Task WriteJsonAsync<T>(HttpListenerResponse res, HttpStatusCode status, T payload, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
    {
        res.StatusCode = (int)status;
        res.ContentType = "application/json; charset=utf-8";
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(payload, typeInfo);
        res.ContentLength64 = bytes.Length;
        await res.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        res.Close();
    }

    private static async Task WriteJsonAsync(HttpListenerResponse res, HttpStatusCode status, object payload)
    {
        res.StatusCode = (int)status;
        res.ContentType = "application/json; charset=utf-8";
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        res.ContentLength64 = bytes.Length;
        await res.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        res.Close();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();

        try { _listener.Stop(); } catch { }
        try { _listener.Close(); } catch { }
        try { _currentLease?.Dispose(); } catch { }
        try { _client?.Dispose(); } catch { }
        _cts.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _cts.CancelAsync().ConfigureAwait(false);

        try { _listener.Stop(); } catch { }
        try { _listener.Close(); } catch { }
        if (_currentLease != null)
        {
            try { await _currentLease.DisposeAsync().ConfigureAwait(false); } catch { }
        }
        try { _client?.Dispose(); } catch { }
        _cts.Dispose();
    }
}
#pragma warning restore CA1031, CA1848, CA1873, CA1849, CA1024, CA1054, CA1056
