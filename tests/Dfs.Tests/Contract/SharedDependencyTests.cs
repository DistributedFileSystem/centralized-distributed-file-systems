namespace Dfs.Tests.Contract;

public class SharedDependencyTests
{
    [Fact]
    public void DfsShared_References_NoOtherDfsProject()
    {
        // plan TC9: acceptance tests reference Dfs.Shared only, so it must not pull in servers or client core
        var refs = ContractSupport.Shared.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(n => n.StartsWith("Dfs.", StringComparison.Ordinal))
            .ToList();
        Assert.Empty(refs);
    }
}
