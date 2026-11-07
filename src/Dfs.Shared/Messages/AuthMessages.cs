namespace Dfs.Shared.Messages;

/// <summary>First message of every connection (0x01).</summary>
/// <param name="RequestId">Correlation id (GUID string).</param>
/// <param name="ProtocolVersion">Protocol version, currently 1.</param>
/// <param name="NodeType">Kind of the sending node.</param>
public sealed record Hello(string RequestId, int ProtocolVersion, NodeType NodeType);

/// <summary>Response to <see cref="Hello"/> (0x81).</summary>
/// <param name="RequestId">Correlation id of the request.</param>
/// <param name="ProtocolVersion">Protocol version of the answering node.</param>
/// <param name="NodeType">Kind of the answering node.</param>
/// <param name="NodeId">Id of the answering node.</param>
public sealed record HelloResponse(string RequestId, int ProtocolVersion, NodeType NodeType, string NodeId);

/// <summary>Log in with username and password (0x10); needs no session.</summary>
/// <param name="RequestId">Correlation id (GUID string).</param>
/// <param name="Username">Login name.</param>
/// <param name="Password">Plain password (stage 1 uses plain TCP in the LAN).</param>
public sealed record Login(string RequestId, string Username, string Password);

/// <summary>Response to <see cref="Login"/> (0x90).</summary>
/// <param name="RequestId">Correlation id of the request.</param>
/// <param name="Session">Session token for later requests.</param>
/// <param name="ExpiresAt">UTC time the session expires.</param>
/// <param name="User">The logged-in user.</param>
/// <param name="SystemRoles">Names of the system roles of the user.</param>
/// <param name="Permissions">System permissions of the user.</param>
public sealed record LoginResponse(string RequestId, string Session, DateTime ExpiresAt, UserRef User, ValueList<string> SystemRoles, ValueList<string> Permissions);

/// <summary>Delete the session (0x11).</summary>
/// <param name="RequestId">Correlation id (GUID string).</param>
/// <param name="Session">Session token to delete.</param>
public sealed record Logout(string RequestId, string Session);

/// <summary>Response to <see cref="Logout"/> (0x91).</summary>
/// <param name="RequestId">Correlation id of the request.</param>
public sealed record LogoutResponse(string RequestId);
