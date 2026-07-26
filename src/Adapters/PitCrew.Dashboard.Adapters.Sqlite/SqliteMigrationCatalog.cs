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
      new(
            5,
            "capacity-operation-queue",
            """
            ALTER TABLE nodes
                ADD COLUMN capacity_capability_json TEXT NULL;

            ALTER TABLE nodes
                ADD COLUMN capacity_capability_at TEXT NULL;

            CREATE TABLE capacity_commands (
                command_id TEXT PRIMARY KEY,
                node_id TEXT NOT NULL,
                profile_id TEXT NOT NULL,
                expected_generation INTEGER NOT NULL
                    CHECK (expected_generation >= 1),
                requested_maximum INTEGER NOT NULL
                    CHECK (requested_maximum >= 1),
                maximum_allowed_at_request INTEGER NOT NULL
                    CHECK (maximum_allowed_at_request >= requested_maximum),
                status TEXT NOT NULL
                    CHECK (status IN (
                        'pending',
                        'delivered',
                        'succeeded',
                        'rejected',
                        'failed')),
                requested_by_github_user_id TEXT NOT NULL,
                requested_at TEXT NOT NULL,
                expires_at TEXT NOT NULL,
                delivered_at TEXT NULL,
                delivery_attempts INTEGER NOT NULL DEFAULT 0
                    CHECK (delivery_attempts >= 0),
                completed_at TEXT NULL,
                accepted_generation INTEGER NULL,
                result_message TEXT NULL,
                FOREIGN KEY (node_id)
                    REFERENCES nodes(node_id) ON DELETE CASCADE,
                FOREIGN KEY (requested_by_github_user_id)
                    REFERENCES dashboard_users(github_user_id)
            );

            CREATE UNIQUE INDEX ix_capacity_commands_profile_active
                ON capacity_commands (node_id, profile_id)
                WHERE status IN ('pending', 'delivered');

            CREATE INDEX ix_capacity_commands_node_requested
                ON capacity_commands (node_id, requested_at DESC);
            """),
      new(
            6,
            "manager-recovery-operations",
            """
            CREATE TABLE profile_active_operations (
                node_id TEXT NOT NULL,
                profile_id TEXT NOT NULL,
                operation_kind TEXT NOT NULL
                    CHECK (operation_kind IN ('capacity', 'recovery')),
                command_id TEXT NOT NULL UNIQUE,
                acquired_at TEXT NOT NULL,
                PRIMARY KEY (node_id, profile_id),
                FOREIGN KEY (node_id)
                    REFERENCES nodes(node_id) ON DELETE CASCADE
            );

            INSERT INTO profile_active_operations (
                node_id,
                profile_id,
                operation_kind,
                command_id,
                acquired_at)
            SELECT
                node_id,
                profile_id,
                'capacity',
                command_id,
                requested_at
            FROM capacity_commands
            WHERE status IN ('pending', 'delivered');

            ALTER TABLE nodes
                ADD COLUMN recovery_capability_json TEXT NULL;

            ALTER TABLE nodes
                ADD COLUMN recovery_capability_at TEXT NULL;

            CREATE TABLE recovery_commands (
                command_id TEXT PRIMARY KEY,
                node_id TEXT NOT NULL,
                profile_id TEXT NOT NULL,
                expected_manager_instance_id TEXT NOT NULL
                    CHECK (length(expected_manager_instance_id)
                        BETWEEN 1 AND 128),
                expected_generation INTEGER NOT NULL
                    CHECK (expected_generation >= 0),
                expected_desired_state_hash TEXT NULL
                    CHECK (expected_desired_state_hash IS NULL
                        OR length(expected_desired_state_hash) = 64),
                status TEXT NOT NULL
                    CHECK (status IN (
                        'queued',
                        'claimed',
                        'started',
                        'succeeded',
                        'rejected',
                        'failed',
                        'expired',
                        'indeterminate')),
                failure_category TEXT NULL
                    CHECK (failure_category IS NULL
                        OR failure_category IN (
                            'not-allowed',
                            'stale-fence',
                            'expired',
                            'manager-unresolved',
                            'operation-active',
                            'timeout',
                            'process-failure',
                            'unknown')),
                requested_by_github_user_id TEXT NOT NULL,
                requested_at TEXT NOT NULL,
                expires_at TEXT NOT NULL,
                delivered_at TEXT NULL,
                delivery_attempts INTEGER NOT NULL DEFAULT 0
                    CHECK (delivery_attempts >= 0),
                claimed_at TEXT NULL,
                started_at TEXT NULL,
                completed_at TEXT NULL,
                before_manager_instance_id TEXT NULL
                    CHECK (before_manager_instance_id IS NULL
                        OR length(before_manager_instance_id) <= 128),
                after_manager_instance_id TEXT NULL
                    CHECK (after_manager_instance_id IS NULL
                        OR length(after_manager_instance_id) <= 128),
                result_message TEXT NULL
                    CHECK (result_message IS NULL
                        OR length(result_message) <= 512),
                FOREIGN KEY (node_id)
                    REFERENCES nodes(node_id) ON DELETE CASCADE,
                FOREIGN KEY (requested_by_github_user_id)
                    REFERENCES dashboard_users(github_user_id)
            );

            CREATE UNIQUE INDEX ix_recovery_commands_profile_active
                ON recovery_commands (node_id, profile_id)
                WHERE status IN ('queued', 'claimed', 'started');

            CREATE INDEX ix_recovery_commands_node_requested
                ON recovery_commands (node_id, requested_at DESC);

            CREATE TRIGGER trg_capacity_commands_require_operation_slot
            BEFORE INSERT ON capacity_commands
            FOR EACH ROW
            WHEN NOT EXISTS (
                SELECT 1
                FROM profile_active_operations
                WHERE node_id = NEW.node_id
                  AND profile_id = NEW.profile_id
                  AND command_id = NEW.command_id
                  AND operation_kind = 'capacity')
            BEGIN
                SELECT RAISE(
                    ABORT,
                    'capacity command requires an exclusive profile operation');
            END;

            CREATE TRIGGER trg_recovery_commands_require_operation_slot
            BEFORE INSERT ON recovery_commands
            FOR EACH ROW
            WHEN NOT EXISTS (
                SELECT 1
                FROM profile_active_operations
                WHERE node_id = NEW.node_id
                  AND profile_id = NEW.profile_id
                  AND command_id = NEW.command_id
                  AND operation_kind = 'recovery')
            BEGIN
                SELECT RAISE(
                    ABORT,
                    'recovery command requires an exclusive profile operation');
            END;

            CREATE TRIGGER trg_recovery_commands_insert_queued
            BEFORE INSERT ON recovery_commands
            FOR EACH ROW
            WHEN NEW.status <> 'queued'
              OR NEW.claimed_at IS NOT NULL
              OR NEW.started_at IS NOT NULL
              OR NEW.completed_at IS NOT NULL
              OR NEW.failure_category IS NOT NULL
            BEGIN
                SELECT RAISE(
                    ABORT,
                    'recovery commands must be inserted as queued');
            END;

            CREATE TRIGGER trg_recovery_commands_immutable
            BEFORE UPDATE ON recovery_commands
            FOR EACH ROW
            WHEN OLD.status IN (
                    'succeeded',
                    'rejected',
                    'failed',
                    'expired',
                    'indeterminate')
              OR OLD.command_id <> NEW.command_id
              OR OLD.node_id <> NEW.node_id
              OR OLD.profile_id <> NEW.profile_id
              OR OLD.requested_by_github_user_id
                  <> NEW.requested_by_github_user_id
              OR OLD.requested_at <> NEW.requested_at
              OR OLD.expires_at <> NEW.expires_at
              OR OLD.expected_manager_instance_id
                  <> NEW.expected_manager_instance_id
              OR OLD.expected_generation <> NEW.expected_generation
              OR IFNULL(OLD.expected_desired_state_hash, '')
                  <> IFNULL(NEW.expected_desired_state_hash, '')
            BEGIN
                SELECT RAISE(
                    ABORT,
                    'recovery command audit data and terminal outcomes are immutable');
            END;

            CREATE TRIGGER trg_recovery_commands_transitions
            BEFORE UPDATE OF status ON recovery_commands
            FOR EACH ROW
            WHEN NOT (
                OLD.status = NEW.status
                OR (OLD.status = 'queued' AND NEW.status IN (
                        'claimed',
                        'started',
                        'succeeded',
                        'rejected',
                        'failed',
                        'expired',
                        'indeterminate'))
                OR (OLD.status = 'claimed' AND NEW.status IN (
                        'started',
                        'succeeded',
                        'rejected',
                        'failed',
                        'expired',
                        'indeterminate'))
                OR (OLD.status = 'started' AND NEW.status IN (
                        'succeeded',
                        'rejected',
                        'failed',
                        'indeterminate')))
            BEGIN
                SELECT RAISE(
                    ABORT,
                    'recovery command lifecycle transition is not allowed');
            END;

            CREATE TRIGGER trg_recovery_commands_terminal_evidence
            BEFORE UPDATE ON recovery_commands
            FOR EACH ROW
            WHEN NEW.status IN (
                    'succeeded',
                    'rejected',
                    'failed',
                    'expired',
                    'indeterminate')
              AND (NEW.completed_at IS NULL
                OR (NEW.status = 'succeeded'
                    AND (NEW.failure_category IS NOT NULL
                        OR NEW.after_manager_instance_id IS NULL))
                OR (NEW.status <> 'succeeded'
                    AND NEW.failure_category IS NULL))
            BEGIN
                SELECT RAISE(
                    ABORT,
                    'recovery command terminal state requires bounded evidence');
            END;
            """),
    ];
}
