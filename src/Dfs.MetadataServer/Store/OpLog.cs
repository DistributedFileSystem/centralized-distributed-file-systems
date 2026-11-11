using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dfs.Shared.Commands;
using CreateFolder = Dfs.Shared.Commands.CreateFolder;
using CreateUser = Dfs.Shared.Commands.CreateUser;
using DeleteItem = Dfs.Shared.Commands.DeleteItem;
using MoveItem = Dfs.Shared.Commands.MoveItem;
using RenameItem = Dfs.Shared.Commands.RenameItem;

namespace Dfs.MetadataServer.Store;

/// <summary>One parsed op log line.</summary>
internal sealed record OpLogEntry(long Seq, IMetadataCommand Command);

/// <summary>Append-only <c>oplog.jsonl</c>: one JSON line per command, fsync before returning from append.</summary>
internal sealed class OpLog
{
    private const string TimeFormat = "yyyy-MM-ddTHH:mm:ss.FFFFFFFZ";

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static readonly Dictionary<string, Type> TypesByName = new()
    {
        ["CreateFolder"] = typeof(CreateFolder),
        ["RenameItem"] = typeof(RenameItem),
        ["MoveItem"] = typeof(MoveItem),
        ["DeleteItem"] = typeof(DeleteItem),
        ["CreateUser"] = typeof(CreateUser),
    };

    private readonly string _path;

    private OpLog(string path, long lastSeq)
    {
        _path = path;
        LastSeq = lastSeq;
    }

    /// <summary>Gets the seq of the last valid line, 0 when the log is empty.</summary>
    public long LastSeq { get; private set; }

    /// <summary>Gets a value indicating whether the command type can be logged and applied.</summary>
    public static bool IsSupported(IMetadataCommand command) => TypesByName.ContainsValue(command.GetType());

    /// <summary>Opens the log, ignoring a torn or blank tail, and returns the valid entries.</summary>
    public static OpLog Open(string path, out List<OpLogEntry> entries)
    {
        entries = new List<OpLogEntry>();
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        // The handle is not kept open between appends, so other readers can open the file with the default share mode.
        using (var file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete))
        {
            var bytes = new byte[file.Length];
            file.ReadExactly(bytes);

            long validEnd = 0;
            var lastValidNeedsNewline = false;
            long lastSeq = 0;
            var pos = 0;
            while (pos < bytes.Length)
            {
                var nl = Array.IndexOf(bytes, (byte)'\n', pos);
                var lineEnd = nl < 0 ? bytes.Length : nl;
                var next = nl < 0 ? bytes.Length : nl + 1;
                var text = Encoding.UTF8.GetString(bytes, pos, lineEnd - pos);
                if (text.Trim().Length > 0)
                {
                    var entry = TryParse(text);
                    if (entry is null)
                    {
                        if (!IsOnlyBlankAfter(bytes, next))
                            throw new InvalidDataException($"Corrupt op log line before the tail of {path}.");
                        break;
                    }
                    if (entry.Seq != lastSeq + 1)
                        throw new InvalidDataException($"Op log seq {entry.Seq} does not follow {lastSeq} in {path}.");
                    entries.Add(entry);
                    lastSeq = entry.Seq;
                    validEnd = next;
                    lastValidNeedsNewline = nl < 0;
                }
                pos = next;
            }

            file.SetLength(validEnd);
            file.Position = validEnd;
            if (lastValidNeedsNewline) file.WriteByte((byte)'\n');
            file.Flush(true);
            return new OpLog(path, lastSeq);
        }
    }

    /// <summary>Appends the command with the next seq and fsyncs.</summary>
    public long Append(IMetadataCommand command)
    {
        var seq = LastSeq + 1;
        var name = TypesByName.First(p => p.Value == command.GetType()).Key;
        var data = JsonSerializer.SerializeToNode(command, command.GetType(), Json)!.AsObject();
        data.Remove("at");
        var line = new JsonObject
        {
            ["seq"] = seq,
            ["type"] = name,
            ["at"] = command.At.ToUniversalTime().ToString(TimeFormat, CultureInfo.InvariantCulture),
            ["data"] = data,
        };
        var bytes = Encoding.UTF8.GetBytes(line.ToJsonString() + "\n");
        using (var file = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
        {
            file.Write(bytes);
            file.Flush(true);
        }
        LastSeq = seq;
        return seq;
    }

    private static bool IsOnlyBlankAfter(byte[] bytes, int from)
    {
        for (var i = from; i < bytes.Length; i++)
        {
            if (!char.IsWhiteSpace((char)bytes[i])) return false;
        }
        return true;
    }

    private static OpLogEntry? TryParse(string text)
    {
        try
        {
            var root = JsonNode.Parse(text)?.AsObject();
            if (root is null) return null;
            var seq = root["seq"]!.GetValue<long>();
            var type = root["type"]!.GetValue<string>();
            var at = root["at"]!.GetValue<string>();
            var data = root["data"]!.AsObject();
            if (!TypesByName.TryGetValue(type, out var clr)) return null;
            data["at"] = at;
            var command = (IMetadataCommand?)data.Deserialize(clr, Json);
            return command is null ? null : new OpLogEntry(seq, command);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException or NullReferenceException)
        {
            return null;
        }
    }
}
