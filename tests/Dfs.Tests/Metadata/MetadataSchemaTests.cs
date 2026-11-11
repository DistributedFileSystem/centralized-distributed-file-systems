using Dfs.MetadataServer.Store;
using Microsoft.Data.Sqlite;

namespace Dfs.Tests.Metadata;

public class MetadataSchemaTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public MetadataSchemaTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public void Initialize_CreatesAllTablesFromDataModel()
    {
        MetadataSchema.Initialize(_connection);

        var expectedTables = new[]
        {
            "users",
            "groups",
            "group_members",
            "roles",
            "role_permissions",
            "grants",
            "items",
            "file_versions",
            "version_chunks",
            "chunks",
            "chunk_locations",
            "data_servers",
            "applied_seq",
            "sessions",
            "uploads",
            "audit_log",
        };

        var actualTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                actualTables.Add(reader.GetString(0));
            }
        }

        foreach (var table in expectedTables)
        {
            Assert.Contains(table, actualTables);
        }
    }

    [Fact]
    public void Initialize_IsIdempotent()
    {
        // Calling twice should not throw
        MetadataSchema.Initialize(_connection);
        MetadataSchema.Initialize(_connection);

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM applied_seq WHERE id = 1";
        var count = Convert.ToInt64(cmd.ExecuteScalar());
        Assert.Equal(1, count);
    }

    [Fact]
    public void Initialize_AppliedSeqHasInitialZeroValue()
    {
        MetadataSchema.Initialize(_connection);

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT seq FROM applied_seq WHERE id = 1";
        var seq = Convert.ToInt64(cmd.ExecuteScalar());
        Assert.Equal(0, seq);
    }
}

