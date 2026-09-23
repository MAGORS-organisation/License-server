using System.Text.RegularExpressions;
using Symbolon.Protocol;

namespace Symbolon.Client.Discovery;

/// <summary>
/// Resolves and parses Symbolon license server addresses from environment variables,
/// configuration strings, and FlexNet-compatible port@host formats.
/// </summary>
public static partial class SymbolonServerResolver
{
    private const int DefaultPort = 8080;

    /// <summary>
    /// Resolves an ordered list of server URIs from the specified input string or environment variables.
    /// Supports FlexNet notation (e.g. "27000@lic1.corp.com"), standard URIs, and semicolon/comma-separated lists.
    /// </summary>
    /// <param name="input">Optional server list string. If null or whitespace, environment variables are consulted.</param>
    /// <param name="fallbackToEnvironment">If true, consults SYMBOLON_LICENSE_SERVER when input is empty.</param>
    /// <returns>Ordered list of resolved server URIs.</returns>
    public static IReadOnlyList<Uri> Resolve(string? input = null, bool fallbackToEnvironment = true)
    {
        string? candidate = input;

        if (string.IsNullOrWhiteSpace(candidate) && fallbackToEnvironment)
        {
            candidate = Environment.GetEnvironmentVariable(SymbolonDiscoveryConstants.EnvironmentVariable);
            if (string.IsNullOrWhiteSpace(candidate))
            {
                candidate = Environment.GetEnvironmentVariable(SymbolonDiscoveryConstants.EnvironmentVariableAlt);
            }
        }

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return Array.Empty<Uri>();
        }

        var results = new List<Uri>();
        var tokens = candidate.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var token in tokens)
        {
            var parsed = ParseSingle(token);
            if (parsed is not null && !results.Contains(parsed))
            {
                results.Add(parsed);
            }
        }

        return results;
    }

    /// <summary>
    /// Parses a single server token into a normalized Uri.
    /// Examples:
    ///   - "27000@server.acme.corp" -> "http://server.acme.corp:27000"
    ///   - "@server.acme.corp"      -> "http://server.acme.corp:8080"
    ///   - "https://srv.corp:8443"  -> "https://srv.corp:8443"
    ///   - "srv.corp:9000"          -> "http://srv.corp:9000"
    ///   - "srv.corp"               -> "http://srv.corp:8080"
    /// </summary>
    public static Uri? ParseSingle(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        string raw = token.Trim();

        // 1. FlexNet port@host format
        int atIndex = raw.IndexOf('@', StringComparison.Ordinal);
        if (atIndex >= 0)
        {
            string portPart = raw[..atIndex].Trim();
            string hostPart = raw[(atIndex + 1)..].Trim();

            if (string.IsNullOrEmpty(hostPart))
            {
                return null;
            }

            int port = DefaultPort;
            if (!string.IsNullOrEmpty(portPart) && int.TryParse(portPart, out int parsedPort) && parsedPort > 0 && parsedPort <= 65535)
            {
                port = parsedPort;
            }

            return new Uri($"http://{hostPart}:{port}");
        }

        // 2. Standard scheme present
        if (raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (Uri.TryCreate(raw, UriKind.Absolute, out var uri))
            {
                return uri;
            }
            return null;
        }

        // 3. Host:Port without scheme
        if (raw.Contains(':', StringComparison.Ordinal))
        {
            if (Uri.TryCreate($"http://{raw}", UriKind.Absolute, out var uri))
            {
                return uri;
            }
            return null;
        }

        // 4. Bare hostname or IP
        if (Uri.TryCreate($"http://{raw}:{DefaultPort}", UriKind.Absolute, out var bareUri))
        {
            return bareUri;
        }

        return null;
    }
}
