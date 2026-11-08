using System.Security.Cryptography;
using System.Text;
using Dfs.Fakes;
using Dfs.Shared.Messages;

namespace Dfs.Tests.Fakes;

public class FakeDfsClientTests
{
    [Fact]
    public async Task Demo_client_lists_a_stable_folder_tree_after_login()
    {
        var client = new FakeDfsClient();
        var unauthorized = await Assert.ThrowsAsync<DfsException>(() => client.ListDirAsync("/"));
        Assert.Equal(ErrorCode.Unauthorized, unauthorized.Code);

        var login = await client.LoginAsync("demo", "demo");
        Assert.Equal("demo", login.User.Username);
        Assert.Equal(["/projects"], (await client.ListDirAsync("/")).Select(item => item.Path));
        Assert.Equal(["/projects/dfs"], (await client.ListDirAsync("/projects")).Select(item => item.Path));
        Assert.Equal(["/projects/dfs/readme.txt"], (await client.ListDirAsync("/projects/dfs")).Select(item => item.Path));
        Assert.Equal(ItemKind.File, (await client.StatAsync("/projects/dfs/readme.txt")).Kind);
        Assert.Single(await client.ListVersionsAsync("/projects/dfs/readme.txt"));

        await client.LogoutAsync();
        await Assert.ThrowsAsync<DfsException>(() => client.StatAsync("/projects"));
    }

    [Fact]
    public void Data_server_keeps_an_independent_copy_and_reports_sha256()
    {
        var data = new FakeDataServer();
        var original = Encoding.UTF8.GetBytes("hello from a chunk");
        var info = data.Store("c-test", original);
        original[0] = (byte)'X';

        Assert.Equal("hello from a chunk", Encoding.UTF8.GetString(data.Read("c-test")));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(data.Read("c-test"))), info.Sha256);
        Assert.Equal(ErrorCode.NotFound, Assert.Throws<DfsException>(() => data.Read("missing")).Code);
    }
}
