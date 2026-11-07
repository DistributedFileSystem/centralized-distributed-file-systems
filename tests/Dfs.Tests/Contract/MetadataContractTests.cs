namespace Dfs.Tests.Contract;

public class MetadataContractTests
{
    // plan.md "Proposed design" item 6 (approved at Gate 1)
    public static readonly string[] CommandNames =
    {
        "CreateFolder", "RenameItem", "MoveItem", "DeleteItem", "CreateUser", "SetUserDisabled", "ResetPassword",
        "CreateGroup", "SetGroupMember", "CreateRole", "UpdateRole", "SetGrant", "RevokeGrant", "BeginUpload",
        "CommitUpload", "AbortUpload", "CreateSession", "DeleteSession", "RegisterDataServer", "UpdateHeartbeat", "RecordAudit",
    };

    public static IEnumerable<object[]> Commands() => CommandNames.Select(n => new object[] { n });

    [Theory]
    [MemberData(nameof(Commands))]
    public void Command_Planned_IsSealedRecordImplementingIMetadataCommand(string name)
    {
        // docs/architecture.md – Design rule 1 (command for every change)
        var marker = ContractSupport.Require(ContractSupport.CommandsNs, "IMetadataCommand");
        var type = ContractSupport.Require(ContractSupport.CommandsNs, name);
        Assert.True(marker.IsInterface);
        Assert.True(ContractSupport.IsRecord(type), $"{name} must be a record");
        Assert.True(type.IsSealed);
        Assert.True(marker.IsAssignableFrom(type));
    }

    [Theory]
    [MemberData(nameof(Commands))]
    public void Command_Planned_HasNoParameterlessConstructor(string name)
    {
        // docs/architecture.md – Design rule 3 (deterministic): ids and timestamps are passed in, never generated
        var type = ContractSupport.Require(ContractSupport.CommandsNs, name);
        Assert.Null(type.GetConstructor(Type.EmptyTypes));
        Assert.Contains(type.GetConstructors(), c => c.GetParameters().Length > 0);
    }

    [Fact]
    public void Commands_Namespace_ContainsOnlyPlannedCommandsAndMarker()
    {
        var marker = ContractSupport.Require(ContractSupport.CommandsNs, "IMetadataCommand");
        var implementers = ContractSupport.PublicTypesIn(ContractSupport.CommandsNs)
            .Where(t => t != marker && marker.IsAssignableFrom(t) && !t.IsAbstract)
            .Select(t => t.Name)
            .ToList();
        Assert.Equal(CommandNames.OrderBy(x => x), implementers.OrderBy(x => x));
    }

    [Fact]
    public void IMetadataStore_SubmitAsync_IsTheOnlyWritePath()
    {
        // docs/architecture.md – Design rule 2 (single write path); plan design 7; TC8
        var store = ContractSupport.Require(ContractSupport.MetadataNs, "IMetadataStore");
        var command = ContractSupport.Require(ContractSupport.CommandsNs, "IMetadataCommand");
        var result = ContractSupport.Require(ContractSupport.MetadataNs, "CommandResult");
        Assert.True(store.IsInterface);

        var submit = Assert.Single(store.GetMethods(), m => m.Name == "SubmitAsync");
        Assert.Equal(typeof(Task<>).MakeGenericType(result), submit.ReturnType);
        Assert.Equal(command, submit.GetParameters()[0].ParameterType);

        var others = store.GetMethods().Where(m => m != submit).Select(m => m.Name).ToList();
        Assert.NotEmpty(others);
        Assert.All(others, n => Assert.Matches("^(Get|List|Find)", n));
        Assert.Empty(store.GetProperties().Where(p => p.CanWrite));
    }
}
