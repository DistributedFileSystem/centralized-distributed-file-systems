namespace Dfs.Shared.Messages;

/// <summary>List the children of a folder (0x20, permission item.list).</summary>
/// <param name="RequestId">Correlation id (GUID string).</param>
/// <param name="Session">Session token.</param>
/// <param name="Path">Absolute path of the folder.</param>
public sealed record ListDir(string RequestId, string Session, string Path);

/// <summary>Response to <see cref="ListDir"/> (0xA0).</summary>
/// <param name="RequestId">Correlation id of the request.</param>
/// <param name="Items">Children the user may list.</param>
public sealed record ListDirResponse(string RequestId, ValueList<ItemInfo> Items);

/// <summary>Get one item (0x21, permission item.list).</summary>
/// <param name="RequestId">Correlation id (GUID string).</param>
/// <param name="Session">Session token.</param>
/// <param name="Path">Absolute path of the item.</param>
public sealed record Stat(string RequestId, string Session, string Path);

/// <summary>Response to <see cref="Stat"/> (0xA1).</summary>
/// <param name="RequestId">Correlation id of the request.</param>
/// <param name="Item">The item.</param>
public sealed record StatResponse(string RequestId, ItemInfo Item);

/// <summary>List retained versions of a file (0x22, permission file.read).</summary>
/// <param name="RequestId">Correlation id (GUID string).</param>
/// <param name="Session">Session token.</param>
/// <param name="Path">Absolute path of the file.</param>
public sealed record ListVersions(string RequestId, string Session, string Path);

/// <summary>Response to <see cref="ListVersions"/> (0xA2).</summary>
/// <param name="RequestId">Correlation id of the request.</param>
/// <param name="Versions">Retained versions, newest first.</param>
public sealed record ListVersionsResponse(string RequestId, ValueList<VersionInfo> Versions);

/// <summary>Create a folder (0x23, permission item.create on the parent).</summary>
/// <param name="RequestId">Correlation id (GUID string).</param>
/// <param name="Session">Session token.</param>
/// <param name="Path">Absolute path of the new folder.</param>
public sealed record CreateFolder(string RequestId, string Session, string Path);

/// <summary>Response to <see cref="CreateFolder"/> (0xA3).</summary>
/// <param name="RequestId">Correlation id of the request.</param>
/// <param name="Item">The new folder.</param>
public sealed record CreateFolderResponse(string RequestId, ItemInfo Item);

/// <summary>Rename an item (0x24, permission item.rename).</summary>
/// <param name="RequestId">Correlation id (GUID string).</param>
/// <param name="Session">Session token.</param>
/// <param name="Path">Absolute path of the item.</param>
/// <param name="NewName">New name of the item.</param>
public sealed record Rename(string RequestId, string Session, string Path, string NewName);

/// <summary>Response to <see cref="Rename"/> (0xA4).</summary>
/// <param name="RequestId">Correlation id of the request.</param>
/// <param name="Item">The renamed item.</param>
public sealed record RenameResponse(string RequestId, ItemInfo Item);

/// <summary>Move an item to another folder (0x25, item.rename on the item, item.create on the destination).</summary>
/// <param name="RequestId">Correlation id (GUID string).</param>
/// <param name="Session">Session token.</param>
/// <param name="Path">Absolute path of the item.</param>
/// <param name="NewParentPath">Absolute path of the destination folder.</param>
public sealed record Move(string RequestId, string Session, string Path, string NewParentPath);

/// <summary>Response to <see cref="Move"/> (0xA5).</summary>
/// <param name="RequestId">Correlation id of the request.</param>
/// <param name="Item">The moved item.</param>
public sealed record MoveResponse(string RequestId, ItemInfo Item);

/// <summary>Delete a file or a folder tree (0x26, permission item.delete).</summary>
/// <param name="RequestId">Correlation id (GUID string).</param>
/// <param name="Session">Session token.</param>
/// <param name="Path">Absolute path of the item.</param>
public sealed record DeleteItem(string RequestId, string Session, string Path);

/// <summary>Response to <see cref="DeleteItem"/> (0xA6).</summary>
/// <param name="RequestId">Correlation id of the request.</param>
public sealed record DeleteItemResponse(string RequestId);
