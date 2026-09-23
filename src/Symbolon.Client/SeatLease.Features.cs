using System.Collections.Concurrent;
using System.Net.Http.Json;
using Symbolon.Protocol;

namespace Symbolon.Client;

[Serializable]
public class SymbolonFeatureDeniedException : Exception
{
    public string FeatureCode { get; }
    public string Reason { get; }

    public SymbolonFeatureDeniedException() : base()
    {
        FeatureCode = string.Empty;
        Reason = string.Empty;
    }

    public SymbolonFeatureDeniedException(string message) : base(message)
    {
        FeatureCode = string.Empty;
        Reason = message;
    }

    public SymbolonFeatureDeniedException(string message, Exception innerException) : base(message, innerException)
    {
        FeatureCode = string.Empty;
        Reason = message;
    }

    public SymbolonFeatureDeniedException(string featureCode, string reason)
        : base($"Prístup k modulu/funkcii '{featureCode}' bol zamietnutý: {reason}")
    {
        FeatureCode = featureCode;
        Reason = reason;
    }
}

public sealed class FeatureLease : IAsyncDisposable, IDisposable
{
    private readonly SeatLease _parent;
    private int _disposed;

    public string FeatureCode { get; }
    public string? Version { get; }
    public DateTimeOffset ExpiresAt { get; }
    public bool IsActive => _disposed == 0 && _parent.Acquired;

    internal FeatureLease(SeatLease parent, string featureCode, string? version, DateTimeOffset expiresAt)
    {
        _parent = parent;
        FeatureCode = featureCode;
        Version = version;
        ExpiresAt = expiresAt;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            await _parent.ReleaseFeatureAsync(FeatureCode).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _parent.ReleaseFeatureSync(FeatureCode);
        }
    }
}

public sealed partial class SeatLease
{
    private readonly ConcurrentDictionary<string, FeatureLease> _activeFeatures = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Checks whether the specified feature is granted as part of initial checkout entitlements or currently acquired dynamically.
    /// </summary>
    public bool HasFeature(string featureCode)
    {
        if (string.IsNullOrWhiteSpace(featureCode)) return false;

        if (Entitlements is not null)
        {
            for (int i = 0; i < Entitlements.Count; i++)
            {
                if (string.Equals(Entitlements[i], featureCode, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return _activeFeatures.ContainsKey(featureCode);
    }

    /// <summary>
    /// Acquires a granular feature seat within this lease.
    /// </summary>
    public async Task<FeatureLease> AcquireFeatureAsync(
        string featureCode,
        string? version = null,
        int? ttlSeconds = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(featureCode);

        if (!Acquired || string.IsNullOrWhiteSpace(LeaseId))
        {
            throw new InvalidOperationException("Nemožno vyžiadať modul pre neaktívny alebo zamietnutý lease.");
        }

        var requestDto = new AcquireFeatureRequestDto
        {
            FeatureCode = featureCode,
            Version = version,
            TtlSeconds = ttlSeconds
        };

        var response = await _http.PostAsJsonAsync(
            new Uri($"v1/leases/{LeaseId}/features/acquire", UriKind.Relative),
            requestDto,
            SymbolonProtocolJsonContext.Default.AcquireFeatureRequestDto,
            ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            string error = $"Server returned {(int)response.StatusCode}";
            try
            {
                var errBody = await response.Content.ReadFromJsonAsync(
                    SymbolonProtocolJsonContext.Default.FeatureAcquisitionResponseDto,
                    ct).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(errBody?.Reason))
                {
                    error = errBody.Reason;
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Fall back to status code message
            }
            throw new SymbolonFeatureDeniedException(featureCode, error);
        }

        var result = await response.Content.ReadFromJsonAsync(
            SymbolonProtocolJsonContext.Default.FeatureAcquisitionResponseDto,
            ct).ConfigureAwait(false);

        if (result is null || !result.Success)
        {
            throw new SymbolonFeatureDeniedException(featureCode, result?.Reason ?? "feature-denied");
        }

        var featLease = new FeatureLease(this, featureCode, version, _expiresAt);
        _activeFeatures[featureCode] = featLease;
        return featLease;
    }

    /// <summary>
    /// RAII helper: acquires the feature and returns a disposable FeatureLease (use with 'await using').
    /// </summary>
    public Task<FeatureLease> UseFeatureAsync(
        string featureCode,
        string? version = null,
        int? ttlSeconds = null,
        CancellationToken ct = default) =>
        AcquireFeatureAsync(featureCode, version, ttlSeconds, ct);

    /// <summary>
    /// Explicitly releases a held feature seat.
    /// </summary>
    public async Task<bool> ReleaseFeatureAsync(string featureCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(featureCode) || string.IsNullOrWhiteSpace(LeaseId))
        {
            return false;
        }

        _activeFeatures.TryRemove(featureCode, out _);

        var reqDto = new ReleaseFeatureRequestDto { FeatureCode = featureCode };
        try
        {
            var resp = await _http.PostAsJsonAsync(
                new Uri($"v1/leases/{LeaseId}/features/release", UriKind.Relative),
                reqDto,
                SymbolonProtocolJsonContext.Default.ReleaseFeatureRequestDto,
                ct).ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or OperationCanceledException)
        {
            return false;
        }
    }

    internal void ReleaseFeatureSync(string featureCode)
    {
        if (string.IsNullOrWhiteSpace(featureCode) || string.IsNullOrWhiteSpace(LeaseId) || _http is null)
        {
            return;
        }

        _activeFeatures.TryRemove(featureCode, out _);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"v1/leases/{LeaseId}/features/release", UriKind.Relative))
            {
                Content = JsonContent.Create(new ReleaseFeatureRequestDto { FeatureCode = featureCode }, SymbolonProtocolJsonContext.Default.ReleaseFeatureRequestDto)
            };
            using var response = _http.Send(request);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or OperationCanceledException or InvalidOperationException)
        {
            // Silent ignore on synchronous disposal
        }
    }

    /// <summary>
    /// Retrieves currently active features recorded for this lease on the server.
    /// </summary>
    public async Task<IReadOnlyList<ActiveFeatureInfoDto>> GetActiveFeaturesAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(LeaseId)) return [];

        try
        {
            var list = await _http.GetFromJsonAsync(
                new Uri($"v1/leases/{LeaseId}/features", UriKind.Relative),
                SymbolonProtocolJsonContext.Default.ListActiveFeatureInfoDto,
                ct).ConfigureAwait(false);
            return list ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or OperationCanceledException)
        {
            return [];
        }
    }
}
