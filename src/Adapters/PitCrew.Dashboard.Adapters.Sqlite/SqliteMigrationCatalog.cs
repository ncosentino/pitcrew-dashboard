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
      new(
            2,
            "dashboard-users-and-tenant-memberships",
            """
            ALTER TABLE tenants
                ADD COLUMN display_name TEXT NOT NULL DEFAULT '';

            ALTER TABLE tenants
                ADD COLUMN created_at TEXT NOT NULL DEFAULT '1970-01-01T00:00:00.0000000+00:00';

            UPDATE tenants
            SET display_name = tenant_id
            WHERE display_name = '';

            CREATE TABLE dashboard_users (
                github_user_id TEXT PRIMARY KEY,
                github_login TEXT NOT NULL,
                display_name TEXT NOT NULL,
                avatar_url TEXT NULL,
                first_seen_at TEXT NOT NULL,
                last_seen_at TEXT NOT NULL
            );

            CREATE INDEX ix_dashboard_users_login
                ON dashboard_users (github_login COLLATE NOCASE);

            CREATE TABLE tenant_memberships (
                tenant_id TEXT NOT NULL,
                github_user_id TEXT NOT NULL,
                role TEXT NOT NULL
                    CHECK (role IN ('viewer', 'administrator', 'owner')),
                created_at TEXT NOT NULL,
                created_by_github_user_id TEXT NULL,
                PRIMARY KEY (tenant_id, github_user_id),
                FOREIGN KEY (tenant_id)
                    REFERENCES tenants(tenant_id) ON DELETE CASCADE,
                FOREIGN KEY (github_user_id)
                    REFERENCES dashboard_users(github_user_id) ON DELETE CASCADE,
                FOREIGN KEY (created_by_github_user_id)
                    REFERENCES dashboard_users(github_user_id)
            );

            CREATE INDEX ix_tenant_memberships_user
                ON tenant_memberships (github_user_id, tenant_id);
            """),
      new(
            3,
            "one-time-enrollment-and-node-credentials",
            """
            CREATE TABLE enrollment_codes (
                enrollment_code_id TEXT PRIMARY KEY,
                tenant_id TEXT NOT NULL,
                code_hash TEXT NOT NULL UNIQUE,
                label TEXT NOT NULL,
                created_by_github_user_id TEXT NOT NULL,
                created_at TEXT NOT NULL,
                expires_at TEXT NOT NULL,
                consumed_at TEXT NULL,
                consumed_by_node_id TEXT NULL,
                FOREIGN KEY (tenant_id)
                    REFERENCES tenants(tenant_id) ON DELETE CASCADE,
                FOREIGN KEY (created_by_github_user_id)
                    REFERENCES dashboard_users(github_user_id),
                FOREIGN KEY (consumed_by_node_id)
                    REFERENCES nodes(node_id)
            );

            CREATE INDEX ix_enrollment_codes_tenant_expiry
                ON enrollment_codes (tenant_id, expires_at);

            ALTER TABLE nodes
                ADD COLUMN revoked_at TEXT NULL;

            ALTER TABLE nodes
                ADD COLUMN rotation_requested_at TEXT NULL;

            ALTER TABLE nodes
                ADD COLUMN pending_credential_hash TEXT NULL;

            ALTER TABLE nodes
                ADD COLUMN credential_rotated_at TEXT NULL;

            CREATE UNIQUE INDEX ix_nodes_pending_credential_hash
                ON nodes (pending_credential_hash)
                WHERE pending_credential_hash IS NOT NULL;
            """),
      new(
            4,
            "node-display-name-overrides",
            """
            ALTER TABLE nodes
                ADD COLUMN display_name_override TEXT NULL;
            """),
    ];
}
