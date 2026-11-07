namespace Dfs.Shared.Messages;

/// <summary>Kind of node that sends or answers a <c>Hello</c>.</summary>
public enum NodeType
{
    /// <summary>A client (SDK or desktop app).</summary>
    Client,
    /// <summary>A Metadata Server.</summary>
    Metadata,
    /// <summary>A Data Server.</summary>
    Data,
}

/// <summary>Kind of a namespace item.</summary>
public enum ItemKind
{
    /// <summary>A file.</summary>
    File,
    /// <summary>A folder.</summary>
    Folder,
}

/// <summary>Scope of a role.</summary>
public enum RoleScope
{
    /// <summary>Contains system permissions only.</summary>
    System,
    /// <summary>Contains folder and file permissions only.</summary>
    Item,
}

/// <summary>Kind of the subject of a grant.</summary>
public enum SubjectType
{
    /// <summary>A single user.</summary>
    User,
    /// <summary>A group; applies to all its members.</summary>
    Group,
}

/// <summary>Liveness of a Data Server.</summary>
public enum DataServerStatus
{
    /// <summary>Heartbeats are arriving.</summary>
    Alive,
    /// <summary>Three heartbeats in a row were missed.</summary>
    Dead,
}

/// <summary>Error code carried by <see cref="ErrorResponse"/> and <see cref="DfsException"/>.</summary>
public enum ErrorCode
{
    /// <summary>Not logged in, wrong username or password, or session expired.</summary>
    Unauthorized,
    /// <summary>The user lacks the required permission.</summary>
    Forbidden,
    /// <summary>Path, upload, version or chunk does not exist.</summary>
    NotFound,
    /// <summary>The name already exists in the folder.</summary>
    AlreadyExists,
    /// <summary>The file changed since <c>baseVersion</c>.</summary>
    Conflict,
    /// <summary>Malformed request, invalid path, missing field, message before Hello, or size over the limit.</summary>
    BadRequest,
    /// <summary>The data token has a wrong signature, operation or chunk id.</summary>
    TokenInvalid,
    /// <summary>The data token is expired.</summary>
    TokenExpired,
    /// <summary>A required Data Server is dead or too few are alive.</summary>
    Unavailable,
    /// <summary>Wrong protocol version.</summary>
    VersionMismatch,
    /// <summary>Unexpected server error; the client may retry.</summary>
    Internal,
    /// <summary>Stage 2: the node is not the leader; <c>details.leader</c> holds the leader address.</summary>
    NotLeader,
}
