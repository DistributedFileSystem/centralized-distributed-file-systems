namespace Dfs.MetadataServer.Handlers;

/// <summary>Validates and splits absolute DFS paths according to <c>docs/protocol.md</c>.</summary>
internal static class NamespacePath
{
    private const int MaxSegmentLength = 255;

    /// <summary>Returns true when <paramref name="segment"/> is a valid single path segment name.</summary>
    public static bool IsValidSegment(string? segment) =>
        !string.IsNullOrWhiteSpace(segment)
        && segment.Length <= MaxSegmentLength
        && !segment.Contains('/')
        && !segment.Contains('\0')
        && segment is not "." and not "..";

    /// <summary>Validates an absolute path and outputs its segments (empty for <c>"/"</c>).</summary>
    public static bool TryParseAbsolute(string? path, out string[] segments)
    {
        segments = Array.Empty<string>();
        if (string.IsNullOrEmpty(path) || path[0] != '/')
        {
            return false;
        }

        if (path == "/")
        {
            return true;
        }

        if (path[^1] == '/')
        {
            return false;
        }

        var parts = path[1..].Split('/');
        foreach (var part in parts)
        {
            if (!IsValidSegment(part))
            {
                return false;
            }
        }

        segments = parts;
        return true;
    }

    /// <summary>Splits a valid non-root path into its parent path and final segment name.</summary>
    public static bool TrySplitParent(string? path, out string parentPath, out string name)
    {
        parentPath = string.Empty;
        name = string.Empty;
        if (!TryParseAbsolute(path, out var segments) || segments.Length == 0)
        {
            return false;
        }

        name = segments[^1];
        parentPath = segments.Length == 1
            ? "/"
            : "/" + string.Join('/', segments, 0, segments.Length - 1);
        return true;
    }
}

