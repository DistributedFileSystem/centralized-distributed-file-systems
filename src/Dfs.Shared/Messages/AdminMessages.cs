namespace Dfs.Shared.Messages;

/// <summary>Create a user (0x60, permission user.manage).</summary>
/// <param name="RequestId">Correlation id (GUID string).</param>
/// <param name="Session">Session token.</param>
/// <param name="Username">Login name of the new user.</param>
/// <param name="Password">Plain password of the new user.</param>
public sealed record CreateUser(string RequestId, string Session, string Username, string Password);

/// <summary>Response to <see cref="CreateUser"/> (0xE0).</summary>
/// <param name="RequestId">Correlation id of the request.</param>
/// <param name="User">The new user.</param>
public sealed record CreateUserResponse(string RequestId, UserInfo User);

/// <summary>List users (0x61, permission user.manage).</summary>
/// <param name="RequestId">Correlation id (GUID string).</param>
/// <param name="Session">Session token.</param>
public sealed record ListUsers(string RequestId, string Session);

/// <summary>Response to <see cref="ListUsers"/> (0xE1).</summary>
/// <param name="RequestId">Correlation id of the request.</param>
/// <param name="Users">All users.</param>
public sealed record ListUsersResponse(string RequestId, ValueList<UserInfo> Users);

/// <summary>Disable or enable a user (0x62, permission user.manage).</summary>
/// <param name="RequestId">Correlation id (GUID string).</param>
/// <param name="Session">Session token.</param>
/// <param name="UserId">Id of the user.</param>
/// <param name="Disabled">True to disable, false to enable.</param>
public sealed record SetUserDisabled(string RequestId, string Session, string UserId, bool Disabled);

/// <summary>Response to <see cref="SetUserDisabled"/> (0xE2).</summary>
/// <param name="RequestId">Correlation id of the request.</param>
public sealed record SetUserDisabledResponse(string RequestId);

/// <summary>Reset the password of a user (0x63, permission user.manage).</summary>
/// <param name="RequestId">Correlation id (GUID string).</param>
/// <param name="Session">Session token.</param>
/// <param name="UserId">Id of the user.</param>
/// <param name="NewPassword">The new plain password.</param>
public sealed record ResetPassword(string RequestId, string Session, string UserId, string NewPassword);

/// <summary>Response to <see cref="ResetPassword"/> (0xE3).</summary>
/// <param name="RequestId">Correlation id of the request.</param>
public sealed record ResetPasswordResponse(string RequestId);

/// <summary>Create a group (0x64, permission group.manage).</summary>
/// <param name="RequestId">Correlation id (GUID string).</param>
/// <param name="Session">Session token.</param>
/// <param name="Name">Name of the new group.</param>
public sealed record CreateGroup(string RequestId, string Session, string Name);

/// <summary>Response to <see cref="CreateGroup"/> (0xE4).</summary>
/// <param name="RequestId">Correlation id of the request.</param>
/// <param name="Group">The new group.</param>
public sealed record CreateGroupResponse(string RequestId, GroupInfo Group);

/// <summary>List groups (0x65, permission group.manage).</summary>
/// <param name="RequestId">Correlation id (GUID string).</param>
/// <param name="Session">Session token.</param>
public sealed record ListGroups(string RequestId, string Session);

/// <summary>Response to <see cref="ListGroups"/> (0xE5).</summary>
/// <param name="RequestId">Correlation id of the request.</param>
/// <param name="Groups">All groups.</param>
public sealed record ListGroupsResponse(string RequestId, ValueList<GroupInfo> Groups);

/// <summary>Add or remove a group member (0x66, permission group.manage).</summary>
/// <param name="RequestId">Correlation id (GUID string).</param>
/// <param name="Session">Session token.</param>
/// <param name="GroupId">Id of the group.</param>
/// <param name="UserId">Id of the user.</param>
/// <param name="Member">True to add the user, false to remove.</param>
public sealed record SetGroupMember(string RequestId, string Session, string GroupId, string UserId, bool Member);

/// <summary>Response to <see cref="SetGroupMember"/> (0xE6).</summary>
/// <param name="RequestId">Correlation id of the request.</param>
public sealed record SetGroupMemberResponse(string RequestId);

/// <summary>Create a role (0x67, permission role.manage).</summary>
/// <param name="RequestId">Correlation id (GUID string).</param>
/// <param name="Session">Session token.</param>
/// <param name="Name">Name of the new role.</param>
/// <param name="Scope">System or item scope.</param>
/// <param name="Permissions">Permission names; must all belong to the scope.</param>
public sealed record CreateRole(string RequestId, string Session, string Name, RoleScope Scope, ValueList<string> Permissions);

/// <summary>Response to <see cref="CreateRole"/> (0xE7).</summary>
/// <param name="RequestId">Correlation id of the request.</param>
/// <param name="Role">The new role.</param>
public sealed record CreateRoleResponse(string RequestId, RoleInfo Role);

/// <summary>List roles (0x68, permission role.manage).</summary>
/// <param name="RequestId">Correlation id (GUID string).</param>
/// <param name="Session">Session token.</param>
public sealed record ListRoles(string RequestId, string Session);

/// <summary>Response to <see cref="ListRoles"/> (0xE8).</summary>
/// <param name="RequestId">Correlation id of the request.</param>
/// <param name="Roles">All roles.</param>
public sealed record ListRolesResponse(string RequestId, ValueList<RoleInfo> Roles);

/// <summary>Replace the permissions of a role (0x69, permission role.manage).</summary>
/// <param name="RequestId">Correlation id (GUID string).</param>
/// <param name="Session">Session token.</param>
/// <param name="RoleId">Id of the role; built-in roles are rejected with Forbidden.</param>
/// <param name="Permissions">The new permission names.</param>
public sealed record UpdateRole(string RequestId, string Session, string RoleId, ValueList<string> Permissions);

/// <summary>Response to <see cref="UpdateRole"/> (0xE9).</summary>
/// <param name="RequestId">Correlation id of the request.</param>
/// <param name="Role">The updated role.</param>
public sealed record UpdateRoleResponse(string RequestId, RoleInfo Role);

/// <summary>Grant a role to a user or group on a path, or system wide when Path is null (0x6A).</summary>
/// <param name="RequestId">Correlation id (GUID string).</param>
/// <param name="Session">Session token.</param>
/// <param name="RoleId">Id of the role to grant.</param>
/// <param name="SubjectType">User or group.</param>
/// <param name="SubjectId">Id of the user or group.</param>
/// <param name="Path">Absolute path; null for a system grant.</param>
public sealed record CreateGrant(string RequestId, string Session, string RoleId, SubjectType SubjectType, string SubjectId, string? Path = null);

/// <summary>Response to <see cref="CreateGrant"/> (0xEA).</summary>
/// <param name="RequestId">Correlation id of the request.</param>
/// <param name="Grant">The new grant.</param>
public sealed record CreateGrantResponse(string RequestId, GrantInfo Grant);

/// <summary>List grants on a path, or system wide when Path is null (0x6B).</summary>
/// <param name="RequestId">Correlation id (GUID string).</param>
/// <param name="Session">Session token.</param>
/// <param name="Path">Absolute path; null for system grants.</param>
/// <param name="SubjectId">Only grants of this user or group.</param>
public sealed record ListGrants(string RequestId, string Session, string? Path = null, string? SubjectId = null);

/// <summary>Response to <see cref="ListGrants"/> (0xEB).</summary>
/// <param name="RequestId">Correlation id of the request.</param>
/// <param name="Grants">Matching grants.</param>
public sealed record ListGrantsResponse(string RequestId, ValueList<GrantInfo> Grants);

/// <summary>Revoke a grant (0x6C).</summary>
/// <param name="RequestId">Correlation id (GUID string).</param>
/// <param name="Session">Session token.</param>
/// <param name="GrantId">Id of the grant.</param>
public sealed record RevokeGrant(string RequestId, string Session, string GrantId);

/// <summary>Response to <see cref="RevokeGrant"/> (0xEC).</summary>
/// <param name="RequestId">Correlation id of the request.</param>
public sealed record RevokeGrantResponse(string RequestId);

/// <summary>List audit entries (0x6D, permission audit.view).</summary>
/// <param name="RequestId">Correlation id (GUID string).</param>
/// <param name="Session">Session token.</param>
/// <param name="From">Inclusive lower time bound.</param>
/// <param name="To">Inclusive upper time bound.</param>
/// <param name="UserId">Only entries of this user.</param>
/// <param name="Limit">Maximum number of entries (default 100, max 1000).</param>
public sealed record ListAudit(string RequestId, string Session, DateTime? From = null, DateTime? To = null, string? UserId = null, int Limit = 100);

/// <summary>Response to <see cref="ListAudit"/> (0xED).</summary>
/// <param name="RequestId">Correlation id of the request.</param>
/// <param name="Entries">Matching entries.</param>
public sealed record ListAuditResponse(string RequestId, ValueList<AuditEntry> Entries);

/// <summary>List Data Servers (0x6E, permission cluster.view).</summary>
/// <param name="RequestId">Correlation id (GUID string).</param>
/// <param name="Session">Session token.</param>
public sealed record ClusterStatus(string RequestId, string Session);

/// <summary>Response to <see cref="ClusterStatus"/> (0xEE).</summary>
/// <param name="RequestId">Correlation id of the request.</param>
/// <param name="DataServers">All registered Data Servers.</param>
public sealed record ClusterStatusResponse(string RequestId, ValueList<DataServerInfo> DataServers);
