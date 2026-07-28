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
      new(
            7,
            "bounded-historical-telemetry",
            """
            CREATE TABLE profile_telemetry_samples (
                node_id TEXT NOT NULL,
                profile_id TEXT NOT NULL,
                observed_at TEXT NOT NULL,
                sampled_at TEXT NULL,
                recorded_at TEXT NOT NULL,
                telemetry_status TEXT NOT NULL
                    CHECK (telemetry_status IN (
                        'available',
                        'partial',
                        'unavailable',
                        'unreported')),
                manager_instance_id TEXT NOT NULL,
                manager_status TEXT NOT NULL,
                generation INTEGER NOT NULL,
                desired_slots INTEGER NOT NULL CHECK (desired_slots >= 0),
                active_slots INTEGER NOT NULL CHECK (active_slots >= 0),
                draining_slots INTEGER NOT NULL CHECK (draining_slots >= 0),
                configured_slots INTEGER NULL,
                eligible_slots INTEGER NULL,
                target_slots INTEGER NULL,
                maximum_slots INTEGER NULL,
                assigned_jobs INTEGER NULL,
                running_jobs INTEGER NULL,
                available_jobs INTEGER NULL,
                idle_runners INTEGER NULL,
                busy_runners INTEGER NULL,
                local_running_workers INTEGER NOT NULL
                    CHECK (local_running_workers >= 0),
                manager_cpu_cores REAL NULL,
                manager_memory_bytes INTEGER NULL,
                manager_pids INTEGER NULL,
                host_logical_processors INTEGER NULL,
                host_memory_bytes INTEGER NULL,
                worker_cpu_cores REAL NULL,
                worker_memory_bytes INTEGER NULL,
                worker_pids INTEGER NULL,
                network_rx_bytes INTEGER NULL,
                network_tx_bytes INTEGER NULL,
                block_read_bytes INTEGER NULL,
                block_write_bytes INTEGER NULL,
                exit_reports INTEGER NOT NULL CHECK (exit_reports >= 0),
                adverse_exit_reports INTEGER NOT NULL
                    CHECK (adverse_exit_reports >= 0),
                local_capacity_deficit INTEGER NULL,
                eligibility_capacity_deficit INTEGER NULL,
                capacity_deficit_reason TEXT NULL,
                capacity_deficit_freshness TEXT NULL
                    CHECK (capacity_deficit_freshness IS NULL
                        OR capacity_deficit_freshness IN (
                            'current',
                            'stale',
                            'unavailable')),
                PRIMARY KEY (node_id, profile_id, observed_at),
                FOREIGN KEY (node_id)
                    REFERENCES nodes(node_id) ON DELETE CASCADE
            ) WITHOUT ROWID;

            CREATE INDEX ix_profile_telemetry_samples_node_observed
                ON profile_telemetry_samples (node_id, observed_at);

            CREATE TABLE profile_telemetry_rollups (
                node_id TEXT NOT NULL,
                profile_id TEXT NOT NULL,
                bucket_start TEXT NOT NULL,
                sample_count INTEGER NOT NULL CHECK (sample_count >= 1),
                max_desired_slots INTEGER NOT NULL,
                max_active_slots INTEGER NOT NULL,
                max_draining_slots INTEGER NOT NULL,
                max_eligible_slots INTEGER NULL,
                max_local_running_workers INTEGER NOT NULL,
                max_manager_cpu_cores REAL NULL,
                max_manager_memory_bytes INTEGER NULL,
                max_manager_pids INTEGER NULL,
                max_worker_cpu_cores REAL NULL,
                max_worker_memory_bytes INTEGER NULL,
                max_worker_pids INTEGER NULL,
                max_network_rx_bytes INTEGER NULL,
                max_network_tx_bytes INTEGER NULL,
                max_block_read_bytes INTEGER NULL,
                max_block_write_bytes INTEGER NULL,
                max_exit_reports INTEGER NOT NULL,
                max_adverse_exit_reports INTEGER NOT NULL,
                max_local_capacity_deficit INTEGER NULL,
                max_eligibility_capacity_deficit INTEGER NULL,
                max_target_slots INTEGER NULL,
                max_assigned_jobs INTEGER NULL,
                max_idle_runners INTEGER NULL,
                max_busy_runners INTEGER NULL,
                PRIMARY KEY (node_id, profile_id, bucket_start),
                FOREIGN KEY (node_id)
                    REFERENCES nodes(node_id) ON DELETE CASCADE
            ) WITHOUT ROWID;

            CREATE INDEX ix_profile_telemetry_rollups_node_bucket
                ON profile_telemetry_rollups (node_id, bucket_start);

            CREATE TABLE profile_manager_events (
                node_id TEXT NOT NULL,
                profile_id TEXT NOT NULL,
                epoch INTEGER NOT NULL CHECK (epoch >= 0),
                sequence INTEGER NOT NULL CHECK (sequence >= 0),
                manager_instance_id TEXT NOT NULL,
                observed_at TEXT NOT NULL,
                recorded_at TEXT NOT NULL,
                subsystem TEXT NOT NULL,
                operation TEXT NOT NULL,
                target TEXT NULL,
                outcome TEXT NOT NULL,
                duration_milliseconds INTEGER NULL,
                attempt INTEGER NULL,
                consecutive_failures INTEGER NULL,
                retry_at TEXT NULL,
                reason TEXT NOT NULL,
                evidence TEXT NULL,
                PRIMARY KEY (node_id, profile_id, epoch, sequence),
                FOREIGN KEY (node_id)
                    REFERENCES nodes(node_id) ON DELETE CASCADE
            ) WITHOUT ROWID;

            CREATE INDEX ix_profile_manager_events_observed
                ON profile_manager_events (node_id, profile_id, observed_at);

            CREATE INDEX ix_profile_manager_events_node_observed
                ON profile_manager_events (node_id, observed_at);

            CREATE TABLE profile_subsystem_health (
                node_id TEXT NOT NULL,
                profile_id TEXT NOT NULL,
                subsystem TEXT NOT NULL CHECK (subsystem IN ('docker', 'github')),
                observed_at TEXT NOT NULL,
                recorded_at TEXT NOT NULL,
                state TEXT NOT NULL,
                consecutive_failures INTEGER NOT NULL
                    CHECK (consecutive_failures >= 0),
                retry_at TEXT NULL,
                last_success_operation TEXT NULL,
                last_success_observed_at TEXT NULL,
                last_success_reason TEXT NULL,
                last_failure_operation TEXT NULL,
                last_failure_observed_at TEXT NULL,
                last_failure_reason TEXT NULL,
                last_failure_evidence TEXT NULL,
                PRIMARY KEY (node_id, profile_id, subsystem, observed_at),
                FOREIGN KEY (node_id)
                    REFERENCES nodes(node_id) ON DELETE CASCADE
            ) WITHOUT ROWID;

            CREATE INDEX ix_profile_subsystem_health_node_observed
                ON profile_subsystem_health (node_id, observed_at);

            CREATE TABLE profile_capacity_deficits (
                node_id TEXT NOT NULL,
                profile_id TEXT NOT NULL,
                target_key TEXT NOT NULL,
                observed_at TEXT NOT NULL,
                recorded_at TEXT NOT NULL,
                repository TEXT NULL,
                freshness TEXT NOT NULL
                    CHECK (freshness IN ('current', 'stale', 'unavailable')),
                target_slots INTEGER NOT NULL,
                active_workers INTEGER NOT NULL,
                starting_workers INTEGER NOT NULL,
                draining_workers INTEGER NOT NULL,
                cleanup_pending_workers INTEGER NOT NULL,
                eligible_workers INTEGER NULL,
                local_deficit INTEGER NOT NULL,
                eligibility_deficit INTEGER NULL,
                reason TEXT NOT NULL,
                evidence TEXT NULL,
                PRIMARY KEY (node_id, profile_id, target_key, observed_at),
                FOREIGN KEY (node_id)
                    REFERENCES nodes(node_id) ON DELETE CASCADE
            ) WITHOUT ROWID;

            CREATE INDEX ix_profile_capacity_deficits_node_observed
                ON profile_capacity_deficits (node_id, observed_at);

            CREATE TABLE profile_history_cursors (
                node_id TEXT NOT NULL,
                profile_id TEXT NOT NULL,
                journal_status TEXT NOT NULL
                    CHECK (journal_status IN (
                        'current',
                        'truncated',
                        'unavailable',
                        'unreported')),
                journal_capacity INTEGER NOT NULL
                    CHECK (journal_capacity >= 0),
                epoch INTEGER NOT NULL CHECK (epoch >= 0),
                epoch_resets INTEGER NOT NULL CHECK (epoch_resets >= 0),
                manager_highest_sequence INTEGER NULL,
                manager_dropped_events INTEGER NOT NULL
                    CHECK (manager_dropped_events >= 0),
                stored_highest_sequence INTEGER NULL,
                missed_events INTEGER NOT NULL
                    CHECK (missed_events >= 0),
                dropped_samples INTEGER NOT NULL CHECK (dropped_samples >= 0),
                dropped_rollups INTEGER NOT NULL CHECK (dropped_rollups >= 0),
                dropped_events INTEGER NOT NULL CHECK (dropped_events >= 0),
                dropped_subsystem_health INTEGER NOT NULL
                    CHECK (dropped_subsystem_health >= 0),
                dropped_capacity_deficits INTEGER NOT NULL
                    CHECK (dropped_capacity_deficits >= 0),
                rejected_future_samples INTEGER NOT NULL
                    CHECK (rejected_future_samples >= 0),
                rejected_future_events INTEGER NOT NULL
                    CHECK (rejected_future_events >= 0),
                updated_at TEXT NOT NULL,
                PRIMARY KEY (node_id, profile_id),
                FOREIGN KEY (node_id)
                    REFERENCES nodes(node_id) ON DELETE CASCADE
            ) WITHOUT ROWID;
            """),
      new(
            8,
            "durable-history-identity-and-global-bounds",
            """
            ALTER TABLE profile_history_cursors
                ADD COLUMN sample_high_water TEXT NULL;

            UPDATE profile_history_cursors
            SET sample_high_water = (
                SELECT MAX(s.observed_at)
                FROM profile_telemetry_samples AS s
                WHERE s.node_id = profile_history_cursors.node_id
                  AND s.profile_id = profile_history_cursors.profile_id);

            ALTER TABLE profile_subsystem_health
                ADD COLUMN revisions INTEGER NOT NULL DEFAULT 0;

            ALTER TABLE profile_capacity_deficits
                ADD COLUMN revisions INTEGER NOT NULL DEFAULT 0;

            CREATE TABLE profile_event_identities (
                node_id TEXT NOT NULL,
                profile_id TEXT NOT NULL,
                epoch INTEGER NOT NULL CHECK (epoch >= 0),
                sequence INTEGER NOT NULL CHECK (sequence >= 0),
                fingerprint TEXT NOT NULL,
                observed_at TEXT NOT NULL,
                recorded_at TEXT NOT NULL,
                PRIMARY KEY (node_id, profile_id, epoch, sequence),
                FOREIGN KEY (node_id)
                    REFERENCES nodes(node_id) ON DELETE CASCADE
            ) WITHOUT ROWID;

            INSERT INTO profile_event_identities (
                node_id,
                profile_id,
                epoch,
                sequence,
                fingerprint,
                observed_at,
                recorded_at)
            SELECT
                node_id,
                profile_id,
                epoch,
                sequence,
                '',
                observed_at,
                recorded_at
            FROM (
                SELECT
                    node_id,
                    profile_id,
                    epoch,
                    sequence,
                    observed_at,
                    recorded_at,
                    ROW_NUMBER() OVER (
                        PARTITION BY node_id, profile_id
                        ORDER BY epoch DESC, sequence DESC) AS rank_index
                FROM profile_manager_events)
            WHERE rank_index <= 64;

            CREATE TABLE profile_history_tombstones (
                node_id TEXT NOT NULL,
                profile_id TEXT NOT NULL,
                expired_at TEXT NOT NULL,
                epoch INTEGER NOT NULL CHECK (epoch >= 0),
                epoch_resets INTEGER NOT NULL CHECK (epoch_resets >= 0),
                sample_high_water TEXT NULL,
                stored_highest_sequence INTEGER NULL,
                manager_dropped_events INTEGER NOT NULL
                    CHECK (manager_dropped_events >= 0),
                missed_events INTEGER NOT NULL CHECK (missed_events >= 0),
                dropped_samples INTEGER NOT NULL CHECK (dropped_samples >= 0),
                dropped_rollups INTEGER NOT NULL CHECK (dropped_rollups >= 0),
                dropped_events INTEGER NOT NULL CHECK (dropped_events >= 0),
                dropped_subsystem_health INTEGER NOT NULL
                    CHECK (dropped_subsystem_health >= 0),
                dropped_capacity_deficits INTEGER NOT NULL
                    CHECK (dropped_capacity_deficits >= 0),
                rejected_future_samples INTEGER NOT NULL
                    CHECK (rejected_future_samples >= 0),
                rejected_future_events INTEGER NOT NULL
                    CHECK (rejected_future_events >= 0),
                PRIMARY KEY (node_id, profile_id),
                FOREIGN KEY (node_id)
                    REFERENCES nodes(node_id) ON DELETE CASCADE
            ) WITHOUT ROWID;

            CREATE TABLE history_maintenance (
                singleton INTEGER NOT NULL PRIMARY KEY CHECK (singleton = 0),
                last_swept_at TEXT NULL
            );

            INSERT INTO history_maintenance (singleton, last_swept_at)
            VALUES (0, NULL);
            """),
      new(
            9,
            "conservative-high-water-and-incompleteness-floors",
            """
            -- Migration 8 backfilled the durable sample high-water from surviving raw samples
            -- alone. Raw samples are the shortest-lived evidence in the database, so a realistic
            -- upgrade where raw retention already pruned them left the high-water NULL and let the
            -- first stale heartbeat after the upgrade reinsert an old sample and inflate the hourly
            -- rollup it had already contributed to. The authoritative latest profile projection,
            -- the retained hourly buckets, and any high-water already recorded are all independent
            -- evidence of an observation the dashboard has already accounted for, so the high-water
            -- is raised to the newest of them.
            --
            -- The latest projection keeps the exact authoritative observation time. A retained
            -- rollup is used only when the latest projection is absent; its bucket end is a
            -- conservative high-water for a profile that is no longer live.
            UPDATE profile_history_cursors
            SET sample_high_water = NULLIF(
                MAX(
                    COALESCE(sample_high_water, ''),
                    COALESCE((
                        SELECT p.observed_at
                        FROM profiles AS p
                        WHERE p.node_id = profile_history_cursors.node_id
                          AND p.profile_id = profile_history_cursors.profile_id), ''),
                    COALESCE((
                        SELECT MAX(s.observed_at)
                        FROM profile_telemetry_samples AS s
                        WHERE s.node_id = profile_history_cursors.node_id
                          AND s.profile_id = profile_history_cursors.profile_id), ''),
                    COALESCE((
                        SELECT strftime(
                            '%Y-%m-%dT%H:%M:%S.0000000+00:00',
                            MAX(r.bucket_start),
                            '+1 hour')
                        FROM profile_telemetry_rollups AS r
                        WHERE r.node_id = profile_history_cursors.node_id
                          AND r.profile_id = profile_history_cursors.profile_id
                          AND NOT EXISTS (
                              SELECT 1
                              FROM profiles AS p
                              WHERE p.node_id = profile_history_cursors.node_id
                                AND p.profile_id = profile_history_cursors.profile_id)), '')),
                '');

            ALTER TABLE profile_history_cursors
                ADD COLUMN history_expired_at TEXT NULL;

            CREATE TABLE history_incompleteness_floors (
                scope TEXT NOT NULL CHECK (scope IN ('database', 'node')),
                node_id TEXT NOT NULL,
                earliest_expired_at TEXT NOT NULL,
                latest_expired_at TEXT NOT NULL,
                expired_profiles INTEGER NOT NULL
                    CHECK (expired_profiles >= 0),
                dropped_samples INTEGER NOT NULL CHECK (dropped_samples >= 0),
                dropped_rollups INTEGER NOT NULL CHECK (dropped_rollups >= 0),
                dropped_events INTEGER NOT NULL CHECK (dropped_events >= 0),
                dropped_subsystem_health INTEGER NOT NULL
                    CHECK (dropped_subsystem_health >= 0),
                dropped_capacity_deficits INTEGER NOT NULL
                    CHECK (dropped_capacity_deficits >= 0),
                PRIMARY KEY (scope, node_id)
            ) WITHOUT ROWID;
            """),
      new(
            10,
            "restart-safe-alert-incidents",
            """
            CREATE TABLE alert_incidents (
                incident_id TEXT PRIMARY KEY,
                alert_key TEXT NOT NULL CHECK (length(alert_key) BETWEEN 1 AND 512),
                tenant_id TEXT NOT NULL,
                node_id TEXT NOT NULL,
                profile_id TEXT NULL CHECK (
                    profile_id IS NULL OR length(profile_id) BETWEEN 1 AND 128),
                kind TEXT NOT NULL CHECK (length(kind) BETWEEN 1 AND 64),
                severity TEXT NOT NULL CHECK (severity IN ('warning', 'critical')),
                status TEXT NOT NULL CHECK (status IN (
                    'pending',
                    'triggered',
                    'acknowledged',
                    'resolved')),
                title TEXT NOT NULL CHECK (length(title) BETWEEN 1 AND 160),
                summary TEXT NOT NULL CHECK (length(summary) BETWEEN 1 AND 512),
                reason TEXT NOT NULL CHECK (length(reason) BETWEEN 1 AND 128),
                evidence TEXT NULL CHECK (
                    evidence IS NULL OR length(evidence) <= 512),
                link TEXT NOT NULL CHECK (length(link) BETWEEN 1 AND 2048),
                first_observed_at TEXT NOT NULL,
                trigger_after TEXT NOT NULL,
                last_observed_at TEXT NOT NULL,
                triggered_at TEXT NULL,
                acknowledged_at TEXT NULL,
                acknowledged_by_github_user_id TEXT NULL,
                resolved_at TEXT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                FOREIGN KEY (tenant_id)
                    REFERENCES tenants(tenant_id) ON DELETE CASCADE,
                FOREIGN KEY (acknowledged_by_github_user_id)
                    REFERENCES dashboard_users(github_user_id),
                CHECK (
                    (status = 'pending'
                        AND triggered_at IS NULL
                        AND acknowledged_at IS NULL
                        AND acknowledged_by_github_user_id IS NULL
                        AND resolved_at IS NULL)
                    OR (status = 'triggered'
                        AND triggered_at IS NOT NULL
                        AND acknowledged_at IS NULL
                        AND acknowledged_by_github_user_id IS NULL
                        AND resolved_at IS NULL)
                    OR (status = 'acknowledged'
                        AND triggered_at IS NOT NULL
                        AND acknowledged_at IS NOT NULL
                        AND acknowledged_by_github_user_id IS NOT NULL
                        AND resolved_at IS NULL)
                    OR (status = 'resolved'
                        AND triggered_at IS NOT NULL
                        AND resolved_at IS NOT NULL))
            );

            CREATE UNIQUE INDEX ix_alert_incidents_open_key
                ON alert_incidents (alert_key)
                WHERE status IN ('pending', 'triggered', 'acknowledged');

            CREATE INDEX ix_alert_incidents_tenant_status_updated
                ON alert_incidents (
                    tenant_id,
                    status,
                    updated_at DESC,
                    incident_id DESC);

            CREATE INDEX ix_alert_incidents_tenant_resolved
                ON alert_incidents (
                    tenant_id,
                    resolved_at DESC,
                    incident_id DESC)
                WHERE status = 'resolved';

            CREATE INDEX ix_capacity_commands_profile_requested
                ON capacity_commands (
                    node_id,
                    profile_id,
                    requested_at DESC,
                    command_id DESC);

            CREATE INDEX ix_recovery_commands_profile_requested
                ON recovery_commands (
                    node_id,
                    profile_id,
                    requested_at DESC,
                    command_id DESC);

            CREATE TRIGGER trg_alert_incidents_identity_immutable
            BEFORE UPDATE ON alert_incidents
            FOR EACH ROW
            WHEN OLD.incident_id <> NEW.incident_id
              OR OLD.alert_key <> NEW.alert_key
              OR OLD.tenant_id <> NEW.tenant_id
              OR OLD.node_id <> NEW.node_id
              OR IFNULL(OLD.profile_id, '') <> IFNULL(NEW.profile_id, '')
              OR OLD.kind <> NEW.kind
              OR OLD.created_at <> NEW.created_at
            BEGIN
                SELECT RAISE(
                    ABORT,
                    'alert incident identity is immutable');
            END;

            CREATE TRIGGER trg_alert_incidents_transitions
            BEFORE UPDATE OF status ON alert_incidents
            FOR EACH ROW
            WHEN NOT (
                OLD.status = NEW.status
                OR (OLD.status = 'pending' AND NEW.status = 'triggered')
                OR (OLD.status = 'triggered' AND NEW.status IN (
                        'acknowledged',
                        'resolved'))
                OR (OLD.status = 'acknowledged'
                    AND NEW.status = 'resolved'))
            BEGIN
                SELECT RAISE(
                    ABORT,
                    'invalid alert incident status transition');
            END;

            CREATE TRIGGER trg_alert_incidents_resolved_immutable
            BEFORE UPDATE ON alert_incidents
            FOR EACH ROW
            WHEN OLD.status = 'resolved'
            BEGIN
                SELECT RAISE(
                    ABORT,
                    'resolved alert incidents are immutable');
            END;
            """),
    ];
}
