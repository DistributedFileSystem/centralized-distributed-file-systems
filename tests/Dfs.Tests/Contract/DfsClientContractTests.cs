namespace Dfs.Tests.Contract;

public class DfsClientContractTests
{
    // user-visible operations that map 1:1 to a protocol message (docs/protocol.md – Auth, Namespace, Admin)
    public static IEnumerable<object[]> Operations() =>
        new[]
        {
            "Login", "Logout", "ListDir", "Stat", "ListVersions", "CreateFolder", "Rename", "Move", "DeleteItem",
            "CreateUser", "ListUsers", "SetUserDisabled", "ResetPassword", "CreateGroup", "ListGroups", "SetGroupMember",
            "CreateRole", "ListRoles", "UpdateRole", "CreateGrant", "ListGrants", "RevokeGrant", "ListAudit", "ClusterStatus",
        }.Select(n => new object[] { n });

    [Theory]
    [MemberData(nameof(Operations))]
    public void IDfsClient_Operation_HasAsyncMethod(string operation)
    {
        // plan design 9: one async method per user-visible operation, named <Message>Async
        var client = ContractSupport.Require(ContractSupport.ClientNs, "IDfsClient");
        Assert.True(client.IsInterface);
        Assert.Contains(client.GetMethods(), m => m.Name == operation + "Async");
    }

    [Fact]
    public void IDfsClient_EveryMethod_ReturnsTask()
    {
        var client = ContractSupport.Require(ContractSupport.ClientNs, "IDfsClient");
        Assert.All(client.GetMethods(), m =>
        {
            Assert.EndsWith("Async", m.Name);
            Assert.True(typeof(Task).IsAssignableFrom(m.ReturnType), $"{m.Name} must return Task");
        });
    }

    [Fact]
    public void IDfsClient_HasUploadAndDownloadOperations()
    {
        // docs/flows.md – Upload, Download (exact names are not fixed by the docs: see test-report Questions)
        var client = ContractSupport.Require(ContractSupport.ClientNs, "IDfsClient");
        var names = client.GetMethods().Select(m => m.Name).ToList();
        Assert.Contains(names, n => n.Contains("Upload", StringComparison.Ordinal));
        Assert.Contains(names, n => n.Contains("Download", StringComparison.Ordinal) || n.Contains("Open", StringComparison.Ordinal));
    }
}
