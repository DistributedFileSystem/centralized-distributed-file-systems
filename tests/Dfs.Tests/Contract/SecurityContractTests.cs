using System.Reflection;

namespace Dfs.Tests.Contract;

public class SecurityContractTests
{
    private static HashSet<string> ConstStrings(Type t) =>
        t.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToHashSet();

    [Fact]
    public void Permission_Constants_EqualRbacPermissionTable()
    {
        // docs/rbac.md – Permissions (TC6)
        var documented = ContractSupport.Section(ContractSupport.DocLines("docs/rbac.md"), "Permissions")
            .Where(l => l.StartsWith("| System") || l.StartsWith("| Thư mục"))
            .SelectMany(l => ContractSupport.BacktickTokens(l.Split('|')[2]))
            .ToHashSet();
        Assert.Equal(14, documented.Count);

        var type = ContractSupport.Require(ContractSupport.SecurityNs, "Permission");
        Assert.Equal(documented.OrderBy(x => x), ConstStrings(type).OrderBy(x => x));
    }

    [Theory]
    [InlineData("Admin", "r-admin")]
    [InlineData("Auditor", "r-auditor")]
    [InlineData("Owner", "r-owner")]
    [InlineData("Editor", "r-editor")]
    [InlineData("Viewer", "r-viewer")]
    public void BuiltInRoles_ContainNameAndFixedId(string name, string id)
    {
        // docs/rbac.md – Built-in roles ("Mỗi built-in role có id cố định")
        var type = ContractSupport.Require(ContractSupport.SecurityNs, "BuiltInRoles");
        var values = ConstStrings(type);
        Assert.Contains(name, values);
        Assert.Contains(id, values);
    }

    [Fact]
    public void IAuthorizer_AuthorizeAsync_TakesUserPermissionItemAndReturnsResult()
    {
        // plan design 8 + Q3: pure decision, AuthorizeAsync(userId, permission, itemId?) -> AuthorizationResult
        var auth = ContractSupport.Require(ContractSupport.SecurityNs, "IAuthorizer");
        var result = ContractSupport.Require(ContractSupport.SecurityNs, "AuthorizationResult");
        Assert.True(auth.IsInterface);

        var method = Assert.Single(auth.GetMethods(), m => m.Name == "AuthorizeAsync");
        Assert.Equal(typeof(Task<>).MakeGenericType(result), method.ReturnType);
        var ps = method.GetParameters().Where(p => p.ParameterType != typeof(CancellationToken)).ToArray();
        Assert.Equal(3, ps.Length);
        Assert.All(ps, p => Assert.Equal(typeof(string), p.ParameterType));

        Assert.Contains(result.GetProperties(), p => p.PropertyType == typeof(bool));
    }

    [Fact]
    public void IAuthorizer_DoesNotDependOnMetadataStore()
    {
        // docs/rbac.md – Kiểm tra một request, step 5: IAuthorizer does not depend on the store
        var auth = ContractSupport.Require(ContractSupport.SecurityNs, "IAuthorizer");
        var store = ContractSupport.Require(ContractSupport.MetadataNs, "IMetadataStore");
        var used = auth.GetMethods().SelectMany(m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType));
        Assert.DoesNotContain(store, used);
        Assert.Empty(auth.GetProperties().Where(p => p.PropertyType == store));
    }
}
