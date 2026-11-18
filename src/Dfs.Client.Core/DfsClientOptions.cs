namespace Dfs.Client.Core;

/// <summary>
/// Configuration options for <see cref="DfsClient"/>.
/// </summary>
public sealed class DfsClientOptions
{
    /// <summary>
    /// Default metadata server endpoint if none is specified.
    /// </summary>
    public const string DefaultEndpoint = "127.0.0.1:7000";

    /// <summary>
    /// Candidate Metadata Server endpoints to connect to (e.g. "127.0.0.1:7000").
    /// Tried in sequence until a connection and Hello handshake are established.
    /// </summary>
    public IReadOnlyList<string> MetadataEndpoints { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Timeout for establishing a TCP connection and completing the handshake for a single endpoint.
    /// Defaults to 5 seconds.
    /// </summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Timeout for waiting for a server response to a control request.
    /// Defaults to 30 seconds.
    /// </summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Creates options by reading the <c>METADATA_ENDPOINTS</c> environment variable,
    /// falling back to <see cref="DefaultEndpoint"/> if unset or empty.
    /// </summary>
    public static DfsClientOptions FromEnvironment()
    {
        var env = Environment.GetEnvironmentVariable("METADATA_ENDPOINTS");
        var endpoints = ParseEndpoints(env);
        return new DfsClientOptions
        {
            MetadataEndpoints = endpoints.Count > 0 ? endpoints : new[] { DefaultEndpoint }
        };
    }

    /// <summary>
    /// Parses a comma- or semicolon-separated list of host:port endpoints into a list.
    /// </summary>
    /// <param name="endpointString">Comma- or semicolon-delimited string (e.g. "10.0.0.1:7000,10.0.0.2:7000").</param>
    /// <returns>A list of trimmed, non-empty endpoint strings.</returns>
    public static IReadOnlyList<string> ParseEndpoints(string? endpointString)
    {
        if (string.IsNullOrWhiteSpace(endpointString))
        {
            return Array.Empty<string>();
        }

        return endpointString
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToArray();
    }
}

