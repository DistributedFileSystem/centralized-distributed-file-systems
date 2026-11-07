using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dfs.Tests.Contract;

/// <summary>
/// AC1 (TC2, TC3): every example in docs/protocol.md deserializes into the message record and serializes back to the same JSON.
/// Options per protocol.md – Frame format: camelCase, null omitted. Enum casing comes from the types themselves (no global enum converter).
/// </summary>
public class MessageJsonTests
{
    private const string Rid = "3f2504e0-4f89-41d3-9a0c-d0b6e6e8c1a0";

    private const string Item =
        """{ "id": "i-01H", "path": "/projects/dfs", "name": "dfs", "kind": "folder", "size": 0, "ownerId": "u-01H", "currentVersion": 0, "modifiedAt": "2026-11-20T09:15:02Z" }""";

    private const string FileItem =
        """{ "id": "i-01H", "path": "/projects/dfs/report.pdf", "name": "report.pdf", "kind": "file", "size": 10485760, "ownerId": "u-01H", "currentVersion": 4, "modifiedAt": "2026-11-20T09:15:02Z" }""";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static IEnumerable<object[]> Examples()
    {
        // docs/protocol.md – Handshake
        yield return Ex("Hello", """{ "requestId": "…", "protocolVersion": 1, "nodeType": "client" }""");
        yield return Ex("HelloResponse", """{ "requestId": "…", "protocolVersion": 1, "nodeType": "metadata", "nodeId": "m1" }""");

        // Auth
        yield return Ex("Login", """{ "requestId": "…", "username": "an.nguyen", "password": "secret" }""");
        yield return Ex("LoginResponse", """
            { "requestId": "…", "session": "…", "expiresAt": "2026-11-20T17:15:02Z",
              "user": { "id": "u-01H", "username": "an.nguyen" },
              "systemRoles": ["Admin"], "permissions": ["user.manage", "cluster.view"] }
            """);
        yield return Ex("Logout", """{ "requestId": "…", "session": "…" }""");
        yield return Ex("LogoutResponse", """{ "requestId": "…" }""");

        // Namespace
        yield return Ex("ListDir", """{ "requestId": "…", "session": "…", "path": "/projects" }""");
        yield return Ex("ListDirResponse", $$"""{ "requestId": "…", "items": [ {{Item}} ] }""");
        yield return Ex("Stat", """{ "requestId": "…", "session": "…", "path": "/projects/dfs/report.pdf" }""");
        yield return Ex("StatResponse", $$"""{ "requestId": "…", "item": {{FileItem}} }""");
        yield return Ex("ListVersions", """{ "requestId": "…", "session": "…", "path": "/projects/dfs/report.pdf" }""");
        yield return Ex("ListVersionsResponse", """
            { "requestId": "…", "versions": [
              { "version": 4, "size": 10485760, "createdBy": "u-01H", "createdAt": "2026-11-20T09:15:02Z", "chunkCount": 3 } ] }
            """);
        yield return Ex("CreateFolder", """{ "requestId": "…", "session": "…", "path": "/projects/dfs/docs" }""");
        yield return Ex("CreateFolderResponse", $$"""{ "requestId": "…", "item": {{Item}} }""");
        yield return Ex("Rename", """{ "requestId": "…", "session": "…", "path": "/projects/a.txt", "newName": "b.txt" }""");
        yield return Ex("RenameResponse", $$"""{ "requestId": "…", "item": {{Item}} }""");
        yield return Ex("Move", """{ "requestId": "…", "session": "…", "path": "/projects/a.txt", "newParentPath": "/archive" }""");
        yield return Ex("MoveResponse", $$"""{ "requestId": "…", "item": {{Item}} }""");
        yield return Ex("DeleteItem", """{ "requestId": "…", "session": "…", "path": "/projects/a.txt" }""");
        yield return Ex("DeleteItemResponse", """{ "requestId": "…" }""");

        // Upload and Open (TC3)
        yield return Ex("CreateUpload", """
            { "requestId": "…", "session": "…", "path": "/projects/dfs/report.pdf",
              "size": 10485760, "baseVersion": 3 }
            """);
        yield return Ex("CreateUploadResponse", """
            { "requestId": "…", "uploadId": "up-01H", "chunkSize": 4194304,
              "chunks": [
                { "index": 0, "chunkId": "c-01H", "servers": ["192.168.10.13:7001"], "token": "…" },
                { "index": 1, "chunkId": "c-02H", "servers": ["192.168.10.15:7001"], "token": "…" },
                { "index": 2, "chunkId": "c-03H", "servers": ["192.168.10.11:7001"], "token": "…" }
              ] }
            """);
        yield return Ex("CommitUpload", """
            { "requestId": "…", "session": "…", "uploadId": "up-01H",
              "checksums": [ { "chunkId": "c-01H", "size": 4194304, "sha256": "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08" } ] }
            """);
        yield return Ex("CommitUploadResponse", $$"""{ "requestId": "…", "item": {{FileItem}}, "version": 4 }""");
        yield return Ex("AbortUpload", """{ "requestId": "…", "session": "…", "uploadId": "up-01H" }""");
        yield return Ex("AbortUploadResponse", """{ "requestId": "…" }""");
        yield return Ex("Open", """{ "requestId": "…", "session": "…", "path": "/projects/dfs/report.pdf", "version": 4 }""");
        yield return Ex("OpenResponse", $$"""
            { "requestId": "…", "item": {{FileItem}}, "version": 4, "size": 10485760, "chunkSize": 4194304,
              "chunks": [ { "index": 0, "chunkId": "c-01H", "size": 4194304, "sha256": "9f86d0",
                            "servers": ["192.168.10.13:7001"], "token": "…" } ] }
            """);

        // Chunk (JSON header only; raw bytes are framed separately)
        yield return Ex("WriteChunk", """{ "requestId": "…", "chunkId": "c-01H", "token": "…", "size": 4194304 }""");
        yield return Ex("WriteChunkResponse", """{ "requestId": "…", "chunkId": "c-01H", "size": 4194304, "sha256": "9f86d0" }""");
        yield return Ex("ReadChunk", """{ "requestId": "…", "chunkId": "c-01H", "token": "…" }""");
        yield return Ex("ReadChunkResponse", """{ "requestId": "…", "chunkId": "c-01H", "size": 4194304, "sha256": "9f86d0" }""");
        yield return Ex("DeleteChunk", """{ "requestId": "…", "chunkIds": ["c-01H"] }""");
        yield return Ex("DeleteChunkResponse", """{ "requestId": "…", "deleted": 1 }""");

        // Cluster
        yield return Ex("Register", """{ "requestId": "…", "nodeId": "d1", "address": "192.168.10.13:7001", "capacityBytes": 107374182400, "freeBytes": 85899345920 }""");
        yield return Ex("RegisterResponse", """{ "requestId": "…", "heartbeatIntervalSeconds": 3 }""");
        yield return Ex("Heartbeat", """{ "requestId": "…", "nodeId": "d1", "freeBytes": 85899345920, "chunkCount": 120 }""");
        yield return Ex("HeartbeatResponse", """{ "requestId": "…" }""");
        yield return Ex("ChunkReport", """{ "requestId": "…", "nodeId": "d1", "chunks": [ { "chunkId": "c-01H", "size": 4194304, "sha256": "9f86d0" } ] }""");
        yield return Ex("ChunkReportResponse", """{ "requestId": "…", "unknownChunkIds": ["c-99H"] }""");

        // Admin (field names from the Admin table and object shapes in protocol.md)
        yield return Ex("CreateUser", """{ "requestId": "…", "session": "…", "username": "an.nguyen", "password": "secret" }""");
        yield return Ex("CreateUserResponse", """{ "requestId": "…", "user": { "id": "u-01H", "username": "an.nguyen", "disabled": false, "groupIds": [] } }""");
        yield return Ex("SetUserDisabled", """{ "requestId": "…", "session": "…", "userId": "u-01H", "disabled": true }""");
        yield return Ex("ResetPassword", """{ "requestId": "…", "session": "…", "userId": "u-01H", "newPassword": "new-secret" }""");
        yield return Ex("CreateGroup", """{ "requestId": "…", "session": "…", "name": "Team A" }""");
        yield return Ex("CreateGroupResponse", """{ "requestId": "…", "group": { "id": "g-01H", "name": "Team A", "memberIds": ["u-01H"] } }""");
        yield return Ex("SetGroupMember", """{ "requestId": "…", "session": "…", "groupId": "g-01H", "userId": "u-01H", "member": true }""");
        yield return Ex("CreateRole", """{ "requestId": "…", "session": "…", "name": "Reviewer", "scope": "item", "permissions": ["item.list", "file.read"] }""");
        yield return Ex("CreateRoleResponse", """{ "requestId": "…", "role": { "id": "r-01H", "name": "Reviewer", "scope": "item", "builtIn": false, "permissions": ["item.list", "file.read"] } }""");
        yield return Ex("UpdateRole", """{ "requestId": "…", "session": "…", "roleId": "r-01H", "permissions": ["item.list"] }""");
        yield return Ex("CreateGrant", """
            { "requestId": "…", "session": "…", "roleId": "r-editor", "subjectType": "group",
              "subjectId": "g-01H", "path": "/projects/dfs" }
            """);
        yield return Ex("CreateGrantResponse", """
            { "requestId": "…", "grant": { "id": "gr-01H", "roleId": "r-editor", "subjectType": "group", "subjectId": "g-01H", "path": "/projects/dfs", "itemId": "i-01H" } }
            """);
        yield return Ex("RevokeGrant", """{ "requestId": "…", "session": "…", "grantId": "gr-01H" }""");
        yield return Ex("ListAudit", """{ "requestId": "…", "session": "…", "limit": 100 }""");
        yield return Ex("ListAuditResponse", """
            { "requestId": "…", "entries": [ { "seq": 17, "time": "2026-11-20T09:15:02Z", "userId": "u-01H", "action": "CreateUpload", "target": "/projects/x.txt", "allowed": false } ] }
            """);
        yield return Ex("ClusterStatus", """{ "requestId": "…", "session": "…" }""");
        yield return Ex("ClusterStatusResponse", """
            { "requestId": "…", "dataServers": [ { "nodeId": "d1", "address": "192.168.10.13:7001", "status": "Alive", "capacityBytes": 107374182400, "freeBytes": 85899345920, "chunkCount": 120, "lastHeartbeat": "2026-11-20T09:15:02Z" } ] }
            """);
    }

    private static object[] Ex(string type, string json) => new object[] { type, json };

    private static string Fill(string json) =>
        json.Replace("\"requestId\": \"…\"", $"\"requestId\": \"{Rid}\"")
            .Replace("\"session\": \"…\"", "\"session\": \"s-AbC123\"");

    [Theory]
    [MemberData(nameof(Examples))]
    public void Message_DocExample_RoundTripsToSameJson(string typeName, string example)
    {
        // docs/protocol.md – Messages (stage 1): each example, AC1
        var type = ContractSupport.Require(ContractSupport.MessagesNs, typeName);
        var json = Fill(example);

        var value = JsonSerializer.Deserialize(json, type, Options);
        Assert.NotNull(value);
        var written = JsonSerializer.Serialize(value, type, Options);

        using var expected = JsonDocument.Parse(json);
        using var actual = JsonDocument.Parse(written);
        AssertJsonEqual(expected.RootElement, actual.RootElement, typeName);

        var again = JsonSerializer.Deserialize(written, type, Options);
        Assert.Equal(value, again); // records: value equality
    }

    [Fact]
    public void ErrorResponse_ConflictCode_SerializesPascalCase()
    {
        // docs/protocol.md – Error codes: code is PascalCase
        var type = ContractSupport.Require(ContractSupport.MessagesNs, "ErrorResponse");
        var json = Fill("""{ "requestId": "…", "code": "Conflict", "message": "baseVersion is stale" }""");
        var value = JsonSerializer.Deserialize(json, type, Options);
        Assert.NotNull(value);
        var written = JsonSerializer.Serialize(value, type, Options);
        using var doc = JsonDocument.Parse(written);
        Assert.Equal("Conflict", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public void DataServerInfo_AliveStatus_SerializesPascalCase()
    {
        // docs/protocol.md – ClusterStatus: status is "Alive"/"Dead"
        var type = ContractSupport.Require(ContractSupport.MessagesNs, "DataServerInfo");
        var json = """{ "nodeId": "ds-1", "address": "192.168.1.11:7001", "status": "Alive", "capacityBytes": 100, "freeBytes": 50, "chunkCount": 3, "lastHeartbeat": "2026-11-20T09:15:02Z" }""";
        var value = JsonSerializer.Deserialize(json, type, Options);
        Assert.NotNull(value);
        var written = JsonSerializer.Serialize(value, type, Options);
        using var doc = JsonDocument.Parse(written);
        Assert.Equal("Alive", doc.RootElement.GetProperty("status").GetString());
    }

    // Strings compare ordinally: the types must produce the documented casing ("file", "Alive", "Conflict").
    private static void AssertJsonEqual(JsonElement expected, JsonElement actual, string path)
    {
        Assert.Equal(expected.ValueKind == JsonValueKind.False ? JsonValueKind.True : expected.ValueKind,
                     actual.ValueKind == JsonValueKind.False ? JsonValueKind.True : actual.ValueKind);
        switch (expected.ValueKind)
        {
            case JsonValueKind.Object:
                var e = expected.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
                var a = actual.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
                Assert.Equal(e.Keys.OrderBy(k => k), a.Keys.OrderBy(k => k));
                foreach (var key in e.Keys) AssertJsonEqual(e[key], a[key], path + "." + key);
                break;
            case JsonValueKind.Array:
                var ea = expected.EnumerateArray().ToList();
                var aa = actual.EnumerateArray().ToList();
                Assert.True(ea.Count == aa.Count, $"{path}: array length");
                for (var i = 0; i < ea.Count; i++) AssertJsonEqual(ea[i], aa[i], $"{path}[{i}]");
                break;
            case JsonValueKind.String:
                Assert.True(string.Equals(expected.GetString(), actual.GetString(), StringComparison.Ordinal),
                    $"{path}: expected {expected.GetString()} but was {actual.GetString()}");
                break;
            case JsonValueKind.Number:
                Assert.Equal(expected.GetDecimal(), actual.GetDecimal());
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
                Assert.Equal(expected.GetBoolean(), actual.GetBoolean());
                break;
        }
    }
}
