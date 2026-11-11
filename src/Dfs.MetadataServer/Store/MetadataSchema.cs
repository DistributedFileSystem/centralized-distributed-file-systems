using Microsoft.Data.Sqlite;

namespace Dfs.MetadataServer.Store;

/// <summary>
/// Defines and initializes the complete SQLite schema for the Metadata Server
/// as specified in docs/data-model.md.
/// </summary>
public static class MetadataSchema
{
    /// <summary>The full DDL script for all metadata tables.</summary>
    public const string SchemaSql = """
        PRAGMA foreign_keys = ON;

        CREATE TABLE IF NOT EXISTS users (
          id TEXT PRIMARY KEY,
          username TEXT NOT NULL UNIQUE,
          password_hash TEXT NOT NULL,
          salt TEXT NOT NULL,
          disabled INTEGER NOT NULL DEFAULT 0,
          created_at TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS groups (
          id TEXT PRIMARY KEY,
          name TEXT NOT NULL UNIQUE
        );

        CREATE TABLE IF NOT EXISTS group_members (
          group_id TEXT NOT NULL,
          user_id TEXT NOT NULL,
          PRIMARY KEY (group_id, user_id),
          FOREIGN KEY (group_id) REFERENCES groups(id) ON DELETE CASCADE,
          FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS roles (
          id TEXT PRIMARY KEY,
          name TEXT NOT NULL UNIQUE,
          scope TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS role_permissions (
          role_id TEXT NOT NULL,
          permission TEXT NOT NULL,
          PRIMARY KEY (role_id, permission),
          FOREIGN KEY (role_id) REFERENCES roles(id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS grants (
          id TEXT PRIMARY KEY,
          role_id TEXT NOT NULL,
          subject_type TEXT NOT NULL,
          subject_id TEXT NOT NULL,
          item_id TEXT,
          FOREIGN KEY (role_id) REFERENCES roles(id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS items (
          id TEXT PRIMARY KEY,
          parent_id TEXT NOT NULL,
          name TEXT NOT NULL,
          kind TEXT NOT NULL,
          owner_id TEXT NOT NULL,
          current_version INTEGER NOT NULL DEFAULT 0,
          size INTEGER NOT NULL DEFAULT 0,
          modified_at TEXT NOT NULL,
          UNIQUE (parent_id, name)
        );

        CREATE TABLE IF NOT EXISTS file_versions (
          item_id TEXT NOT NULL,
          version INTEGER NOT NULL,
          size INTEGER NOT NULL,
          created_by TEXT NOT NULL,
          created_at TEXT NOT NULL,
          PRIMARY KEY (item_id, version),
          FOREIGN KEY (item_id) REFERENCES items(id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS version_chunks (
          item_id TEXT NOT NULL,
          version INTEGER NOT NULL,
          idx INTEGER NOT NULL,
          chunk_id TEXT NOT NULL,
          PRIMARY KEY (item_id, version, idx),
          FOREIGN KEY (item_id) REFERENCES items(id) ON DELETE CASCADE,
          FOREIGN KEY (chunk_id) REFERENCES chunks(id)
        );

        CREATE TABLE IF NOT EXISTS chunks (
          id TEXT PRIMARY KEY,
          size INTEGER NOT NULL,
          sha256 TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS chunk_locations (
          chunk_id TEXT NOT NULL,
          server_id TEXT NOT NULL,
          PRIMARY KEY (chunk_id, server_id),
          FOREIGN KEY (chunk_id) REFERENCES chunks(id) ON DELETE CASCADE,
          FOREIGN KEY (server_id) REFERENCES data_servers(id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS data_servers (
          id TEXT PRIMARY KEY,
          address TEXT NOT NULL,
          status TEXT NOT NULL,
          free_bytes INTEGER NOT NULL,
          last_heartbeat TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS applied_seq (
          id INTEGER PRIMARY KEY CHECK (id = 1),
          seq INTEGER NOT NULL
        );
        INSERT OR IGNORE INTO applied_seq (id, seq) VALUES (1, 0);

        CREATE TABLE IF NOT EXISTS sessions (
          token TEXT PRIMARY KEY,
          user_id TEXT NOT NULL,
          expires_at TEXT NOT NULL,
          FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS uploads (
          id TEXT PRIMARY KEY,
          item_id TEXT NOT NULL,
          base_version INTEGER NOT NULL,
          size INTEGER NOT NULL,
          chunk_size INTEGER NOT NULL,
          created_by TEXT NOT NULL,
          created_at TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS audit_log (
          seq INTEGER PRIMARY KEY AUTOINCREMENT,
          time TEXT NOT NULL,
          user_id TEXT NOT NULL,
          action TEXT NOT NULL,
          target TEXT NOT NULL,
          allowed INTEGER NOT NULL
        );
        """;

    /// <summary>
    /// Executes the full schema DDL against the provided SQLite connection.
    /// </summary>
    /// <param name="connection">An open SQLite connection.</param>
    public static void Initialize(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = SchemaSql;
        cmd.ExecuteNonQuery();
    }
}

