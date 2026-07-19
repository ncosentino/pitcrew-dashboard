using System.Security.Cryptography;
using System.Text;

namespace PitCrew.Dashboard.Adapters.Sqlite;

internal sealed record SqliteMigration(
    int Version,
    string Name,
    string Sql)
{
  public string Checksum { get; } =
      Convert.ToHexString(
          SHA256.HashData(Encoding.UTF8.GetBytes(Sql)));
}

internal static class SqliteMigrationCatalog
{
  public static IReadOnlyList<SqliteMigration> All { get; } =
  [
      new(
            1,
            "identity-and-current-fleet",
            """
            CREATE TABLE tenants (
                tenant_id TEXT PRIMARY KEY
            );

            CREATE TABLE nodes (
                node_id TEXT PRIMARY KEY,
                tenant_id TEXT NOT NULL,
                connector_instance_id TEXT NOT NULL,
                display_name TEXT NOT NULL,
                credential_hash TEXT NOT NULL UNIQUE,
                connector_version TEXT NOT NULL DEFAULT '',
                enrolled_at TEXT NOT NULL,
                last_seen_at TEXT NULL,
                FOREIGN KEY (tenant_id) REFERENCES tenants(tenant_id),
                UNIQUE (tenant_id, connector_instance_id)
            );

            CREATE TABLE profiles (
                node_id TEXT NOT NULL,
                profile_id TEXT NOT NULL,
                payload_hash TEXT NOT NULL,
                payload_json TEXT NOT NULL,
                observed_at TEXT NOT NULL,
                PRIMARY KEY (node_id, profile_id),
                FOREIGN KEY (node_id) REFERENCES nodes(node_id) ON DELETE CASCADE
            );

            CREATE INDEX ix_nodes_tenant_last_seen
                ON nodes (tenant_id, last_seen_at);
            """),
    ];
}
