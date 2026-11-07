namespace Dfs.Tests.Contract;

public class MessageTypeTests
{
    private static readonly string[] NoSession =
        { "Hello", "Login", "Register", "Heartbeat", "ChunkReport", "WriteChunk", "ReadChunk", "DeleteChunk" };

    public static IEnumerable<object[]> Stage1Requests() =>
        ContractSupport.MessageTable()
            .Where(r => r.Stage == "1" && r.Name != "Error")
            .Select(r => new object[] { r.Name, r.Id });

    [Fact]
    public void MessageTable_InProtocolDoc_HasAllStage1AndStage2Rows()
    {
        // docs/protocol.md – Message id
        var table = ContractSupport.MessageTable();
        Assert.Equal(table.Count, table.Select(r => r.Id).Distinct().Count());
        Assert.Contains(table, r => r.Name == "ForwardChunk" && r.Id == 0x70 && r.Stage == "2");
        Assert.Contains(table, r => r.Name == "Error" && r.Id == 0xFF);
        Assert.True(table.Count(r => r.Stage == "1") == 36, "stage 1 lists 35 requests plus Error");
    }

    [Fact]
    public void MessageType_Enum_ExistsAndMatchesIdTable()
    {
        // docs/protocol.md – Message id (id table lives in the MessageType enum)
        var type = ContractSupport.Require(ContractSupport.MessagesNs, "MessageType");
        Assert.True(type.IsEnum);

        var actual = Enum.GetNames(type)
            .ToDictionary(n => n, n => Convert.ToInt32(Enum.Parse(type, n)));

        foreach (var (name, id, _) in ContractSupport.MessageTable())
        {
            Assert.True(actual.ContainsKey(name), $"MessageType is missing {name}");
            Assert.Equal(id, actual[name]);
        }
    }

    [Fact]
    public void MessageType_RequestIds_AreUniqueAndBelow0x80()
    {
        // docs/protocol.md – Message id: request 0x01-0x7F, response = request | 0x80, 0xFF = Error
        var type = ContractSupport.Require(ContractSupport.MessagesNs, "MessageType");
        var requestIds = ContractSupport.MessageTable()
            .Where(r => r.Name != "Error")
            .Select(r => r.Id)
            .ToList();

        Assert.All(requestIds, id => Assert.InRange(id, 0x01, 0x7F));
        Assert.Equal(requestIds.Count, requestIds.Distinct().Count());
        Assert.Equal(0xFF, Convert.ToInt32(Enum.Parse(type, "Error")));
    }

    [Theory]
    [MemberData(nameof(Stage1Requests))]
    public void Message_Stage1_HasRequestAndResponseRecord(string name, int id)
    {
        // docs/protocol.md – Message id; AC1 (TC1). Convention: request = <Name>, response = <Name>Response.
        _ = id;
        var request = ContractSupport.Require(ContractSupport.MessagesNs, name);
        var response = ContractSupport.Require(ContractSupport.MessagesNs, name + "Response");

        Assert.True(ContractSupport.IsRecord(request), $"{name} must be a record");
        Assert.True(ContractSupport.IsRecord(response), $"{name}Response must be a record");
        Assert.NotNull(request.GetProperty("RequestId"));
        Assert.NotNull(response.GetProperty("RequestId"));
    }

    [Theory]
    [MemberData(nameof(Stage1Requests))]
    public void Request_Session_PresentExceptForHelloLoginAndDataServerMessages(string name, int id)
    {
        // docs/protocol.md – Quy ước chung / Session
        _ = id;
        var request = ContractSupport.Require(ContractSupport.MessagesNs, name);
        var hasSession = request.GetProperty("Session") is not null;
        Assert.Equal(!NoSession.Contains(name), hasSession);
    }

    [Theory]
    [InlineData("ForwardChunk")]
    [InlineData("CopyChunk")]
    public void Message_Stage2_HasNoRecordYet(string name)
    {
        // docs/protocol.md – stage 2 "chưa định nghĩa field"; plan: no records
        Assert.Null(ContractSupport.Find(ContractSupport.MessagesNs, name));
    }
}
