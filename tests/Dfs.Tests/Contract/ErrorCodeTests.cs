namespace Dfs.Tests.Contract;

public class ErrorCodeTests
{
    private static HashSet<string> DocumentedCodes()
    {
        // docs/protocol.md – Error codes: first column of the table, may hold several `Code` tokens
        var codes = new HashSet<string>();
        foreach (var line in ContractSupport.Section(ContractSupport.DocLines("docs/protocol.md"), "Error codes"))
        {
            if (!line.StartsWith("| `", StringComparison.Ordinal)) continue;
            var firstCell = line.Split('|')[1];
            foreach (var token in ContractSupport.BacktickTokens(firstCell)) codes.Add(token);
        }
        return codes;
    }

    [Fact]
    public void ErrorCode_Enum_ContainsEveryDocumentedCodeAndNothingElse()
    {
        // docs/protocol.md – Error codes; AC2 (TC4)
        var documented = DocumentedCodes();
        Assert.Equal(12, documented.Count);

        var type = ContractSupport.Require(ContractSupport.MessagesNs, "ErrorCode");
        Assert.True(type.IsEnum);
        Assert.Equal(documented.OrderBy(c => c), Enum.GetNames(type).OrderBy(c => c));
    }

    [Theory]
    [InlineData("Unauthorized")]
    [InlineData("Forbidden")]
    [InlineData("NotFound")]
    [InlineData("AlreadyExists")]
    [InlineData("Conflict")]
    [InlineData("BadRequest")]
    [InlineData("TokenInvalid")]
    [InlineData("TokenExpired")]
    [InlineData("Unavailable")]
    [InlineData("VersionMismatch")]
    [InlineData("Internal")]
    [InlineData("NotLeader")]
    public void ErrorCode_NamedInProtocolDoc_Exists(string code)
    {
        // docs/protocol.md – Error codes
        var type = ContractSupport.Require(ContractSupport.MessagesNs, "ErrorCode");
        Assert.Contains(code, Enum.GetNames(type));
    }

    [Fact]
    public void DfsException_IsExceptionCarryingErrorCode()
    {
        // plan: IDfsClient throws DfsException(ErrorCode)
        var ex = ContractSupport.Require(ContractSupport.MessagesNs, "DfsException");
        var code = ContractSupport.Require(ContractSupport.MessagesNs, "ErrorCode");
        Assert.True(typeof(Exception).IsAssignableFrom(ex));
        var prop = ex.GetProperty("Code");
        Assert.NotNull(prop);
        Assert.Equal(code, prop!.PropertyType);
        Assert.Contains(ex.GetConstructors(), c => c.GetParameters().Length >= 1 && c.GetParameters()[0].ParameterType == code);
    }
}
