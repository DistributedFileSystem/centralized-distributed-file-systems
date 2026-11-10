using Dfs.MetadataServer.Config;

namespace Dfs.Tests.Metadata;

public class MetadataServerConfigTests
{
    [Fact]
    public void FromEnvironment_DefaultValues_WhenVariablesAreUnset()
    {
        // Clear variables to ensure defaults apply
        var vars = new[] { "NODE_ROLE", "NODE_ID", "LISTEN_PORT", "ADVERTISE_HOST", "METADATA_ENDPOINTS", "DATA_DIR", "TOKEN_KEY", "ADMIN_USER", "ADMIN_PASSWORD" };
        var originals = vars.ToDictionary(v => v, Environment.GetEnvironmentVariable);
        try
        {
            foreach (var v in vars) Environment.SetEnvironmentVariable(v, null);

            var config = MetadataServerConfig.FromEnvironment();

            Assert.Equal("metadata", config.NodeRole);
            Assert.Equal("m1", config.NodeId);
            Assert.Equal(7000, config.ListenPort);
            Assert.Equal("127.0.0.1", config.AdvertiseHost);
            Assert.Equal(new[] { "127.0.0.1:7000" }, config.MetadataEndpoints);
            Assert.Equal("data", config.DataDir);
            Assert.Equal(string.Empty, config.TokenKey);
            Assert.Equal("admin", config.AdminUser);
            Assert.Equal(Path.Combine("data", "metadata.db"), config.DatabasePath);
            Assert.Equal(Path.Combine("data", "oplog.jsonl"), config.OpLogPath);
        }
        finally
        {
            foreach (var (k, v) in originals) Environment.SetEnvironmentVariable(k, v);
        }
    }

    [Fact]
    public void FromEnvironment_CustomValues_ParsedCorrectly()
    {
        var vars = new[] { "NODE_ROLE", "NODE_ID", "LISTEN_PORT", "ADVERTISE_HOST", "METADATA_ENDPOINTS", "DATA_DIR", "TOKEN_KEY", "ADMIN_USER", "ADMIN_PASSWORD" };
        var originals = vars.ToDictionary(v => v, Environment.GetEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable("NODE_ROLE", "metadata-leader");
            Environment.SetEnvironmentVariable("NODE_ID", "meta-node-42");
            Environment.SetEnvironmentVariable("LISTEN_PORT", "8050");
            Environment.SetEnvironmentVariable("ADVERTISE_HOST", "192.168.1.100");
            Environment.SetEnvironmentVariable("METADATA_ENDPOINTS", "192.168.1.100:8050, 192.168.1.101:8050");
            Environment.SetEnvironmentVariable("DATA_DIR", "/var/dfs/metadata");
            Environment.SetEnvironmentVariable("TOKEN_KEY", "super-secret-hmac-key");
            Environment.SetEnvironmentVariable("ADMIN_USER", "sysadmin");
            Environment.SetEnvironmentVariable("ADMIN_PASSWORD", "topsecret");

            var config = MetadataServerConfig.FromEnvironment();

            Assert.Equal("metadata-leader", config.NodeRole);
            Assert.Equal("meta-node-42", config.NodeId);
            Assert.Equal(8050, config.ListenPort);
            Assert.Equal("192.168.1.100", config.AdvertiseHost);
            Assert.Equal(new[] { "192.168.1.100:8050", "192.168.1.101:8050" }, config.MetadataEndpoints);
            Assert.Equal("/var/dfs/metadata", config.DataDir);
            Assert.Equal("super-secret-hmac-key", config.TokenKey);
            Assert.Equal("sysadmin", config.AdminUser);
            Assert.Equal("topsecret", config.AdminPassword);
        }
        finally
        {
            foreach (var (k, v) in originals) Environment.SetEnvironmentVariable(k, v);
        }
    }

    [Theory]
    [InlineData("not-a-number")]
    [InlineData("-5")]
    [InlineData("70000")]
    public void FromEnvironment_InvalidPort_FallsBackToDefault(string invalidPort)
    {
        var original = Environment.GetEnvironmentVariable("LISTEN_PORT");
        try
        {
            Environment.SetEnvironmentVariable("LISTEN_PORT", invalidPort);
            var config = MetadataServerConfig.FromEnvironment();
            Assert.Equal(MetadataServerConfig.DefaultPort, config.ListenPort);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LISTEN_PORT", original);
        }
    }
}
