namespace Dfs.Shared.Messages;

/// <summary>Frame <c>type</c> byte: request ids 0x01-0x7F, response = request | 0x80, 0xFF = Error.</summary>
public enum MessageType : byte
{
    /// <summary>Request <c>Hello</c>.</summary>
    Hello = 0x01,
    /// <summary>Response to <c>Hello</c>.</summary>
    HelloResponse = 0x81,
    /// <summary>Request <c>Login</c>.</summary>
    Login = 0x10,
    /// <summary>Response to <c>Login</c>.</summary>
    LoginResponse = 0x90,
    /// <summary>Request <c>Logout</c>.</summary>
    Logout = 0x11,
    /// <summary>Response to <c>Logout</c>.</summary>
    LogoutResponse = 0x91,
    /// <summary>Request <c>ListDir</c>.</summary>
    ListDir = 0x20,
    /// <summary>Response to <c>ListDir</c>.</summary>
    ListDirResponse = 0xA0,
    /// <summary>Request <c>Stat</c>.</summary>
    Stat = 0x21,
    /// <summary>Response to <c>Stat</c>.</summary>
    StatResponse = 0xA1,
    /// <summary>Request <c>ListVersions</c>.</summary>
    ListVersions = 0x22,
    /// <summary>Response to <c>ListVersions</c>.</summary>
    ListVersionsResponse = 0xA2,
    /// <summary>Request <c>CreateFolder</c>.</summary>
    CreateFolder = 0x23,
    /// <summary>Response to <c>CreateFolder</c>.</summary>
    CreateFolderResponse = 0xA3,
    /// <summary>Request <c>Rename</c>.</summary>
    Rename = 0x24,
    /// <summary>Response to <c>Rename</c>.</summary>
    RenameResponse = 0xA4,
    /// <summary>Request <c>Move</c>.</summary>
    Move = 0x25,
    /// <summary>Response to <c>Move</c>.</summary>
    MoveResponse = 0xA5,
    /// <summary>Request <c>DeleteItem</c>.</summary>
    DeleteItem = 0x26,
    /// <summary>Response to <c>DeleteItem</c>.</summary>
    DeleteItemResponse = 0xA6,
    /// <summary>Request <c>CreateUpload</c>.</summary>
    CreateUpload = 0x30,
    /// <summary>Response to <c>CreateUpload</c>.</summary>
    CreateUploadResponse = 0xB0,
    /// <summary>Request <c>CommitUpload</c>.</summary>
    CommitUpload = 0x31,
    /// <summary>Response to <c>CommitUpload</c>.</summary>
    CommitUploadResponse = 0xB1,
    /// <summary>Request <c>AbortUpload</c>.</summary>
    AbortUpload = 0x32,
    /// <summary>Response to <c>AbortUpload</c>.</summary>
    AbortUploadResponse = 0xB2,
    /// <summary>Request <c>Open</c>.</summary>
    Open = 0x33,
    /// <summary>Response to <c>Open</c>.</summary>
    OpenResponse = 0xB3,
    /// <summary>Request <c>WriteChunk</c>.</summary>
    WriteChunk = 0x40,
    /// <summary>Response to <c>WriteChunk</c>.</summary>
    WriteChunkResponse = 0xC0,
    /// <summary>Request <c>ReadChunk</c>.</summary>
    ReadChunk = 0x41,
    /// <summary>Response to <c>ReadChunk</c>.</summary>
    ReadChunkResponse = 0xC1,
    /// <summary>Request <c>DeleteChunk</c>.</summary>
    DeleteChunk = 0x42,
    /// <summary>Response to <c>DeleteChunk</c>.</summary>
    DeleteChunkResponse = 0xC2,
    /// <summary>Request <c>Register</c>.</summary>
    Register = 0x50,
    /// <summary>Response to <c>Register</c>.</summary>
    RegisterResponse = 0xD0,
    /// <summary>Request <c>Heartbeat</c>.</summary>
    Heartbeat = 0x51,
    /// <summary>Response to <c>Heartbeat</c>.</summary>
    HeartbeatResponse = 0xD1,
    /// <summary>Request <c>ChunkReport</c>.</summary>
    ChunkReport = 0x52,
    /// <summary>Response to <c>ChunkReport</c>.</summary>
    ChunkReportResponse = 0xD2,
    /// <summary>Request <c>CreateUser</c>.</summary>
    CreateUser = 0x60,
    /// <summary>Response to <c>CreateUser</c>.</summary>
    CreateUserResponse = 0xE0,
    /// <summary>Request <c>ListUsers</c>.</summary>
    ListUsers = 0x61,
    /// <summary>Response to <c>ListUsers</c>.</summary>
    ListUsersResponse = 0xE1,
    /// <summary>Request <c>SetUserDisabled</c>.</summary>
    SetUserDisabled = 0x62,
    /// <summary>Response to <c>SetUserDisabled</c>.</summary>
    SetUserDisabledResponse = 0xE2,
    /// <summary>Request <c>ResetPassword</c>.</summary>
    ResetPassword = 0x63,
    /// <summary>Response to <c>ResetPassword</c>.</summary>
    ResetPasswordResponse = 0xE3,
    /// <summary>Request <c>CreateGroup</c>.</summary>
    CreateGroup = 0x64,
    /// <summary>Response to <c>CreateGroup</c>.</summary>
    CreateGroupResponse = 0xE4,
    /// <summary>Request <c>ListGroups</c>.</summary>
    ListGroups = 0x65,
    /// <summary>Response to <c>ListGroups</c>.</summary>
    ListGroupsResponse = 0xE5,
    /// <summary>Request <c>SetGroupMember</c>.</summary>
    SetGroupMember = 0x66,
    /// <summary>Response to <c>SetGroupMember</c>.</summary>
    SetGroupMemberResponse = 0xE6,
    /// <summary>Request <c>CreateRole</c>.</summary>
    CreateRole = 0x67,
    /// <summary>Response to <c>CreateRole</c>.</summary>
    CreateRoleResponse = 0xE7,
    /// <summary>Request <c>ListRoles</c>.</summary>
    ListRoles = 0x68,
    /// <summary>Response to <c>ListRoles</c>.</summary>
    ListRolesResponse = 0xE8,
    /// <summary>Request <c>UpdateRole</c>.</summary>
    UpdateRole = 0x69,
    /// <summary>Response to <c>UpdateRole</c>.</summary>
    UpdateRoleResponse = 0xE9,
    /// <summary>Request <c>CreateGrant</c>.</summary>
    CreateGrant = 0x6A,
    /// <summary>Response to <c>CreateGrant</c>.</summary>
    CreateGrantResponse = 0xEA,
    /// <summary>Request <c>ListGrants</c>.</summary>
    ListGrants = 0x6B,
    /// <summary>Response to <c>ListGrants</c>.</summary>
    ListGrantsResponse = 0xEB,
    /// <summary>Request <c>RevokeGrant</c>.</summary>
    RevokeGrant = 0x6C,
    /// <summary>Response to <c>RevokeGrant</c>.</summary>
    RevokeGrantResponse = 0xEC,
    /// <summary>Request <c>ListAudit</c>.</summary>
    ListAudit = 0x6D,
    /// <summary>Response to <c>ListAudit</c>.</summary>
    ListAuditResponse = 0xED,
    /// <summary>Request <c>ClusterStatus</c>.</summary>
    ClusterStatus = 0x6E,
    /// <summary>Response to <c>ClusterStatus</c>.</summary>
    ClusterStatusResponse = 0xEE,
    /// <summary>Stage 2 request <c>ForwardChunk</c> (fields not defined yet).</summary>
    ForwardChunk = 0x70,
    /// <summary>Stage 2 request <c>CopyChunk</c> (fields not defined yet).</summary>
    CopyChunk = 0x71,
    /// <summary>Error response that replaces any normal response.</summary>
    Error = 0xFF,
}
