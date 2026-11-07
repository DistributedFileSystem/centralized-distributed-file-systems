using System.Reflection;
using System.Text.RegularExpressions;

namespace Dfs.Tests.Contract;

/// <summary>Helpers shared by the contract tests: locate docs, parse their tables, find Dfs.Shared types by name.</summary>
internal static class ContractSupport
{
    public const string MessagesNs = "Dfs.Shared.Messages";
    public const string CommandsNs = "Dfs.Shared.Commands";
    public const string SecurityNs = "Dfs.Shared.Security";
    public const string MetadataNs = "Dfs.Shared.Metadata";
    public const string ClientNs = "Dfs.Shared.Client";

    public static readonly string[] ContractNamespaces = { MessagesNs, CommandsNs, SecurityNs, MetadataNs, ClientNs };

    public static Assembly Shared => Assembly.Load("Dfs.Shared");

    public static Type? Find(string ns, string name) => Shared.GetType(ns + "." + name);

    public static Type Require(string ns, string name)
    {
        var t = Find(ns, name);
        Assert.True(t is not null, $"Type {ns}.{name} does not exist in Dfs.Shared");
        return t!;
    }

    public static IEnumerable<Type> PublicTypesIn(string ns) =>
        Shared.GetExportedTypes().Where(t => t.Namespace == ns);

    public static bool IsRecord(Type t) => t.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.Instance) is not null;

    public static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Dfs.sln")))
                dir = dir.Parent;
            Assert.True(dir is not null, "Dfs.sln not found above " + AppContext.BaseDirectory);
            return dir!.FullName;
        }
    }

    public static string[] DocLines(string relativePath) =>
        File.ReadAllLines(Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>Lines under the "## ..." heading whose text starts with <paramref name="headingPrefix"/>, until the next "## ".</summary>
    public static List<string> Section(string[] lines, string headingPrefix)
    {
        var result = new List<string>();
        var inside = false;
        foreach (var line in lines)
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                if (inside) break;
                inside = line.Substring(3).StartsWith(headingPrefix, StringComparison.Ordinal);
                continue;
            }
            if (inside) result.Add(line);
        }
        return result;
    }

    public static IEnumerable<string> BacktickTokens(string text) =>
        Regex.Matches(text, "`([^`]+)`").Select(m => m.Groups[1].Value);

    /// <summary>Rows of the "Message id" table of protocol.md: (name, id, stage).</summary>
    public static List<(string Name, int Id, string Stage)> MessageTable()
    {
        var rows = new List<(string, int, string)>();
        foreach (var line in Section(DocLines("docs/protocol.md"), "Message id"))
        {
            var m = Regex.Match(line, @"^\|\s*`0x([0-9A-Fa-f]{2})`\s*\|\s*`(\w+)`\s*\|[^|]*\|\s*(\d)");
            if (m.Success)
                rows.Add((m.Groups[2].Value, Convert.ToInt32(m.Groups[1].Value, 16), m.Groups[3].Value));
        }
        return rows;
    }
}
