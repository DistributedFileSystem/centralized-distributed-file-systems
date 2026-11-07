using System.Reflection;
using System.Xml.Linq;

namespace Dfs.Tests.Contract;

/// <summary>AC3 (TC5): public contract types and interface members carry XML docs (needs GenerateDocumentationFile on Dfs.Shared).</summary>
public class XmlDocTests
{
    private static HashSet<string> DocumentedMembers()
    {
        var asm = ContractSupport.Shared;
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Dfs.Shared.xml"),
            Path.Combine(Path.GetDirectoryName(asm.Location)!, "Dfs.Shared.xml"),
            Path.Combine(ContractSupport.RepoRoot, "src", "Dfs.Shared", "bin", "Release", "net10.0", "Dfs.Shared.xml"),
        };
        var file = candidates.FirstOrDefault(File.Exists);
        Assert.True(file is not null, "Dfs.Shared.xml not found: enable GenerateDocumentationFile in Dfs.Shared.csproj");

        var doc = XDocument.Load(file!);
        return doc.Descendants("member")
            .Where(m => !string.IsNullOrWhiteSpace((string?)m.Element("summary") ?? (m.Element("inheritdoc") is null ? null : "inherit")))
            .Select(m => (string)m.Attribute("name")!)
            .ToHashSet();
    }

    private static string TypeId(Type t) => "T:" + t.FullName!.Replace('+', '.');

    [Fact]
    public void ContractNamespaces_HaveAtLeastOnePublicType()
    {
        foreach (var ns in ContractSupport.ContractNamespaces)
            Assert.True(ContractSupport.PublicTypesIn(ns).Any(), $"no public type in {ns}");
    }

    [Fact]
    public void PublicContractTypes_HaveXmlSummary()
    {
        var documented = DocumentedMembers();
        var missing = ContractSupport.ContractNamespaces
            .SelectMany(ContractSupport.PublicTypesIn)
            .Select(TypeId)
            .Where(id => !documented.Contains(id))
            .ToList();
        Assert.True(missing.Count == 0, "Types without <summary>: " + string.Join(", ", missing));
    }

    [Fact]
    public void ContractInterfaces_HaveXmlSummaryOnEveryMember()
    {
        var documented = DocumentedMembers();
        var names = new[] { (ContractSupport.MetadataNs, "IMetadataStore"), (ContractSupport.SecurityNs, "IAuthorizer"), (ContractSupport.ClientNs, "IDfsClient") };
        var missing = new List<string>();
        foreach (var (ns, name) in names)
        {
            var t = ContractSupport.Require(ns, name);
            foreach (var m in t.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                var prefix = m.MemberType switch
                {
                    MemberTypes.Method => "M:",
                    MemberTypes.Property => "P:",
                    MemberTypes.Event => "E:",
                    _ => null,
                };
                if (prefix is null) continue;
                if (m is MethodInfo { IsSpecialName: true }) continue; // property accessors
                var id = prefix + t.FullName + "." + m.Name;
                if (!documented.Any(d => d == id || d.StartsWith(id + "(", StringComparison.Ordinal) || d.StartsWith(id + "`", StringComparison.Ordinal)))
                    missing.Add(id);
            }
        }
        Assert.True(missing.Count == 0, "Members without <summary>: " + string.Join(", ", missing));
    }

    [Fact]
    public void PublicEnums_HaveXmlSummaryOnEveryValue()
    {
        var documented = DocumentedMembers();
        var missing = ContractSupport.ContractNamespaces
            .SelectMany(ContractSupport.PublicTypesIn)
            .Where(t => t.IsEnum)
            .SelectMany(t => Enum.GetNames(t).Select(n => "F:" + t.FullName + "." + n))
            .Where(id => !documented.Contains(id))
            .ToList();
        Assert.True(missing.Count == 0, "Enum values without <summary>: " + string.Join(", ", missing));
    }
}
