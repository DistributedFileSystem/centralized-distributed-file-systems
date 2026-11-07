using System.Text.Json;
using Dfs.Shared.Messages;

namespace Dfs.Shared.Metadata;

/// <summary>Outcome of <see cref="IMetadataStore.SubmitAsync"/>.</summary>
/// <param name="Success">True when the command was logged and applied.</param>
/// <param name="Seq">Op log sequence number of the command; 0 when it failed.</param>
/// <param name="Error">Error code when the command was rejected; null on success.</param>
/// <param name="Message">Human-readable reason when rejected.</param>
/// <param name="Details">Extra values, for example currentVersion for Conflict.</param>
public sealed record CommandResult(bool Success, long Seq, ErrorCode? Error = null, string? Message = null, IReadOnlyDictionary<string, JsonElement>? Details = null)
{
    /// <summary>Creates a success result.</summary>
    /// <param name="seq">Op log sequence number.</param>
    /// <returns>A successful result.</returns>
    public static CommandResult Ok(long seq) => new(true, seq);

    /// <summary>Creates a rejection result.</summary>
    /// <param name="error">Error code.</param>
    /// <param name="message">Human-readable reason.</param>
    /// <param name="details">Extra values.</param>
    /// <returns>A failed result.</returns>
    public static CommandResult Fail(ErrorCode error, string message, IReadOnlyDictionary<string, JsonElement>? details = null) =>
        new(false, 0, error, message, details);
}
