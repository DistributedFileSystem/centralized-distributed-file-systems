using Dfs.Shared.Commands;
using Dfs.Shared.Messages;

namespace Dfs.Shared.Metadata;

/// <summary>User with the stored credentials, for login checks only; never sent over the wire.</summary>
/// <param name="Id">Id of the user.</param>
/// <param name="Username">Login name.</param>
/// <param name="PasswordHash">PBKDF2-SHA256 hash.</param>
/// <param name="Salt">Salt of the hash.</param>
/// <param name="Disabled">True when the user is disabled.</param>
public sealed record UserCredentials(string Id, string Username, string PasswordHash, string Salt, bool Disabled);

/// <summary>Stored session.</summary>
/// <param name="Token">The session token.</param>
/// <param name="UserId">Id of the user.</param>
/// <param name="ExpiresAt">UTC time the session expires.</param>
public sealed record SessionRecord(string Token, string UserId, DateTime ExpiresAt);

/// <summary>Chunk of a committed file version, with its locations.</summary>
/// <param name="Index">Zero-based index of the chunk in the file.</param>
/// <param name="ChunkId">Id of the chunk.</param>
/// <param name="Size">Size in bytes.</param>
/// <param name="Sha256">Lower-case hex SHA-256.</param>
/// <param name="Servers">Addresses of the Data Servers holding the chunk (always a list).</param>
public sealed record ChunkRecord(int Index, string ChunkId, long Size, string Sha256, ValueList<string> Servers);

/// <summary>Upload that was started and not yet committed or aborted.</summary>
/// <param name="UploadId">Id of the upload.</param>
/// <param name="ItemId">Id of the file.</param>
/// <param name="ParentId">Id of the parent folder.</param>
/// <param name="Name">Name of the file.</param>
/// <param name="CreatedBy">Id of the user who started the upload.</param>
/// <param name="Size">Size in bytes.</param>
/// <param name="BaseVersion">Version the change is based on; 0 for a new file.</param>
/// <param name="ChunkSize">Chunk size in bytes.</param>
/// <param name="Chunks">Planned chunks with locations.</param>
public sealed record UploadRecord(string UploadId, string ItemId, string ParentId, string Name, string CreatedBy, long Size, int BaseVersion, int ChunkSize, ValueList<PlannedChunk> Chunks);
