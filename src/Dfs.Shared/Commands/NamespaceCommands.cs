using Dfs.Shared.Messages;

namespace Dfs.Shared.Commands;

/// <summary>Create a folder.</summary>
/// <param name="ItemId">Id of the new folder.</param>
/// <param name="ParentId">Id of the parent folder.</param>
/// <param name="Name">Name of the folder.</param>
/// <param name="OwnerId">Id of the creator, who becomes Owner.</param>
/// <param name="At">UTC time decided before logging.</param>
public sealed record CreateFolder(string ItemId, string ParentId, string Name, string OwnerId, DateTime At) : IMetadataCommand;

/// <summary>Rename an item.</summary>
/// <param name="ItemId">Id of the item.</param>
/// <param name="NewName">New name.</param>
/// <param name="At">UTC time decided before logging.</param>
public sealed record RenameItem(string ItemId, string NewName, DateTime At) : IMetadataCommand;

/// <summary>Move an item under another folder.</summary>
/// <param name="ItemId">Id of the item.</param>
/// <param name="NewParentId">Id of the destination folder.</param>
/// <param name="At">UTC time decided before logging.</param>
public sealed record MoveItem(string ItemId, string NewParentId, DateTime At) : IMetadataCommand;

/// <summary>Delete an item and its subtree.</summary>
/// <param name="ItemId">Id of the item.</param>
/// <param name="At">UTC time decided before logging.</param>
public sealed record DeleteItem(string ItemId, DateTime At) : IMetadataCommand;

/// <summary>Start an upload with the chunk ids and locations decided beforehand.</summary>
/// <param name="UploadId">Id of the upload.</param>
/// <param name="ItemId">Id of the file (existing, or the id the new file will get).</param>
/// <param name="ParentId">Id of the parent folder.</param>
/// <param name="Name">Name of the file.</param>
/// <param name="CreatedBy">Id of the user who starts the upload.</param>
/// <param name="Size">Size in bytes.</param>
/// <param name="BaseVersion">Version the change is based on; 0 for a new file.</param>
/// <param name="ChunkSize">Chunk size in bytes.</param>
/// <param name="Chunks">Chunks with their locations.</param>
/// <param name="At">UTC time decided before logging.</param>
public sealed record BeginUpload(string UploadId, string ItemId, string ParentId, string Name, string CreatedBy, long Size, int BaseVersion, int ChunkSize, ValueList<PlannedChunk> Chunks, DateTime At) : IMetadataCommand;

/// <summary>Commit an upload as a new version; the result is Conflict when the current version differs from BaseVersion.</summary>
/// <param name="UploadId">Id of the upload.</param>
/// <param name="Checksums">One checksum per chunk.</param>
/// <param name="KeepVersions">Number of versions to keep; older ones are dropped.</param>
/// <param name="At">UTC time decided before logging.</param>
public sealed record CommitUpload(string UploadId, ValueList<ChecksumInfo> Checksums, int KeepVersions, DateTime At) : IMetadataCommand;

/// <summary>Abort an upload.</summary>
/// <param name="UploadId">Id of the upload.</param>
/// <param name="At">UTC time decided before logging.</param>
public sealed record AbortUpload(string UploadId, DateTime At) : IMetadataCommand;
