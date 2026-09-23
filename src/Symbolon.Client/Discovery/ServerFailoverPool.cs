using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace Symbolon.Client.Discovery;

/// <summary>
/// Exception thrown when all configured license servers in the failover pool have been exhausted.
/// </summary>
public sealed class SymbolonFailoverExhaustedException : Exception
{
    public SymbolonFailoverExhaustedException() : base("All failover servers exhausted.") { }
    public SymbolonFailoverExhaustedException(string message) : base(message) { }
    public SymbolonFailoverExhaustedException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Status record for a server node within the failover pool.
/// </summary>
public sealed class ServerNodeStatus
{
    public required Uri ServerUri { get; init; }
    public bool IsHealthy { get; set; } = true;
    public int ConsecutiveFailures { get; set; }
    public DateTimeOffset? CooldownUntil { get; set; }
    public DateTimeOffset? LastAttempt { get; set; }
}

/// <summary>
/// Manages high-availability failover across a list of Symbolon server endpoints.
/// Automatically retries operations on subsequent nodes if the primary node is unreachable.
/// </summary>
[SuppressMessage("Usage", "CA1848:Use the LoggerMessage delegates", Justification = "SDK internal client logging")]
[SuppressMessage("Performance", "CA1873:Avoid potentially expensive evaluations", Justification = "Uri formatting in low-frequency log")]
public sealed class ServerFailoverPool : IDisposable
{
    private readonly List<ServerNodeStatus> _nodes;
    private readonly Dictionary<Uri, HttpClient> _httpClients = new();
    private readonly object _lock = new();
    private readonly TimeSpan _cooldownDuration;
    private readonly TimeProvider _time;
    private readonly ILogger? _log;
    private readonly HttpClient? _injectedClient;
    private bool _disposed;

    public ServerFailoverPool(
        IEnumerable<Uri> serverUris,
        TimeSpan? cooldownDuration = null,
        TimeProvider? timeProvider = null,
        HttpClient? customHttpClient = null,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(serverUris);

        _nodes = serverUris.Select(u => new ServerNodeStatus { ServerUri = u }).ToList();
        _cooldownDuration = cooldownDuration ?? TimeSpan.FromSeconds(15);
        _time = timeProvider ?? TimeProvider.System;
        _injectedClient = customHttpClient;
        _log = logger;
    }

    /// <summary>
    /// Current count of configured server nodes in the pool.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _nodes.Count;
            }
        }
    }

    /// <summary>
    /// Returns the snapshot of all server node statuses.
    /// </summary>
    public IReadOnlyList<ServerNodeStatus> GetNodes()
    {
        lock (_lock)
        {
            return _nodes.Select(n => new ServerNodeStatus
            {
                ServerUri = n.ServerUri,
                IsHealthy = n.IsHealthy,
                ConsecutiveFailures = n.ConsecutiveFailures,
                CooldownUntil = n.CooldownUntil,
                LastAttempt = n.LastAttempt
            }).ToList();
        }
    }

    /// <summary>
    /// Adds a newly discovered server to the pool if not already present.
    /// </summary>
    public void AddServer(Uri serverUri)
    {
        ArgumentNullException.ThrowIfNull(serverUri);

        lock (_lock)
        {
            if (!_nodes.Any(n => n.ServerUri == serverUri))
            {
                _nodes.Add(new ServerNodeStatus { ServerUri = serverUri });
                _log?.LogInformation("Added server {Uri} to failover pool.", serverUri);
            }
        }
    }

    /// <summary>
    /// Executes an asynchronous network operation against the server pool with automatic failover.
    /// </summary>
    public async Task<T> ExecuteWithFailoverAsync<T>(
        Func<Uri, HttpClient, CancellationToken, Task<T>> operation,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        List<ServerNodeStatus> candidates;
        var now = _time.GetUtcNow();

        lock (_lock)
        {
            if (_nodes.Count == 0)
            {
                throw new SymbolonFailoverExhaustedException("No servers configured in failover pool.");
            }

            // Prioritize healthy nodes and nodes whose cooldown has passed
            candidates = _nodes
                .OrderBy(n => n.CooldownUntil.HasValue && n.CooldownUntil.Value > now)
                .ThenBy(n => n.ConsecutiveFailures)
                .ToList();
        }

        Exception? lastError = null;

        foreach (var node in candidates)
        {
            ct.ThrowIfCancellationRequested();

            var httpClient = GetHttpClientFor(node.ServerUri);
            node.LastAttempt = now;

            try
            {
                var result = await operation(node.ServerUri, httpClient, ct).ConfigureAwait(false);

                // Success: mark node healthy
                lock (_lock)
                {
                    node.IsHealthy = true;
                    node.ConsecutiveFailures = 0;
                    node.CooldownUntil = null;
                }

                return result;
            }
            catch (Exception ex) when (IsTransientNetworkException(ex, ct))
            {
                lastError = ex;

                lock (_lock)
                {
                    node.ConsecutiveFailures++;
                    node.IsHealthy = false;
                    node.CooldownUntil = now.Add(_cooldownDuration);
                }

                _log?.LogWarning(ex, "Connection to Symbolon server {Uri} failed. Failing over to next available server.", node.ServerUri);
            }
        }

        throw new SymbolonFailoverExhaustedException(
            $"All {candidates.Count} server(s) in failover pool failed. Last error: {lastError?.Message}",
            lastError!);
    }

    private HttpClient GetHttpClientFor(Uri serverUri)
    {
        if (_injectedClient is not null)
        {
            return _injectedClient;
        }

        lock (_lock)
        {
            if (!_httpClients.TryGetValue(serverUri, out var client))
            {
                client = new HttpClient { BaseAddress = serverUri };
                _httpClients[serverUri] = client;
            }
            return client;
        }
    }

    private static bool IsTransientNetworkException(Exception ex, CancellationToken ct)
    {
        if (ct.IsCancellationRequested && ex is OperationCanceledException)
        {
            return false; // Caller intentionally cancelled
        }

        return ex is HttpRequestException ||
               ex is SocketException ||
               ex is TimeoutException ||
               (ex is OperationCanceledException && !ct.IsCancellationRequested);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_injectedClient is null)
        {
            lock (_lock)
            {
                foreach (var client in _httpClients.Values)
                {
                    client.Dispose();
                }
                _httpClients.Clear();
            }
        }
    }
}
