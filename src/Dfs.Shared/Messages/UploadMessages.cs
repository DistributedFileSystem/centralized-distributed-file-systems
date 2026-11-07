namespace Dfs.Shared.Messages;

/// <summary>Start an upload of a new file or a new version (0x30).</summary>
/// <param name="RequestId">Correlation id (GUID string).</param>
/// <param name="Session">Session token.</param>
/// <param name="Path">Absolute path of the file.</param>
/// <param name="Size">Size of the file in bytes.</param>
/// <param name="BaseVersion">Version the change is based on; 0 for a new file.</param>
public sealed record CreateUpload(string RequestId, string Session, string Path, long Size, int BaseVersion);

/// <summary>Response to <see cref="CreateUpload"/> (0xB0).</summary>
/// <param name="RequestId">Correlation id of the request.</param>
/// <param name="UploadId">Id of the upload.</param>
/// <param name="ChunkSize">Chunk size in bytes.</param>
/// <param name="Chunks">Chunks to write, each with locations and a write token.</param>
public sealed record CreateUploadResponse(string RequestId, string UploadId, int ChunkSize, ValueList<UploadChunk> Chunks);

/// <summary>Commit an upload after all chunks are written (0x31).</summary>
/// <param name="RequestId">Correlation id (GUID string).</param>
/// <param name="Session">Session token.</param>
/// <param name="UploadId">Id of the upload.</param>
/// <param name="Checksums">One checksum per chunk of the upload.</param>
public sealed record CommitUpload(string RequestId, string Session, string UploadId, ValueList<ChecksumInfo> Checksums);

/// <summary>Response to <see cref="CommitUpload"/> (0xB1).</summary>
/// <param name="RequestId">Correlation id of the request.</param>
/// <param name="Item">The file after the commit.</param>
/// <param name="Version">The new version number.</param>
public sealed record CommitUploadResponse(string RequestId, ItemInfo Item, int Version);

/// <summary>Abort an upload (0x32).</summary>
/// <param name="RequestId">Correlation id (GUID string).</param>
/// <param name="Session">Session token.</param>
/// <param name="UploadId">Id of the upload.</param>
public sealed record AbortUpload(string RequestId, string Session, string UploadId);

/// <summary>Response to <see cref="AbortUpload"/> (0xB2).</summary>
/// <param name="RequestId">Correlation id of the request.</param>
public sealed record AbortUploadResponse(string RequestId);

/// <summary>Open a version of a file for reading (0x33, permission file.read).</summary>
/// <param name="RequestId">Correlation id (GUID string).</param>
/// <param name="Session">Session token.</param>
/// <param name="Path">Absolute path of the file.</param>
/// <param name="Version">Version to open; 0 means the current version.</param>
public sealed record Open(string RequestId, string Session, string Path, int Version = 0);

/// <summary>Response to <see cref="Open"/> (0xB3).</summary>
/// <param name="RequestId">Correlation id of the request.</param>
/// <param name="Item">The file.</param>
/// <param name="Version">The version opened.</param>
/// <param name="Size">Size of the version in bytes.</param>
/// <param name="ChunkSize">Chunk size in bytes.</param>
/// <param name="Chunks">Chunks to read, each with locations and a read token.</param>
public sealed record OpenResponse(string RequestId, ItemInfo Item, int Version, long Size, int ChunkSize, ValueList<OpenChunk> Chunks);
