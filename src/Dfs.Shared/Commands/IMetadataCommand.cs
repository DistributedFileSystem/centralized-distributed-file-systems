namespace Dfs.Shared.Commands;

/// <summary>Marker for a metadata change. Ids, timestamps, password hashes and chunk locations are decided before the command is submitted, so applying it on any node gives the same result.</summary>
public interface IMetadataCommand
{
    /// <summary>UTC time (Kind Utc) decided by the caller before the command is logged (the <c>at</c> field of an op log line).</summary>
    DateTime At { get; }
}
