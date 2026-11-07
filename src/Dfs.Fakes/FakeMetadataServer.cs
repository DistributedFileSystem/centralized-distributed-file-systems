using System.Security.Cryptography;
using Dfs.Shared.Messages;
using Dfs.Shared.Security;

namespace Dfs.Fakes;

/// <summary>Fake Metadata Server with a fixed Data Server location and a small in-memory namespace.</summary>
public sealed class FakeMetadataServer : FakeTcpServer
{
    private const int ChunkSize = 4 * 1024 * 1024;
    private readonly FakeDataServer _data;
    private readonly Dictionary<string, List<FileVersion>> _files = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PendingUpload> _uploads = new(StringComparer.Ordinal);
    private readonly HashSet<string> _sessions = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public FakeMetadataServer(FakeDataServer data)
    {
        _data = data;
        _files[FakeSamples.FilePath] =
        [
            new(FakeSamples.Items[^1], FakeSamples.Version,
                [new ChunkInfo(FakeSamples.ChunkId, FakeSamples.Content.Length, FakeSamples.Sha256)]),
        ];
    }

    protected override NodeType NodeType => Dfs.Shared.Messages.NodeType.Metadata;
    protected override string NodeId => "m-demo";

    protected override async Task HandleAsync(Stream stream, MessageType type, byte[] payload, CancellationToken cancellationToken)
    {
        if (type == MessageType.Login)
        {
            var login = FakeWire.Parse<Login>(payload);
            if (login.Username != "demo" || login.Password != "demo")
                throw new DfsException(ErrorCode.Unauthorized, "Invalid demo credentials.");
            var sessionToken = $"s-{Guid.NewGuid():N}";
            lock (_gate) _sessions.Add(sessionToken);
            await FakeWire.WriteAsync(stream, MessageType.LoginResponse,
                new LoginResponse(login.RequestId, sessionToken, DateTime.UtcNow.AddHours(8),
                    new UserRef(FakeSamples.UserId, "demo"), new ValueList<string>([BuiltInRoles.Admin]),
                    new ValueList<string>([Permission.UserManage, Permission.GroupManage, Permission.RoleManage,
                        Permission.ClusterView, Permission.ClusterManage, Permission.AuditView])), cancellationToken);
            return;
        }

        // Every remaining supported metadata request carries the demo session.
        using var document = System.Text.Json.JsonDocument.Parse(payload);
        if (!document.RootElement.TryGetProperty("session", out var session) ||
            session.GetString() is not { } token || !HasSession(token))
            throw new DfsException(ErrorCode.Unauthorized, "Log in to the demo first.");

        switch (type)
        {
            case MessageType.Logout:
            {
                var request = FakeWire.Parse<Logout>(payload);
                lock (_gate) _sessions.Remove(request.Session);
                await FakeWire.WriteAsync(stream, MessageType.LogoutResponse,
                    new LogoutResponse(request.RequestId), cancellationToken);
                break;
            }
            case MessageType.ListDir:
            {
                var request = FakeWire.Parse<ListDir>(payload);
                var items = ListChildren(request.Path);
                await FakeWire.WriteAsync(stream, MessageType.ListDirResponse,
                    new ListDirResponse(request.RequestId, new ValueList<ItemInfo>(items)), cancellationToken);
                break;
            }
            case MessageType.Stat:
            {
                var request = FakeWire.Parse<Stat>(payload);
                await FakeWire.WriteAsync(stream, MessageType.StatResponse,
                    new StatResponse(request.RequestId, Find(request.Path)), cancellationToken);
                break;
            }
            case MessageType.ListVersions:
            {
                var request = FakeWire.Parse<ListVersions>(payload);
                List<VersionInfo> versions;
                lock (_gate)
                    versions = GetFile(request.Path).Select(file => file.Version).Reverse().ToList();
                await FakeWire.WriteAsync(stream, MessageType.ListVersionsResponse,
                    new ListVersionsResponse(request.RequestId, new ValueList<VersionInfo>(versions)), cancellationToken);
                break;
            }
            case MessageType.CreateUpload:
            {
                var request = FakeWire.Parse<CreateUpload>(payload);
                var response = PlanUpload(request);
                await FakeWire.WriteAsync(stream, MessageType.CreateUploadResponse, response, cancellationToken);
                break;
            }
            case MessageType.CommitUpload:
            {
                var request = FakeWire.Parse<CommitUpload>(payload);
                var response = Commit(request);
                await FakeWire.WriteAsync(stream, MessageType.CommitUploadResponse, response, cancellationToken);
                break;
            }
            case MessageType.AbortUpload:
            {
                var request = FakeWire.Parse<AbortUpload>(payload);
                lock (_gate)
                    if (!_uploads.Remove(request.UploadId)) throw new DfsException(ErrorCode.NotFound, "Upload not found.");
                await FakeWire.WriteAsync(stream, MessageType.AbortUploadResponse,
                    new AbortUploadResponse(request.RequestId), cancellationToken);
                break;
            }
            case MessageType.Open:
            {
                var request = FakeWire.Parse<Open>(payload);
                OpenResponse response;
                lock (_gate)
                {
                    var versions = GetFile(request.Path);
                    var file = request.Version == 0 ? versions[^1] : versions.FirstOrDefault(v => v.Version.Version == request.Version)
                        ?? throw new DfsException(ErrorCode.NotFound, "Version not found.");
                    var chunks = file.Chunks.Select((chunk, index) => new OpenChunk(index, chunk.ChunkId,
                        chunk.Size, chunk.Sha256, new ValueList<string>([_data.Address]), FakeToken.Create(chunk.ChunkId, "r")));
                    response = new OpenResponse(request.RequestId, file.Item, file.Version.Version,
                        file.Version.Size, ChunkSize, new ValueList<OpenChunk>(chunks));
                }
                await FakeWire.WriteAsync(stream, MessageType.OpenResponse, response, cancellationToken);
                break;
            }
            case MessageType.ClusterStatus:
            {
                var request = FakeWire.Parse<ClusterStatus>(payload);
                var server = new DataServerInfo("d-demo", _data.Address, DataServerStatus.Alive,
                    1024 * 1024 * 1024, 1024 * 1024 * 1024 - FakeSamples.Content.Length, 1, DateTime.UtcNow);
                await FakeWire.WriteAsync(stream, MessageType.ClusterStatusResponse,
                    new ClusterStatusResponse(request.RequestId, new ValueList<DataServerInfo>([server])), cancellationToken);
                break;
            }
            default:
                throw new DfsException(ErrorCode.BadRequest, "Unsupported fake metadata request.");
        }
    }

    private ItemInfo[] ListChildren(string path)
    {
        if (Find(path).Kind != ItemKind.Folder) throw new DfsException(ErrorCode.BadRequest, "Path is not a folder.");
        var prefix = path == "/" ? "/" : path + "/";
        lock (_gate)
            return FakeSamples.Items.Where(item => item.Kind == ItemKind.Folder)
                .Concat(_files.Values.Select(versions => versions[^1].Item))
                .Where(item => item.Path != path && item.Path.StartsWith(prefix, StringComparison.Ordinal)
                    && !item.Path[prefix.Length..].Contains('/'))
                .DistinctBy(item => item.Path)
                .ToArray();
    }

    private ItemInfo Find(string path)
    {
        lock (_gate)
        {
            if (_files.TryGetValue(path, out var versions)) return versions[^1].Item;
            return FakeSamples.Items.FirstOrDefault(item => item.Path == path)
                ?? throw new DfsException(ErrorCode.NotFound, "Demo path not found.");
        }
    }

    private List<FileVersion> GetFile(string path) => _files.TryGetValue(path, out var versions)
        ? versions : throw new DfsException(ErrorCode.NotFound, "Demo file not found.");

    private CreateUploadResponse PlanUpload(CreateUpload request)
    {
        if (!request.Path.StartsWith('/') || request.Path.EndsWith('/') ||
            request.Path.Contains("//", StringComparison.Ordinal) || request.Path.Split('/').Skip(1).Any(part => part is "." or ".."))
            throw new DfsException(ErrorCode.BadRequest, "Invalid demo path.");
        if (request.Size < 0 || request.Size > 2L * 1024 * 1024 * 1024)
            throw new DfsException(ErrorCode.BadRequest, "Invalid demo file size.");
        if (request.Path == "/" || !FakeSamples.Items.Any(item => item.Path == Parent(request.Path) && item.Kind == ItemKind.Folder))
            throw new DfsException(ErrorCode.NotFound, "Demo parent folder not found.");
        if (string.IsNullOrEmpty(_data.Address)) throw new InvalidOperationException("Start the fake Data Server first.");

        lock (_gate)
        {
            var current = _files.TryGetValue(request.Path, out var versions) ? versions[^1].Version.Version : 0;
            if (current != request.BaseVersion) throw new DfsException(ErrorCode.Conflict, "Version changed.");
            var uploadId = $"up-{Guid.NewGuid():N}";
            var count = (int)((request.Size + ChunkSize - 1) / ChunkSize);
            var chunks = Enumerable.Range(0, count)
                .Select(index => $"c-{Guid.NewGuid():N}").ToArray();
            _uploads.Add(uploadId, new PendingUpload(request.Path, request.Size, request.BaseVersion, chunks));
            return new CreateUploadResponse(request.RequestId, uploadId, ChunkSize,
                new ValueList<UploadChunk>(chunks.Select((id, index) =>
                    new UploadChunk(index, id, new ValueList<string>([_data.Address]), FakeToken.Create(id, "w")))));
        }
    }

    private CommitUploadResponse Commit(CommitUpload request)
    {
        lock (_gate)
        {
            if (!_uploads.TryGetValue(request.UploadId, out var upload))
                throw new DfsException(ErrorCode.NotFound, "Upload not found.");
            var current = _files.TryGetValue(upload.Path, out var versions) ? versions[^1].Version.Version : 0;
            if (current != upload.BaseVersion) throw new DfsException(ErrorCode.Conflict, "Version changed.");
            if (request.Checksums.Count != upload.ChunkIds.Length)
                throw new DfsException(ErrorCode.BadRequest, "Missing chunk checksums.");

            var chunks = new List<ChunkInfo>();
            foreach (var id in upload.ChunkIds)
            {
                var bytes = _data.Read(id);
                var matches = request.Checksums.Where(info => info.ChunkId == id).Take(2).ToArray();
                if (matches.Length != 1) throw new DfsException(ErrorCode.BadRequest, "Expected one checksum per chunk.");
                var checksum = matches[0];
                var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
                if (checksum.Size != bytes.Length || checksum.Sha256 != hash)
                    throw new DfsException(ErrorCode.BadRequest, "Chunk checksum does not match.");
                chunks.Add(new ChunkInfo(id, bytes.Length, hash));
            }
            if (chunks.Sum(chunk => chunk.Size) != upload.Size)
                throw new DfsException(ErrorCode.BadRequest, "Uploaded size does not match.");

            var version = current + 1;
            var now = DateTime.UtcNow;
            var item = new ItemInfo(versions is null ? $"i-{Guid.NewGuid():N}" : versions[^1].Item.Id,
                upload.Path, upload.Path[(upload.Path.LastIndexOf('/') + 1)..], ItemKind.File,
                upload.Size, FakeSamples.UserId, version, now);
            var fileVersion = new FileVersion(item, new VersionInfo(version, upload.Size, FakeSamples.UserId, now, chunks.Count), chunks);
            if (versions is null) _files[upload.Path] = [fileVersion];
            else versions.Add(fileVersion);
            _uploads.Remove(request.UploadId);
            return new CommitUploadResponse(request.RequestId, item, version);
        }
    }

    private static string Parent(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash == 0 ? "/" : path[..slash];
    }

    private bool HasSession(string token)
    {
        lock (_gate) return _sessions.Contains(token);
    }

    private sealed record FileVersion(ItemInfo Item, VersionInfo Version, List<ChunkInfo> Chunks);
    private sealed record PendingUpload(string Path, long Size, int BaseVersion, string[] ChunkIds);
}
