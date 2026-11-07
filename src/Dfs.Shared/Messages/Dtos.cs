namespace Dfs.Shared.Messages;

/// <summary>Short user reference returned by Login.</summary>
/// <param name="Id">Id of the user.</param>
/// <param name="Username">Login name.</param>
public sealed record UserRef(string Id, string Username);

/// <summary>User as returned by the admin messages (never carries hash or salt).</summary>
/// <param name="Id">Id of the user.</param>
/// <param name="Username">Login name.</param>
/// <param name="Disabled">True when the user is disabled.</param>
/// <param name="GroupIds">Ids of the groups of the user.</param>
public sealed record UserInfo(string Id, string Username, bool Disabled, ValueList<string> GroupIds);

/// <summary>Group with its members.</summary>
/// <param name="Id">Id of the group.</param>
/// <param name="Name">Name of the group.</param>
/// <param name="MemberIds">Ids of the member users.</param>
public sealed record GroupInfo(string Id, string Name, ValueList<string> MemberIds);

/// <summary>Role with its permissions.</summary>
/// <param name="Id">Id of the role.</param>
/// <param name="Name">Name of the role.</param>
/// <param name="Scope">System or item scope.</param>
/// <param name="BuiltIn">True for a built-in role, which cannot be changed.</param>
/// <param name="Permissions">Permission names (see Dfs.Shared.Security.Permission).</param>
public sealed record RoleInfo(string Id, string Name, RoleScope Scope, bool BuiltIn, ValueList<string> Permissions);

/// <summary>Role granted to a user or group, system wide (Path and ItemId null) or on one item.</summary>
/// <param name="Id">Id of the grant.</param>
/// <param name="RoleId">Id of the granted role.</param>
/// <param name="SubjectType">User or group.</param>
/// <param name="SubjectId">Id of the user or group.</param>
/// <param name="Path">Path of the item; null for a system grant.</param>
/// <param name="ItemId">Id of the item; null for a system grant.</param>
public sealed record GrantInfo(string Id, string RoleId, SubjectType SubjectType, string SubjectId, string? Path, string? ItemId);

/// <summary>One audit log entry.</summary>
/// <param name="Seq">Increasing sequence number.</param>
/// <param name="Time">UTC time of the action.</param>
/// <param name="UserId">Id of the user who acted.</param>
/// <param name="Action">Name of the message.</param>
/// <param name="Target">Path or id the action was applied to.</param>
/// <param name="Allowed">True when the action was allowed.</param>
public sealed record AuditEntry(long Seq, DateTime Time, string UserId, string Action, string Target, bool Allowed);

/// <summary>State of a Data Server as seen by the Metadata Server.</summary>
/// <param name="NodeId">Id of the Data Server.</param>
/// <param name="Address">ADVERTISE_HOST:PORT of the Data Server.</param>
/// <param name="Status">Alive or Dead.</param>
/// <param name="CapacityBytes">Total capacity in bytes.</param>
/// <param name="FreeBytes">Free bytes.</param>
/// <param name="ChunkCount">Number of chunks stored.</param>
/// <param name="LastHeartbeat">UTC time of the last heartbeat.</param>
public sealed record DataServerInfo(string NodeId, string Address, DataServerStatus Status, long CapacityBytes, long FreeBytes, int ChunkCount, DateTime LastHeartbeat);

/// <summary>File or folder.</summary>
/// <param name="Id">Id of the item.</param>
/// <param name="Path">Absolute path.</param>
/// <param name="Name">Name of the last path segment.</param>
/// <param name="Kind">File or folder.</param>
/// <param name="Size">Size in bytes; 0 for a folder.</param>
/// <param name="OwnerId">Id of the owner user.</param>
/// <param name="CurrentVersion">Current version; 0 for a folder.</param>
/// <param name="ModifiedAt">UTC time of the last change.</param>
public sealed record ItemInfo(string Id, string Path, string Name, ItemKind Kind, long Size, string OwnerId, int CurrentVersion, DateTime ModifiedAt);

/// <summary>One retained version of a file.</summary>
/// <param name="Version">Version number starting at 1.</param>
/// <param name="Size">Size in bytes.</param>
/// <param name="CreatedBy">Id of the user who created the version.</param>
/// <param name="CreatedAt">UTC creation time.</param>
/// <param name="ChunkCount">Number of chunks.</param>
public sealed record VersionInfo(int Version, long Size, string CreatedBy, DateTime CreatedAt, int ChunkCount);

/// <summary>Chunk to write, with its write token.</summary>
/// <param name="Index">Zero-based index of the chunk in the file.</param>
/// <param name="ChunkId">Id of the chunk.</param>
/// <param name="Servers">Addresses of the Data Servers to write to (always a list).</param>
/// <param name="Token">Write token.</param>
public sealed record UploadChunk(int Index, string ChunkId, ValueList<string> Servers, string Token);

/// <summary>Chunk to read, with its read token.</summary>
/// <param name="Index">Zero-based index of the chunk in the file.</param>
/// <param name="ChunkId">Id of the chunk.</param>
/// <param name="Size">Size in bytes.</param>
/// <param name="Sha256">Lower-case hex SHA-256 of the chunk.</param>
/// <param name="Servers">Addresses of the Data Servers holding the chunk (always a list).</param>
/// <param name="Token">Read token.</param>
public sealed record OpenChunk(int Index, string ChunkId, long Size, string Sha256, ValueList<string> Servers, string Token);

/// <summary>Size and SHA-256 of an uploaded chunk.</summary>
/// <param name="ChunkId">Id of the chunk.</param>
/// <param name="Size">Size in bytes.</param>
/// <param name="Sha256">Lower-case hex SHA-256 of the chunk.</param>
public sealed record ChecksumInfo(string ChunkId, long Size, string Sha256);

/// <summary>Chunk held by a Data Server.</summary>
/// <param name="ChunkId">Id of the chunk.</param>
/// <param name="Size">Size in bytes.</param>
/// <param name="Sha256">Lower-case hex SHA-256 of the chunk.</param>
public sealed record ChunkInfo(string ChunkId, long Size, string Sha256);
