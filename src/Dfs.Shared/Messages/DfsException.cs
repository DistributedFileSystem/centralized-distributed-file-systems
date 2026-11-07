using System.Text.Json;

namespace Dfs.Shared.Messages;

/// <summary>Thrown when an <see cref="ErrorResponse"/> is received or a contract operation fails.</summary>
public class DfsException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="code">Error code.</param>
    /// <param name="message">Human-readable message.</param>
    /// <param name="details">Optional extra values.</param>
    public DfsException(ErrorCode code, string message, IReadOnlyDictionary<string, JsonElement>? details = null)
        : base(message)
    {
        Code = code;
        Details = details;
    }

    /// <summary>The error code.</summary>
    public ErrorCode Code { get; }

    /// <summary>Optional extra values (Conflict: currentVersion, NotLeader: leader).</summary>
    public IReadOnlyDictionary<string, JsonElement>? Details { get; }
}
