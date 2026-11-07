using System.Text.Json;

namespace Dfs.Shared.Messages;

/// <summary>Error response (type 0xFF) that replaces any normal response.</summary>
/// <param name="RequestId">Correlation id of the failed request.</param>
/// <param name="Code">Error code.</param>
/// <param name="Message">Human-readable message.</param>
/// <param name="Details">Optional extra values (Conflict: currentVersion, NotLeader: leader).</param>
public sealed record ErrorResponse(string RequestId, ErrorCode Code, string Message, Dictionary<string, JsonElement>? Details = null);
