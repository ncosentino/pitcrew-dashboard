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
      new(
            11,
            "worker-image-rollout-history",
            """
            ALTER TABLE profile_telemetry_samples
                ADD COLUMN worker_update_status TEXT NULL
                    CHECK (worker_update_status IS NULL
                        OR worker_update_status IN (
                            'current',
                            'rolling',
                            'degraded'));

            ALTER TABLE profile_telemetry_samples
                ADD COLUMN worker_target_image TEXT NULL
                    CHECK (worker_target_image IS NULL
                        OR length(worker_target_image) BETWEEN 1 AND 2048);

            ALTER TABLE profile_telemetry_samples
                ADD COLUMN worker_target_image_id TEXT NULL
                    CHECK (worker_target_image_id IS NULL
                        OR (length(worker_target_image_id) = 71
                            AND substr(worker_target_image_id, 1, 7) = 'sha256:'
                            AND substr(worker_target_image_id, 8)
                                NOT GLOB '*[^0-9a-f]*'));

            ALTER TABLE profile_telemetry_samples
                ADD COLUMN worker_target_revision TEXT NULL
                    CHECK (worker_target_revision IS NULL
                        OR (length(worker_target_revision) = 64
                            AND worker_target_revision NOT GLOB '*[^0-9a-f]*'));

            ALTER TABLE profile_telemetry_samples
                ADD COLUMN worker_current_workers INTEGER NULL
                    CHECK (worker_current_workers IS NULL
                        OR worker_current_workers >= 0);

            ALTER TABLE profile_telemetry_samples
                ADD COLUMN worker_stale_workers INTEGER NULL
                    CHECK (worker_stale_workers IS NULL
                        OR worker_stale_workers >= 0);

            ALTER TABLE profile_telemetry_samples
                ADD COLUMN worker_update_error TEXT NULL
                    CHECK (worker_update_error IS NULL
                        OR length(worker_update_error) <= 512);
            """),
        new(
              12,
              "scoped-diagnostic-credentials",
              """
              CREATE TABLE diagnostic_credentials (
                  credential_id TEXT PRIMARY KEY,
                  tenant_id TEXT NOT NULL,
                  label TEXT NOT NULL,
                  token_hash TEXT NOT NULL UNIQUE,
                  created_by_github_user_id TEXT NOT NULL,
                  created_at TEXT NOT NULL,
                  expires_at TEXT NOT NULL,
                  revoked_at TEXT NULL,
                  revoked_by_github_user_id TEXT NULL,
                  rotated_from_credential_id TEXT NULL,
                  last_used_at TEXT NULL,
                  use_count INTEGER NOT NULL DEFAULT 0
                      CHECK (use_count >= 0),
                  FOREIGN KEY (tenant_id)
                      REFERENCES tenants(tenant_id) ON DELETE CASCADE,
                  FOREIGN KEY (created_by_github_user_id)
                      REFERENCES dashboard_users(github_user_id),
                  FOREIGN KEY (revoked_by_github_user_id)
                      REFERENCES dashboard_users(github_user_id),
                  FOREIGN KEY (rotated_from_credential_id)
                      REFERENCES diagnostic_credentials(credential_id)
              );

              CREATE INDEX ix_diagnostic_credentials_tenant_created
                  ON diagnostic_credentials (tenant_id, created_at DESC);

              CREATE INDEX ix_diagnostic_credentials_active_expiry
                  ON diagnostic_credentials (tenant_id, expires_at)
                  WHERE revoked_at IS NULL;

              CREATE TABLE diagnostic_credential_nodes (
                  credential_id TEXT NOT NULL,
                  node_id TEXT NOT NULL,
                  PRIMARY KEY (credential_id, node_id),
                  FOREIGN KEY (credential_id)
                      REFERENCES diagnostic_credentials(credential_id)
                      ON DELETE CASCADE,
                  FOREIGN KEY (node_id)
                      REFERENCES nodes(node_id)
                      ON DELETE CASCADE
              ) WITHOUT ROWID;

              CREATE TABLE diagnostic_credential_profiles (
                  credential_id TEXT NOT NULL,
                  profile_id TEXT NOT NULL,
                  PRIMARY KEY (credential_id, profile_id),
                  FOREIGN KEY (credential_id)
                      REFERENCES diagnostic_credentials(credential_id)
                      ON DELETE CASCADE
              ) WITHOUT ROWID;
              """),
        new(
              13,
              "node-hardware-inventory-history",
              """
              ALTER TABLE history_incompleteness_floors
                  ADD COLUMN dropped_hardware_revisions INTEGER NOT NULL
                      DEFAULT 0
                      CHECK (dropped_hardware_revisions >= 0);

              CREATE TABLE node_hardware_current (
                  node_id TEXT PRIMARY KEY,
                  status TEXT NOT NULL
                      CHECK (status IN (
                          'current',
                          'stale',
                          'unavailable')),
                  collected_at TEXT NULL,
                  attempted_at TEXT NOT NULL,
                  inventory_hash TEXT NULL
                      CHECK (inventory_hash IS NULL
                          OR (length(inventory_hash) = 64
                              AND inventory_hash
                                  NOT GLOB '*[^0-9a-f]*')),
                  source_profile_id TEXT NOT NULL,
                  processor_model TEXT NULL,
                  architecture TEXT NULL,
                  physical_core_count INTEGER NULL,
                  logical_processor_count INTEGER NULL,
                  performance_core_count INTEGER NULL,
                  efficiency_core_count INTEGER NULL,
                  memory_bytes INTEGER NULL,
                  operating_system TEXT NULL,
                  kernel_version TEXT NULL,
                  docker_server_version TEXT NULL,
                  docker_storage_driver TEXT NULL,
                  docker_backing_filesystem TEXT NULL,
                  recorded_at TEXT NOT NULL,
                  FOREIGN KEY (node_id)
                      REFERENCES nodes(node_id) ON DELETE CASCADE
              );

              CREATE TABLE node_hardware_revisions (
                  revision_id INTEGER PRIMARY KEY,
                  node_id TEXT NOT NULL,
                  inventory_hash TEXT NOT NULL
                      CHECK (length(inventory_hash) = 64
                          AND inventory_hash
                              NOT GLOB '*[^0-9a-f]*'),
                  collected_at TEXT NOT NULL,
                  first_observed_at TEXT NOT NULL,
                  last_observed_at TEXT NOT NULL,
                  last_status TEXT NOT NULL
                      CHECK (last_status IN ('current', 'stale')),
                  last_attempted_at TEXT NOT NULL,
                  source_profile_id TEXT NOT NULL,
                  processor_model TEXT NULL,
                  architecture TEXT NULL,
                  physical_core_count INTEGER NULL,
                  logical_processor_count INTEGER NULL,
                  performance_core_count INTEGER NULL,
                  efficiency_core_count INTEGER NULL,
                  memory_bytes INTEGER NULL,
                  operating_system TEXT NULL,
                  kernel_version TEXT NULL,
                  docker_server_version TEXT NULL,
                  docker_storage_driver TEXT NULL,
                  docker_backing_filesystem TEXT NULL,
                  FOREIGN KEY (node_id)
                      REFERENCES nodes(node_id) ON DELETE CASCADE
              );

              CREATE INDEX ix_node_hardware_revisions_observed
                  ON node_hardware_revisions (
                      node_id,
                      first_observed_at,
                      revision_id);
              """),
        new(
              14,
              "runner-correlation-assignment-history",
              """
              ALTER TABLE profile_history_cursors
                  ADD COLUMN dropped_runner_assignments INTEGER NOT NULL
                      DEFAULT 0
                      CHECK (dropped_runner_assignments >= 0);

              ALTER TABLE profile_history_tombstones
                  ADD COLUMN dropped_runner_assignments INTEGER NOT NULL
                      DEFAULT 0
                      CHECK (dropped_runner_assignments >= 0);

              ALTER TABLE history_incompleteness_floors
                  ADD COLUMN dropped_runner_assignments INTEGER NOT NULL
                      DEFAULT 0
                      CHECK (dropped_runner_assignments >= 0);

              CREATE TABLE profile_runner_assignments (
                  node_id TEXT NOT NULL,
                  profile_id TEXT NOT NULL
                      CHECK (length(profile_id) BETWEEN 1 AND 32),
                  runner_name_hash TEXT NOT NULL
                      CHECK (length(runner_name_hash) = 64
                          AND runner_name_hash
                              NOT GLOB '*[^0-9a-f]*'),
                  slot_key TEXT NOT NULL
                      CHECK (length(slot_key) BETWEEN 1 AND 128),
                  repository TEXT NULL
                      CHECK (repository IS NULL
                          OR length(repository) BETWEEN 1 AND 2048),
                  target TEXT NULL
                      CHECK (target IS NULL
                          OR length(target) BETWEEN 1 AND 512),
                  first_observed_at TEXT NOT NULL,
                  last_observed_at TEXT NOT NULL,
                  PRIMARY KEY (
                      node_id,
                      profile_id,
                      runner_name_hash),
                  FOREIGN KEY (node_id)
                      REFERENCES nodes(node_id) ON DELETE CASCADE
              ) WITHOUT ROWID;

              CREATE INDEX ix_profile_runner_assignments_node_interval
                  ON profile_runner_assignments (
                      node_id,
                      last_observed_at,
                      first_observed_at);
              """),
        new(
              15,
              "host-pressure-and-workload-history",
              """
              ALTER TABLE profile_telemetry_samples
                  ADD COLUMN host_pressure_status TEXT NULL
                      CHECK (host_pressure_status IS NULL
                          OR host_pressure_status IN (
                              'available',
                              'partial',
                              'unavailable'));
              ALTER TABLE profile_telemetry_samples
                  ADD COLUMN host_cpu_utilization_percent REAL NULL;
              ALTER TABLE profile_telemetry_samples
                  ADD COLUMN host_load1 REAL NULL;
              ALTER TABLE profile_telemetry_samples
                  ADD COLUMN host_load5 REAL NULL;
              ALTER TABLE profile_telemetry_samples
                  ADD COLUMN host_load15 REAL NULL;
              ALTER TABLE profile_telemetry_samples
                  ADD COLUMN host_pressure_memory_total_bytes INTEGER NULL;
              ALTER TABLE profile_telemetry_samples
                  ADD COLUMN host_memory_available_bytes INTEGER NULL;
              ALTER TABLE profile_telemetry_samples
                  ADD COLUMN host_swap_used_bytes INTEGER NULL;
              ALTER TABLE profile_telemetry_samples
                  ADD COLUMN host_cpu_pressure_some_avg10 REAL NULL;
              ALTER TABLE profile_telemetry_samples
                  ADD COLUMN host_cpu_pressure_full_avg10 REAL NULL;
              ALTER TABLE profile_telemetry_samples
                  ADD COLUMN host_memory_pressure_some_avg10 REAL NULL;
              ALTER TABLE profile_telemetry_samples
                  ADD COLUMN host_memory_pressure_full_avg10 REAL NULL;
              ALTER TABLE profile_telemetry_samples
                  ADD COLUMN host_io_pressure_some_avg10 REAL NULL;
              ALTER TABLE profile_telemetry_samples
                  ADD COLUMN host_io_pressure_full_avg10 REAL NULL;

              ALTER TABLE profile_telemetry_rollups
                  ADD COLUMN max_host_cpu_utilization_percent REAL NULL;
              ALTER TABLE profile_telemetry_rollups
                  ADD COLUMN max_host_load1 REAL NULL;
              ALTER TABLE profile_telemetry_rollups
                  ADD COLUMN max_host_load5 REAL NULL;
              ALTER TABLE profile_telemetry_rollups
                  ADD COLUMN max_host_load15 REAL NULL;
              ALTER TABLE profile_telemetry_rollups
                  ADD COLUMN min_host_memory_available_bytes INTEGER NULL;
              ALTER TABLE profile_telemetry_rollups
                  ADD COLUMN max_host_swap_used_bytes INTEGER NULL;
              ALTER TABLE profile_telemetry_rollups
                  ADD COLUMN max_host_cpu_pressure_some_avg10 REAL NULL;
              ALTER TABLE profile_telemetry_rollups
                  ADD COLUMN max_host_cpu_pressure_full_avg10 REAL NULL;
              ALTER TABLE profile_telemetry_rollups
                  ADD COLUMN max_host_memory_pressure_some_avg10 REAL NULL;
              ALTER TABLE profile_telemetry_rollups
                  ADD COLUMN max_host_memory_pressure_full_avg10 REAL NULL;
              ALTER TABLE profile_telemetry_rollups
                  ADD COLUMN max_host_io_pressure_some_avg10 REAL NULL;
              ALTER TABLE profile_telemetry_rollups
                  ADD COLUMN max_host_io_pressure_full_avg10 REAL NULL;

              ALTER TABLE profile_runner_assignments
                  ADD COLUMN job_repository TEXT NULL;
              ALTER TABLE profile_runner_assignments
                  ADD COLUMN workflow_run_id INTEGER NULL;
              ALTER TABLE profile_runner_assignments
                  ADD COLUMN job_id TEXT NULL;
              ALTER TABLE profile_runner_assignments
                  ADD COLUMN job_display_name TEXT NULL;
              ALTER TABLE profile_runner_assignments
                  ADD COLUMN job_event_name TEXT NULL;
              ALTER TABLE profile_runner_assignments
                  ADD COLUMN job_queued_at TEXT NULL;
              ALTER TABLE profile_runner_assignments
                  ADD COLUMN job_scale_set_assigned_at TEXT NULL;
              ALTER TABLE profile_runner_assignments
                  ADD COLUMN job_runner_assigned_at TEXT NULL;
              ALTER TABLE profile_runner_assignments
                  ADD COLUMN job_started_at TEXT NULL;
              ALTER TABLE profile_runner_assignments
                  ADD COLUMN job_finished_at TEXT NULL;
              ALTER TABLE profile_runner_assignments
                  ADD COLUMN job_result TEXT NULL;
              """),
        new(
              16,
              "audited-capacity-pause-resume",
              """
              DROP TRIGGER trg_capacity_commands_require_operation_slot;
              DROP INDEX ix_capacity_commands_profile_active;
              DROP INDEX ix_capacity_commands_node_requested;
              DROP INDEX ix_capacity_commands_profile_requested;

              ALTER TABLE capacity_commands
                  RENAME TO capacity_commands_legacy;

              CREATE TABLE capacity_commands (
                  command_id TEXT PRIMARY KEY,
                  node_id TEXT NOT NULL,
                  profile_id TEXT NOT NULL,
                  expected_generation INTEGER NOT NULL
                      CHECK (expected_generation >= 1),
                  previous_maximum INTEGER NULL
                      CHECK (previous_maximum IS NULL
                          OR previous_maximum >= 0),
                  requested_maximum INTEGER NOT NULL
                      CHECK (requested_maximum >= 0),
                  resumes_command_id TEXT NULL,
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
                      REFERENCES dashboard_users(github_user_id),
                  FOREIGN KEY (resumes_command_id)
                      REFERENCES capacity_commands(command_id),
                  CHECK (resumes_command_id IS NULL
                      OR requested_maximum >= 1)
              );

              INSERT INTO capacity_commands (
                  command_id,
                  node_id,
                  profile_id,
                  expected_generation,
                  previous_maximum,
                  requested_maximum,
                  resumes_command_id,
                  maximum_allowed_at_request,
                  status,
                  requested_by_github_user_id,
                  requested_at,
                  expires_at,
                  delivered_at,
                  delivery_attempts,
                  completed_at,
                  accepted_generation,
                  result_message)
              SELECT
                  command_id,
                  node_id,
                  profile_id,
                  expected_generation,
                  NULL,
                  requested_maximum,
                  NULL,
                  maximum_allowed_at_request,
                  status,
                  requested_by_github_user_id,
                  requested_at,
                  expires_at,
                  delivered_at,
                  delivery_attempts,
                  completed_at,
                  accepted_generation,
                  result_message
              FROM capacity_commands_legacy;

              DROP TABLE capacity_commands_legacy;

              CREATE UNIQUE INDEX ix_capacity_commands_profile_active
                  ON capacity_commands (node_id, profile_id)
                  WHERE status IN ('pending', 'delivered');

              CREATE INDEX ix_capacity_commands_node_requested
                  ON capacity_commands (node_id, requested_at DESC);

              CREATE INDEX ix_capacity_commands_profile_requested
                  ON capacity_commands (
                      node_id,
                      profile_id,
                      requested_at DESC,
                      command_id DESC);

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

              CREATE TRIGGER trg_capacity_commands_audit_immutable
              BEFORE UPDATE ON capacity_commands
              FOR EACH ROW
              WHEN OLD.command_id <> NEW.command_id
                OR OLD.node_id <> NEW.node_id
                OR OLD.profile_id <> NEW.profile_id
                OR OLD.expected_generation <> NEW.expected_generation
                OR IFNULL(OLD.previous_maximum, -1)
                    <> IFNULL(NEW.previous_maximum, -1)
                OR OLD.requested_maximum <> NEW.requested_maximum
                OR IFNULL(OLD.resumes_command_id, '')
                    <> IFNULL(NEW.resumes_command_id, '')
                OR OLD.maximum_allowed_at_request
                    <> NEW.maximum_allowed_at_request
                OR OLD.requested_by_github_user_id
                    <> NEW.requested_by_github_user_id
                OR OLD.requested_at <> NEW.requested_at
                OR OLD.expires_at <> NEW.expires_at
              BEGIN
                  SELECT RAISE(
                      ABORT,
                      'capacity command audit fields are immutable');
              END;
              """),
        new(
              17,
              "connector-health-replay",
              """
              CREATE TABLE connector_health_current (
                  node_id TEXT PRIMARY KEY,
                  state TEXT NOT NULL
                      CHECK (state IN (
                          'starting',
                          'healthy',
                          'degraded',
                          'stopping')),
                  process_started_at TEXT NOT NULL,
                  updated_at TEXT NOT NULL,
                  last_attempt_at TEXT NULL,
                  last_success_at TEXT NULL,
                  active_outage_id TEXT NULL,
                  active_outage_started_at TEXT NULL,
                  last_failure_at TEXT NULL,
                  last_failure_category TEXT NULL
                      CHECK (last_failure_category IS NULL
                          OR length(last_failure_category) <= 128),
                  last_failure_profile_id TEXT NULL
                      CHECK (last_failure_profile_id IS NULL
                          OR length(last_failure_profile_id) <= 32),
                  last_failure_detail TEXT NULL
                      CHECK (last_failure_detail IS NULL
                          OR length(last_failure_detail) <= 512),
                  consecutive_failures INTEGER NOT NULL
                      CHECK (consecutive_failures >= 0),
                  next_retry_at TEXT NULL,
                  last_recovered_outage_id TEXT NULL,
                  last_recovered_outage_started_at TEXT NULL,
                  last_recovered_at TEXT NULL,
                  last_recovered_failure_category TEXT NULL
                      CHECK (last_recovered_failure_category IS NULL
                          OR length(last_recovered_failure_category) <= 128),
                  received_at TEXT NOT NULL,
                  FOREIGN KEY (node_id)
                      REFERENCES nodes(node_id) ON DELETE CASCADE
              );

              CREATE TABLE connector_health_events (
                  node_id TEXT NOT NULL,
                  event_id TEXT NOT NULL,
                  kind TEXT NOT NULL
                      CHECK (kind IN (
                          'process-started',
                          'process-stopping',
                          'synchronization-succeeded',
                          'synchronization-failed',
                          'observation-incomplete',
                          'enrollment-failed',
                          'rejected',
                          'recovered')),
                  occurred_at TEXT NOT NULL,
                  state TEXT NOT NULL
                      CHECK (state IN (
                          'starting',
                          'healthy',
                          'degraded',
                          'stopping')),
                  outage_id TEXT NULL,
                  outage_started_at TEXT NULL,
                  failure_category TEXT NULL
                      CHECK (failure_category IS NULL
                          OR length(failure_category) <= 128),
                  profile_id TEXT NULL
                      CHECK (profile_id IS NULL
                          OR length(profile_id) <= 32),
                  consecutive_failures INTEGER NOT NULL
                      CHECK (consecutive_failures >= 0),
                  retry_delay_seconds INTEGER NULL
                      CHECK (retry_delay_seconds IS NULL
                          OR retry_delay_seconds >= 0),
                  detail TEXT NULL
                      CHECK (detail IS NULL
                          OR length(detail) <= 512),
                  received_at TEXT NOT NULL,
                  PRIMARY KEY (node_id, event_id),
                  FOREIGN KEY (node_id)
                      REFERENCES nodes(node_id) ON DELETE CASCADE
              );

              CREATE INDEX ix_connector_health_events_node_occurred
                  ON connector_health_events (
                      node_id,
                      occurred_at DESC,
                      event_id DESC);

              CREATE INDEX ix_connector_health_events_received
                  ON connector_health_events (received_at);
              """),
        new(
              18,
              "alert-unacknowledge-transition",
              """
              DROP TRIGGER IF EXISTS trg_alert_incidents_transitions;

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
                      AND NEW.status IN ('resolved', 'triggered')))
              BEGIN
                  SELECT RAISE(
                      ABORT,
                      'invalid alert incident status transition');
              END;
              """),
        new(
              19,
              "host-admission-history",
              """
              ALTER TABLE profile_telemetry_samples
                  ADD COLUMN host_admission_status TEXT NULL
                      CHECK (host_admission_status IS NULL
                          OR host_admission_status IN (
                              'disabled',
                              'available',
                              'degraded',
                              'unavailable'));

              ALTER TABLE profile_telemetry_samples
                  ADD COLUMN host_admission_namespace TEXT NULL
                      CHECK (host_admission_namespace IS NULL
                          OR (
                              length(host_admission_namespace)
                                  BETWEEN 1 AND 32
                              AND substr(
                                  host_admission_namespace,
                                  1,
                                  1) GLOB '[a-z]'
                              AND host_admission_namespace
                                  NOT GLOB '*[^a-z0-9-]*'));

              ALTER TABLE profile_telemetry_samples
                  ADD COLUMN host_admission_epoch INTEGER NULL
                      CHECK (host_admission_epoch IS NULL
                          OR host_admission_epoch >= 0);

              ALTER TABLE profile_telemetry_samples
                  ADD COLUMN host_admission_decision_sequence INTEGER NULL
                      CHECK (host_admission_decision_sequence IS NULL
                          OR host_admission_decision_sequence >= 0);

              ALTER TABLE profile_telemetry_samples
                  ADD COLUMN host_admission_capacity_units INTEGER NULL
                      CHECK (host_admission_capacity_units IS NULL
                          OR host_admission_capacity_units > 0);

              ALTER TABLE profile_telemetry_samples
                  ADD COLUMN host_admission_safety_margin_units INTEGER NULL
                      CHECK (host_admission_safety_margin_units IS NULL
                          OR host_admission_safety_margin_units >= 0);

              ALTER TABLE profile_telemetry_samples
                  ADD COLUMN host_admission_effective_total_units INTEGER NULL
                      CHECK (host_admission_effective_total_units IS NULL
                          OR host_admission_effective_total_units > 0);

              ALTER TABLE profile_telemetry_samples
                  ADD COLUMN host_admission_available_units INTEGER NULL
                      CHECK (host_admission_available_units IS NULL
                          OR host_admission_available_units >= 0);

              ALTER TABLE profile_telemetry_samples
                  ADD COLUMN host_admission_unit_cost INTEGER NULL
                      CHECK (host_admission_unit_cost IS NULL
                          OR host_admission_unit_cost > 0);

              ALTER TABLE profile_telemetry_samples
                  ADD COLUMN host_admission_reserved_units INTEGER NULL
                      CHECK (host_admission_reserved_units IS NULL
                          OR host_admission_reserved_units >= 0);

              ALTER TABLE profile_telemetry_samples
                  ADD COLUMN host_admission_borrowable INTEGER NULL
                      CHECK (host_admission_borrowable IS NULL
                          OR host_admission_borrowable IN (0, 1));

              ALTER TABLE profile_telemetry_samples
                  ADD COLUMN host_admission_active_units INTEGER NULL
                      CHECK (host_admission_active_units IS NULL
                          OR host_admission_active_units >= 0);

              ALTER TABLE profile_telemetry_samples
                  ADD COLUMN host_admission_provisional_units INTEGER NULL
                      CHECK (host_admission_provisional_units IS NULL
                          OR host_admission_provisional_units >= 0);

              ALTER TABLE profile_telemetry_samples
                  ADD COLUMN host_admission_held_units INTEGER NULL
                      CHECK (host_admission_held_units IS NULL
                          OR host_admission_held_units >= 0);

              ALTER TABLE profile_telemetry_samples
                  ADD COLUMN host_admission_borrowed_units INTEGER NULL
                      CHECK (host_admission_borrowed_units IS NULL
                          OR host_admission_borrowed_units >= 0);

              ALTER TABLE profile_telemetry_samples
                  ADD COLUMN host_admission_pending_units INTEGER NULL
                      CHECK (host_admission_pending_units IS NULL
                          OR host_admission_pending_units >= 0);

              ALTER TABLE profile_telemetry_samples
                  ADD COLUMN host_admission_withheld_units INTEGER NULL
                      CHECK (host_admission_withheld_units IS NULL
                          OR host_admission_withheld_units >= 0);
              """),
        new(
              20,
              "support-plane-v1",
              """
              CREATE TABLE support_nodes (
                  node_id TEXT PRIMARY KEY,
                  tenant_id TEXT NOT NULL,
                  display_name TEXT NOT NULL
                      CHECK (length(display_name) BETWEEN 1 AND 128),
                  node_signing_public_key_spki TEXT NOT NULL,
                  node_encryption_public_key_spki TEXT NOT NULL,
                  transport_credential_hash TEXT NOT NULL UNIQUE,
                  enrollment_code_hash TEXT NOT NULL UNIQUE,
                  enrollment_expires_at TEXT NOT NULL,
                  enrollment_consumed_at TEXT NULL,
                  created_by_github_user_id TEXT NOT NULL,
                  created_at TEXT NOT NULL,
                  revoked_at TEXT NULL,
                  revoked_by_github_user_id TEXT NULL,
                  last_poll_at TEXT NULL,
                  last_result_at TEXT NULL,
                  capability_version INTEGER NOT NULL
                      CHECK (capability_version >= 1),
                  FOREIGN KEY (tenant_id)
                      REFERENCES tenants(tenant_id) ON DELETE CASCADE,
                  FOREIGN KEY (created_by_github_user_id)
                      REFERENCES dashboard_users(github_user_id),
                  FOREIGN KEY (revoked_by_github_user_id)
                      REFERENCES dashboard_users(github_user_id)
              );

              CREATE INDEX ix_support_nodes_tenant_created
                  ON support_nodes (tenant_id, created_at DESC);

              CREATE INDEX ix_support_nodes_tenant_active
                  ON support_nodes (tenant_id, node_id)
                  WHERE revoked_at IS NULL;

              CREATE TABLE support_sessions (
                  session_id TEXT PRIMARY KEY,
                  tenant_id TEXT NOT NULL,
                  node_id TEXT NOT NULL,
                  diagnostic_mode TEXT NOT NULL
                      CHECK (diagnostic_mode IN (
                          'ConnectorOffline',
                          'CapacityMismatch',
                          'JobNotAssigned',
                          'HostPressure',
                          'Full')),
                  profile_id TEXT NULL
                      CHECK (profile_id IS NULL
                          OR length(profile_id) BETWEEN 1 AND 128),
                  package_id TEXT NOT NULL
                      CHECK (length(package_id) BETWEEN 1 AND 128),
                  status TEXT NOT NULL
                      CHECK (status IN (
                          'queued',
                          'dispatched',
                          'completed',
                          'rejected',
                          'cancelled',
                          'expired')),
                  requested_by_github_user_id TEXT NOT NULL,
                  requested_at TEXT NOT NULL,
                  expires_at TEXT NOT NULL,
                  request_envelope_json TEXT NOT NULL,
                  completed_at TEXT NULL,
                  result_envelope_json TEXT NULL,
                  report_json TEXT NULL,
                  markdown TEXT NULL,
                  attestation_json TEXT NULL,
                  cancelled_at TEXT NULL,
                  FOREIGN KEY (tenant_id)
                      REFERENCES tenants(tenant_id) ON DELETE CASCADE,
                  FOREIGN KEY (node_id)
                      REFERENCES support_nodes(node_id) ON DELETE CASCADE
              );

              CREATE INDEX ix_support_sessions_tenant_requested
                  ON support_sessions (
                      tenant_id,
                      requested_at DESC,
                      session_id DESC);

              CREATE INDEX ix_support_sessions_node_active
                  ON support_sessions (node_id, expires_at)
                  WHERE status IN ('queued', 'dispatched');

              CREATE TABLE support_audit_events (
                  event_id TEXT PRIMARY KEY,
                  tenant_id TEXT NOT NULL,
                  node_id TEXT NULL,
                  session_id TEXT NULL,
                  kind TEXT NOT NULL
                      CHECK (length(kind) BETWEEN 1 AND 64),
                  actor_github_user_id TEXT NULL,
                  occurred_at TEXT NOT NULL,
                  detail TEXT NULL
                      CHECK (detail IS NULL OR length(detail) <= 512),
                  FOREIGN KEY (tenant_id)
                      REFERENCES tenants(tenant_id) ON DELETE CASCADE
              );

              CREATE INDEX ix_support_audit_events_tenant_occurred
                  ON support_audit_events (
                      tenant_id,
                      occurred_at DESC,
                      event_id DESC);
              """),
        new(
              21,
              "support-session-pinned-client-contract",
              """
              ALTER TABLE support_sessions
                      ADD COLUMN capability TEXT NOT NULL DEFAULT ''
                          CHECK (capability = ''
                              OR capability = 'pitcrew.diagnostics.snapshot.v1');

              ALTER TABLE support_sessions
                      ADD COLUMN request_digest TEXT NOT NULL DEFAULT ''
                          CHECK (request_digest = ''
                              OR (length(request_digest) = 64
                                  AND request_digest NOT GLOB '*[^0-9a-f]*'));

              ALTER TABLE support_sessions
                      ADD COLUMN node_signing_key_fingerprint TEXT NOT NULL DEFAULT ''
                          CHECK (node_signing_key_fingerprint = ''
                              OR (length(node_signing_key_fingerprint) = 64
                                  AND node_signing_key_fingerprint
                                      NOT GLOB '*[^0-9a-f]*'));

              CREATE TRIGGER trg_support_sessions_pinned_contract_immutable
              BEFORE UPDATE ON support_sessions
              FOR EACH ROW
              WHEN OLD.capability <> NEW.capability
                OR OLD.request_digest <> NEW.request_digest
                OR OLD.node_signing_key_fingerprint
                        <> NEW.node_signing_key_fingerprint
                OR OLD.expires_at <> NEW.expires_at
              BEGIN
                      SELECT RAISE(
                          ABORT,
                          'support session pinned contract is immutable');
              END;
              """),
        new(
              22,
              "support-identity-local-enrollment-and-rotation",
              """
              CREATE TABLE support_enrollments (
                  enrollment_id TEXT PRIMARY KEY,
                  tenant_id TEXT NOT NULL,
                  display_name TEXT NOT NULL
                      CHECK (length(display_name) BETWEEN 1 AND 128),
                  enrollment_code_hash TEXT NOT NULL UNIQUE,
                  created_by_github_user_id TEXT NOT NULL,
                  created_at TEXT NOT NULL,
                  expires_at TEXT NOT NULL,
                  consumed_at TEXT NULL,
                  recovery_expires_at TEXT NULL,
                  completion_id TEXT NULL UNIQUE,
                  completed_node_id TEXT NULL UNIQUE,
                  transport_credential_envelope_json TEXT NULL,
                  CHECK (
                      (completion_id IS NULL
                       AND completed_node_id IS NULL
                       AND recovery_expires_at IS NULL
                       AND transport_credential_envelope_json IS NULL)
                      OR
                      (length(completion_id) = 36
                       AND length(completed_node_id) = 36
                       AND recovery_expires_at IS NOT NULL
                       AND transport_credential_envelope_json IS NOT NULL)),
                  FOREIGN KEY (tenant_id)
                      REFERENCES tenants(tenant_id) ON DELETE CASCADE,
                  FOREIGN KEY (created_by_github_user_id)
                      REFERENCES dashboard_users(github_user_id)
              );

              CREATE INDEX ix_support_enrollments_tenant_expiry
                  ON support_enrollments (tenant_id, expires_at)
                  WHERE consumed_at IS NULL;

              CREATE INDEX ix_support_enrollments_recovery_expiry
                  ON support_enrollments (recovery_expires_at)
                  WHERE consumed_at IS NOT NULL;

              CREATE TABLE support_identity_rotations (
                  rotation_id TEXT PRIMARY KEY,
                  tenant_id TEXT NOT NULL,
                  node_id TEXT NOT NULL UNIQUE,
                  expected_transport_credential_hash TEXT NOT NULL,
                  replacement_transport_credential_hash TEXT NOT NULL,
                  node_signing_public_key_spki TEXT NOT NULL,
                  node_encryption_public_key_spki TEXT NOT NULL,
                  phase TEXT NOT NULL CHECK (phase IN (
                      'prepared',
                      'dashboard_promoted',
                      'finalized')),
                  created_at TEXT NOT NULL,
                  dashboard_promoted_at TEXT NULL,
                  finalized_at TEXT NULL,
                  FOREIGN KEY (node_id)
                      REFERENCES support_nodes(node_id) ON DELETE CASCADE,
                  FOREIGN KEY (tenant_id)
                      REFERENCES tenants(tenant_id) ON DELETE CASCADE
              );

              CREATE INDEX ix_support_identity_rotations_tenant_phase
                  ON support_identity_rotations (tenant_id, phase, node_id);

              CREATE TABLE support_relay_cleanup (
                  node_id TEXT PRIMARY KEY,
                  created_at TEXT NOT NULL,
                  last_attempt_at TEXT NULL,
                  attempt_count INTEGER NOT NULL DEFAULT 0
                      CHECK (attempt_count >= 0),
                  next_attempt_at TEXT NOT NULL,
                  lease_id TEXT NULL,
                  lease_expires_at TEXT NULL,
                  CHECK (
                      (lease_id IS NULL AND lease_expires_at IS NULL)
                      OR
                      (lease_id IS NOT NULL AND lease_expires_at IS NOT NULL))
              );

              CREATE INDEX ix_support_relay_cleanup_due
                  ON support_relay_cleanup (
                      next_attempt_at,
                      lease_expires_at,
                      created_at,
                      node_id);
              """),
        new(
              23,
              "trusted-image-candidate-domain",
              """
              CREATE TABLE image_recipe_versions (
                  tenant_id TEXT NOT NULL,
                  registration_id TEXT NOT NULL
                      CHECK (length(registration_id) = 36),
                  version INTEGER NOT NULL
                      CHECK (version >= 1),
                  github_installation_id INTEGER NOT NULL
                      CHECK (github_installation_id >= 1),
                  github_repository_id INTEGER NOT NULL
                      CHECK (github_repository_id >= 1),
                  github_workflow_id INTEGER NOT NULL
                      CHECK (github_workflow_id >= 1),
                  repository_owner TEXT NOT NULL
                      CHECK (length(repository_owner) BETWEEN 1 AND 100),
                  repository_name TEXT NOT NULL
                      CHECK (length(repository_name) BETWEEN 1 AND 100),
                  canonical_repository TEXT GENERATED ALWAYS AS (
                      repository_owner || '/' || repository_name) STORED,
                  workflow_path TEXT NOT NULL
                      CHECK (length(workflow_path) BETWEEN 1 AND 256),
                  workflow_blob_sha TEXT NOT NULL
                      CHECK (length(workflow_blob_sha) = 40
                          AND workflow_blob_sha NOT GLOB '*[^0-9a-f]*'),
                  dispatch_ref TEXT NOT NULL
                      CHECK (length(dispatch_ref) BETWEEN 1 AND 255),
                  recipe_id TEXT NOT NULL
                      CHECK (length(recipe_id) BETWEEN 1 AND 64
                          AND substr(recipe_id, 1, 1) GLOB '[a-z]'
                          AND recipe_id NOT GLOB '*[^a-z0-9-]*'),
                  candidate_schema_version INTEGER NOT NULL
                      CHECK (candidate_schema_version = 1),
                  source_ref_policy_json TEXT NOT NULL
                      CHECK (length(source_ref_policy_json) BETWEEN 2 AND 4096
                          AND json_valid(source_ref_policy_json)),
                  input_schema_json TEXT NOT NULL
                      CHECK (length(input_schema_json) BETWEEN 2 AND 16384
                          AND json_valid(input_schema_json)),
                  created_by_github_user_id TEXT NOT NULL
                      CHECK (length(created_by_github_user_id) BETWEEN 1 AND 64),
                  created_at TEXT NOT NULL,
                  disabled_by_github_user_id TEXT NULL
                      CHECK (disabled_by_github_user_id IS NULL
                          OR length(disabled_by_github_user_id) BETWEEN 1 AND 64),
                  disabled_at TEXT NULL,
                  PRIMARY KEY (registration_id, version),
                  UNIQUE (tenant_id, registration_id, version),
                  UNIQUE (tenant_id, recipe_id, version),
                  UNIQUE (
                      tenant_id,
                      registration_id,
                      version,
                      recipe_id,
                      canonical_repository),
                  CHECK ((disabled_by_github_user_id IS NULL
                          AND disabled_at IS NULL)
                      OR (disabled_by_github_user_id IS NOT NULL
                          AND disabled_at IS NOT NULL)),
                  FOREIGN KEY (tenant_id)
                      REFERENCES tenants(tenant_id) ON DELETE CASCADE,
                  FOREIGN KEY (created_by_github_user_id)
                      REFERENCES dashboard_users(github_user_id),
                  FOREIGN KEY (disabled_by_github_user_id)
                      REFERENCES dashboard_users(github_user_id)
              );

              CREATE INDEX ix_image_recipe_versions_tenant_recipe
                  ON image_recipe_versions (
                      tenant_id,
                      recipe_id,
                      version DESC);

              CREATE INDEX ix_image_recipe_versions_tenant_active
                  ON image_recipe_versions (
                      tenant_id,
                      recipe_id,
                      registration_id,
                      version DESC)
                  WHERE disabled_at IS NULL;

              CREATE TRIGGER trg_image_recipe_versions_immutable
              BEFORE UPDATE ON image_recipe_versions
              FOR EACH ROW
              WHEN OLD.tenant_id <> NEW.tenant_id
                OR OLD.registration_id <> NEW.registration_id
                OR OLD.version <> NEW.version
                OR OLD.github_installation_id <> NEW.github_installation_id
                OR OLD.github_repository_id <> NEW.github_repository_id
                OR OLD.github_workflow_id <> NEW.github_workflow_id
                OR OLD.repository_owner <> NEW.repository_owner
                OR OLD.repository_name <> NEW.repository_name
                OR OLD.workflow_path <> NEW.workflow_path
                OR OLD.workflow_blob_sha <> NEW.workflow_blob_sha
                OR OLD.dispatch_ref <> NEW.dispatch_ref
                OR OLD.recipe_id <> NEW.recipe_id
                OR OLD.candidate_schema_version <> NEW.candidate_schema_version
                OR OLD.source_ref_policy_json <> NEW.source_ref_policy_json
                OR OLD.input_schema_json <> NEW.input_schema_json
                OR OLD.created_by_github_user_id
                    <> NEW.created_by_github_user_id
                OR OLD.created_at <> NEW.created_at
                OR (OLD.disabled_at IS NOT NULL
                    AND (OLD.disabled_at <> NEW.disabled_at
                        OR OLD.disabled_by_github_user_id
                            <> NEW.disabled_by_github_user_id))
                OR (OLD.disabled_at IS NULL
                    AND ((NEW.disabled_at IS NULL
                          AND NEW.disabled_by_github_user_id IS NOT NULL)
                        OR (NEW.disabled_at IS NOT NULL
                          AND NEW.disabled_by_github_user_id IS NULL)))
              BEGIN
                  SELECT RAISE(
                      ABORT,
                      'image recipe registration identity is immutable');
              END;

              CREATE TABLE image_build_requests (
                  request_id TEXT PRIMARY KEY
                      CHECK (length(request_id) = 36),
                  tenant_id TEXT NOT NULL,
                  registration_id TEXT NOT NULL
                      CHECK (length(registration_id) = 36),
                  registration_version INTEGER NOT NULL
                      CHECK (registration_version >= 1),
                  recipe_id TEXT NOT NULL
                      CHECK (length(recipe_id) BETWEEN 1 AND 64
                          AND substr(recipe_id, 1, 1) GLOB '[a-z]'
                          AND recipe_id NOT GLOB '*[^a-z0-9-]*'),
                  source_repository TEXT NOT NULL
                      CHECK (length(source_repository) BETWEEN 3 AND 200
                          AND instr(source_repository, '/') BETWEEN 2
                              AND length(source_repository) - 1),
                  source_commit TEXT NOT NULL
                      CHECK (length(source_commit) = 40
                          AND source_commit NOT GLOB '*[^0-9a-f]*'),
                  input_values_json TEXT NOT NULL
                      CHECK (length(input_values_json) BETWEEN 2 AND 16384
                          AND json_valid(input_values_json)),
                  input_values_sha256 TEXT NOT NULL
                      CHECK (length(input_values_sha256) = 64
                          AND input_values_sha256 NOT GLOB '*[^0-9a-f]*'),
                  requested_by_github_user_id TEXT NOT NULL
                      CHECK (length(requested_by_github_user_id) BETWEEN 1 AND 64),
                  requested_at TEXT NOT NULL,
                  status TEXT NOT NULL
                      CHECK (status IN (
                          'requested',
                          'dispatching',
                          'building',
                          'qualifying',
                          'ready',
                          'blocked',
                          'failed')),
                  github_run_id INTEGER NULL
                      CHECK (github_run_id IS NULL OR github_run_id >= 1),
                  github_run_url TEXT NULL
                      CHECK (github_run_url IS NULL
                          OR length(github_run_url) BETWEEN 1 AND 512),
                  terminal_category TEXT NULL
                      CHECK (terminal_category IS NULL
                          OR length(terminal_category) BETWEEN 1 AND 64),
                  terminal_detail TEXT NULL
                      CHECK (terminal_detail IS NULL
                          OR length(terminal_detail) BETWEEN 1 AND 512),
                  updated_at TEXT NOT NULL,
                  UNIQUE (tenant_id, request_id),
                  CHECK ((github_run_id IS NULL AND github_run_url IS NULL)
                      OR (github_run_id IS NOT NULL
                          AND github_run_url IS NOT NULL)),
                  CHECK ((status IN ('blocked', 'failed')
                          AND terminal_category IS NOT NULL
                          AND terminal_detail IS NOT NULL)
                      OR (status NOT IN ('blocked', 'failed')
                          AND terminal_category IS NULL
                          AND terminal_detail IS NULL)),
                  FOREIGN KEY (tenant_id)
                      REFERENCES tenants(tenant_id) ON DELETE CASCADE,
                  FOREIGN KEY (requested_by_github_user_id)
                      REFERENCES dashboard_users(github_user_id),
                  FOREIGN KEY (
                      tenant_id,
                      registration_id,
                      registration_version,
                      recipe_id,
                      source_repository)
                      REFERENCES image_recipe_versions (
                          tenant_id,
                          registration_id,
                          version,
                          recipe_id,
                          canonical_repository)
              );

              CREATE INDEX ix_image_build_requests_tenant_requested
                  ON image_build_requests (
                      tenant_id,
                      requested_at DESC,
                      request_id DESC);

              CREATE INDEX ix_image_build_requests_tenant_status
                  ON image_build_requests (
                      tenant_id,
                      status,
                      requested_at DESC,
                      request_id DESC);

              CREATE INDEX ix_image_build_requests_active
                  ON image_build_requests (
                      status,
                      updated_at,
                      tenant_id,
                      request_id)
                  WHERE status IN (
                      'requested',
                      'dispatching',
                      'building',
                      'qualifying');

              CREATE TRIGGER trg_image_build_requests_identity_immutable
              BEFORE UPDATE ON image_build_requests
              FOR EACH ROW
              WHEN OLD.request_id <> NEW.request_id
                OR OLD.tenant_id <> NEW.tenant_id
                OR OLD.registration_id <> NEW.registration_id
                OR OLD.registration_version <> NEW.registration_version
                OR OLD.recipe_id <> NEW.recipe_id
                OR OLD.source_repository <> NEW.source_repository
                OR OLD.source_commit <> NEW.source_commit
                OR OLD.input_values_json <> NEW.input_values_json
                OR OLD.input_values_sha256 <> NEW.input_values_sha256
                OR OLD.requested_by_github_user_id
                    <> NEW.requested_by_github_user_id
                OR OLD.requested_at <> NEW.requested_at
                OR (OLD.github_run_id IS NOT NULL
                    AND (OLD.github_run_id <> NEW.github_run_id
                        OR OLD.github_run_url <> NEW.github_run_url))
              BEGIN
                  SELECT RAISE(
                      ABORT,
                      'image build request identity is immutable');
              END;

              CREATE TRIGGER trg_image_build_requests_monotonic
              BEFORE UPDATE ON image_build_requests
              FOR EACH ROW
              WHEN NEW.updated_at < OLD.updated_at
                OR OLD.status IN ('ready', 'blocked', 'failed')
                OR NOT (
                    (OLD.status = 'requested'
                        AND NEW.status = 'dispatching')
                    OR (OLD.status = 'dispatching'
                        AND NEW.status = 'building')
                    OR (OLD.status = 'building'
                        AND NEW.status = 'qualifying')
                    OR (OLD.status = 'qualifying'
                        AND NEW.status IN ('ready', 'blocked', 'failed')))
              BEGIN
                  SELECT RAISE(
                      ABORT,
                      'invalid image build request transition');
              END;

              CREATE TABLE image_candidates (
                  candidate_id TEXT PRIMARY KEY
                      CHECK (length(candidate_id) = 36),
                  tenant_id TEXT NOT NULL,
                  request_id TEXT NOT NULL,
                  outcome TEXT NOT NULL
                      CHECK (outcome IN ('ready', 'failed')),
                  recipe_id TEXT NOT NULL
                      CHECK (length(recipe_id) BETWEEN 1 AND 64
                          AND substr(recipe_id, 1, 1) GLOB '[a-z]'
                          AND recipe_id NOT GLOB '*[^a-z0-9-]*'),
                  source_repository TEXT NOT NULL
                      CHECK (length(source_repository) BETWEEN 3 AND 200
                          AND instr(source_repository, '/') BETWEEN 2
                              AND length(source_repository) - 1),
                  source_commit TEXT NOT NULL
                      CHECK (length(source_commit) = 40
                          AND source_commit NOT GLOB '*[^0-9a-f]*'),
                  github_run_id INTEGER NOT NULL
                      CHECK (github_run_id >= 1),
                  artifact_id INTEGER NOT NULL
                      CHECK (artifact_id >= 1),
                  artifact_name TEXT NOT NULL
                      CHECK (artifact_name = 'pitcrew-image-candidate'),
                  artifact_digest TEXT NOT NULL
                      CHECK (length(artifact_digest) = 71
                          AND artifact_digest GLOB 'sha256:*'
                          AND substr(artifact_digest, 8)
                              NOT GLOB '*[^0-9a-f]*'),
                  report_hash TEXT NOT NULL
                      CHECK (length(report_hash) = 64
                          AND report_hash NOT GLOB '*[^0-9a-f]*'),
                  report_json TEXT NOT NULL
                      CHECK (length(report_json) BETWEEN 2 AND 32768
                          AND json_valid(report_json)),
                  image_reference TEXT NOT NULL
                      CHECK (length(image_reference) BETWEEN 1 AND 512),
                  digest TEXT NULL
                      CHECK (digest IS NULL
                          OR (length(digest) = 71
                              AND digest GLOB 'sha256:*'
                              AND substr(digest, 8)
                                  NOT GLOB '*[^0-9a-f]*')),
                  immutable_reference TEXT NULL
                      CHECK (immutable_reference IS NULL
                          OR (length(immutable_reference) BETWEEN 72 AND 584
                              AND immutable_reference GLOB '*@sha256:*'
                              AND substr(
                                  immutable_reference,
                                  length(immutable_reference) - 63)
                                  NOT GLOB '*[^0-9a-f]*')),
                  platform TEXT NOT NULL
                      CHECK (platform IN ('linux/amd64', 'linux/arm64')),
                  output_mode TEXT NOT NULL
                      CHECK (output_mode IN ('registry', 'oci')),
                  failure_category TEXT NULL
                      CHECK (failure_category IS NULL OR failure_category IN (
                          'build-failed',
                          'digest-unavailable',
                          'registry-verification-failed',
                          'registry-digest-mismatch',
                          'oci-verification-failed',
                          'oci-digest-mismatch',
                          'oci-manifest-missing',
                          'builder-cleanup-failed')),
                  failure_detail TEXT NULL
                      CHECK (failure_detail IS NULL OR failure_detail IN (
                          'Image build did not complete.',
                          'BuildKit did not return an immutable image digest.',
                          'Registry digest verification failed.',
                          'Registry digest did not match BuildKit digest.',
                          'OCI output verification failed.',
                          'OCI output digest did not match BuildKit digest.',
                          'OCI output omitted its declared manifest blob.',
                          'BuildKit cleanup did not reach an empty state.')),
                  created_at TEXT NOT NULL,
                  stored_at TEXT NOT NULL,
                  UNIQUE (tenant_id, candidate_id),
                  UNIQUE (tenant_id, request_id),
                  UNIQUE (tenant_id, github_run_id, artifact_id),
                  CHECK ((outcome = 'ready'
                          AND digest IS NOT NULL
                          AND failure_category IS NULL
                          AND failure_detail IS NULL)
                      OR (outcome = 'failed'
                          AND failure_category IS NOT NULL
                          AND failure_detail IS NOT NULL)),
                  CHECK ((output_mode = 'registry'
                          AND (outcome = 'failed'
                              OR immutable_reference IS NOT NULL))
                      OR (output_mode = 'oci'
                          AND immutable_reference IS NULL)),
                  FOREIGN KEY (tenant_id)
                      REFERENCES tenants(tenant_id) ON DELETE CASCADE,
                  FOREIGN KEY (tenant_id, request_id)
                      REFERENCES image_build_requests (tenant_id, request_id)
                      ON DELETE CASCADE
              );

              CREATE INDEX ix_image_candidates_tenant_stored
                  ON image_candidates (
                      tenant_id,
                      stored_at DESC,
                      candidate_id DESC);

              CREATE TRIGGER trg_image_candidates_immutable_update
              BEFORE UPDATE ON image_candidates
              FOR EACH ROW
              BEGIN
                  SELECT RAISE(ABORT, 'image candidates are immutable');
              END;

              CREATE TABLE image_candidate_qualifications (
                  candidate_id TEXT NOT NULL,
                  name TEXT NOT NULL
                      CHECK (name IN (
                          'image-build',
                          'buildkit-digest',
                          'registry-digest',
                          'oci-manifest',
                          'builder-cleanup')),
                  status TEXT NOT NULL
                      CHECK (status IN (
                          'passed',
                          'failed',
                          'unavailable')),
                  PRIMARY KEY (candidate_id, name),
                  FOREIGN KEY (candidate_id)
                      REFERENCES image_candidates(candidate_id)
                      ON DELETE CASCADE
              );

              CREATE TRIGGER trg_image_candidate_qualifications_ready
              BEFORE INSERT ON image_candidate_qualifications
              FOR EACH ROW
              WHEN NEW.status <> 'passed'
                AND EXISTS (
                    SELECT 1
                    FROM image_candidates
                    WHERE candidate_id = NEW.candidate_id
                      AND outcome = 'ready')
              BEGIN
                  SELECT RAISE(
                      ABORT,
                      'ready image candidate qualifications must pass');
              END;

              CREATE TRIGGER trg_image_candidate_qualifications_immutable_update
              BEFORE UPDATE ON image_candidate_qualifications
              FOR EACH ROW
              BEGIN
                  SELECT RAISE(
                      ABORT,
                      'image candidate qualifications are immutable');
              END;

              """),
        new(
              24,
              "tenant-scoped-image-recipe-registration-authority",
              """
              CREATE TABLE image_recipe_versions_migration24 (
                  tenant_id TEXT NOT NULL,
                  registration_id TEXT NOT NULL
                      CHECK (length(registration_id) = 36),
                  version INTEGER NOT NULL
                      CHECK (version >= 1),
                  github_installation_id INTEGER NOT NULL
                      CHECK (github_installation_id >= 1),
                  github_repository_id INTEGER NOT NULL
                      CHECK (github_repository_id >= 1),
                  github_workflow_id INTEGER NOT NULL
                      CHECK (github_workflow_id >= 1),
                  repository_owner TEXT NOT NULL
                      CHECK (length(repository_owner) BETWEEN 1 AND 100),
                  repository_name TEXT NOT NULL
                      CHECK (length(repository_name) BETWEEN 1 AND 100),
                  canonical_repository TEXT GENERATED ALWAYS AS (
                      repository_owner || '/' || repository_name) STORED,
                  workflow_path TEXT NOT NULL
                      CHECK (length(workflow_path) BETWEEN 1 AND 256),
                  workflow_blob_sha TEXT NOT NULL
                      CHECK (length(workflow_blob_sha) = 40
                          AND workflow_blob_sha NOT GLOB '*[^0-9a-f]*'),
                  dispatch_ref TEXT NOT NULL
                      CHECK (length(dispatch_ref) BETWEEN 1 AND 255),
                  recipe_id TEXT NOT NULL
                      CHECK (length(recipe_id) BETWEEN 1 AND 64
                          AND substr(recipe_id, 1, 1) GLOB '[a-z]'
                          AND recipe_id NOT GLOB '*[^a-z0-9-]*'),
                  candidate_schema_version INTEGER NOT NULL
                      CHECK (candidate_schema_version = 1),
                  source_ref_policy_json TEXT NOT NULL
                      CHECK (length(source_ref_policy_json) BETWEEN 2 AND 4096
                          AND json_valid(source_ref_policy_json)),
                  input_schema_json TEXT NOT NULL
                      CHECK (length(input_schema_json) BETWEEN 2 AND 16384
                          AND json_valid(input_schema_json)),
                  created_by_github_user_id TEXT NOT NULL
                      CHECK (length(created_by_github_user_id) BETWEEN 1 AND 64),
                  created_at TEXT NOT NULL,
                  disabled_by_github_user_id TEXT NULL
                      CHECK (disabled_by_github_user_id IS NULL
                          OR length(disabled_by_github_user_id) BETWEEN 1 AND 64),
                  disabled_at TEXT NULL,
                  PRIMARY KEY (tenant_id, registration_id, version),
                  UNIQUE (tenant_id, recipe_id, version),
                  UNIQUE (
                      tenant_id,
                      registration_id,
                      version,
                      recipe_id,
                      canonical_repository),
                  CHECK ((disabled_by_github_user_id IS NULL
                          AND disabled_at IS NULL)
                      OR (disabled_by_github_user_id IS NOT NULL
                          AND disabled_at IS NOT NULL)),
                  FOREIGN KEY (tenant_id)
                      REFERENCES tenants(tenant_id) ON DELETE CASCADE,
                  FOREIGN KEY (created_by_github_user_id)
                      REFERENCES dashboard_users(github_user_id),
                  FOREIGN KEY (disabled_by_github_user_id)
                      REFERENCES dashboard_users(github_user_id)
              );

              CREATE TABLE image_build_requests_migration24 (
                  request_id TEXT PRIMARY KEY
                      CHECK (length(request_id) = 36),
                  tenant_id TEXT NOT NULL,
                  registration_id TEXT NOT NULL
                      CHECK (length(registration_id) = 36),
                  registration_version INTEGER NOT NULL
                      CHECK (registration_version >= 1),
                  recipe_id TEXT NOT NULL
                      CHECK (length(recipe_id) BETWEEN 1 AND 64
                          AND substr(recipe_id, 1, 1) GLOB '[a-z]'
                          AND recipe_id NOT GLOB '*[^a-z0-9-]*'),
                  source_repository TEXT NOT NULL
                      CHECK (length(source_repository) BETWEEN 3 AND 200
                          AND instr(source_repository, '/') BETWEEN 2
                              AND length(source_repository) - 1),
                  source_commit TEXT NOT NULL
                      CHECK (length(source_commit) = 40
                          AND source_commit NOT GLOB '*[^0-9a-f]*'),
                  input_values_json TEXT NOT NULL
                      CHECK (length(input_values_json) BETWEEN 2 AND 16384
                          AND json_valid(input_values_json)),
                  input_values_sha256 TEXT NOT NULL
                      CHECK (length(input_values_sha256) = 64
                          AND input_values_sha256 NOT GLOB '*[^0-9a-f]*'),
                  requested_by_github_user_id TEXT NOT NULL
                      CHECK (length(requested_by_github_user_id) BETWEEN 1 AND 64),
                  requested_at TEXT NOT NULL,
                  status TEXT NOT NULL
                      CHECK (status IN (
                          'requested',
                          'dispatching',
                          'building',
                          'qualifying',
                          'ready',
                          'blocked',
                          'failed')),
                  github_run_id INTEGER NULL
                      CHECK (github_run_id IS NULL OR github_run_id >= 1),
                  github_run_url TEXT NULL
                      CHECK (github_run_url IS NULL
                          OR length(github_run_url) BETWEEN 1 AND 512),
                  terminal_category TEXT NULL
                      CHECK (terminal_category IS NULL
                          OR length(terminal_category) BETWEEN 1 AND 64),
                  terminal_detail TEXT NULL
                      CHECK (terminal_detail IS NULL
                          OR length(terminal_detail) BETWEEN 1 AND 512),
                  updated_at TEXT NOT NULL,
                  UNIQUE (tenant_id, request_id),
                  CHECK ((github_run_id IS NULL AND github_run_url IS NULL)
                      OR (github_run_id IS NOT NULL
                          AND github_run_url IS NOT NULL)),
                  CHECK ((status IN ('blocked', 'failed')
                          AND terminal_category IS NOT NULL
                          AND terminal_detail IS NOT NULL)
                      OR (status NOT IN ('blocked', 'failed')
                          AND terminal_category IS NULL
                          AND terminal_detail IS NULL)),
                  FOREIGN KEY (tenant_id)
                      REFERENCES tenants(tenant_id) ON DELETE CASCADE,
                  FOREIGN KEY (requested_by_github_user_id)
                      REFERENCES dashboard_users(github_user_id),
                  FOREIGN KEY (
                      tenant_id,
                      registration_id,
                      registration_version,
                      recipe_id,
                      source_repository)
                      REFERENCES image_recipe_versions_migration24 (
                          tenant_id,
                          registration_id,
                          version,
                          recipe_id,
                          canonical_repository)
              );

              CREATE TABLE image_candidates_migration24 (
                  candidate_id TEXT PRIMARY KEY
                      CHECK (length(candidate_id) = 36),
                  tenant_id TEXT NOT NULL,
                  request_id TEXT NOT NULL,
                  outcome TEXT NOT NULL
                      CHECK (outcome IN ('ready', 'failed')),
                  recipe_id TEXT NOT NULL
                      CHECK (length(recipe_id) BETWEEN 1 AND 64
                          AND substr(recipe_id, 1, 1) GLOB '[a-z]'
                          AND recipe_id NOT GLOB '*[^a-z0-9-]*'),
                  source_repository TEXT NOT NULL
                      CHECK (length(source_repository) BETWEEN 3 AND 200
                          AND instr(source_repository, '/') BETWEEN 2
                              AND length(source_repository) - 1),
                  source_commit TEXT NOT NULL
                      CHECK (length(source_commit) = 40
                          AND source_commit NOT GLOB '*[^0-9a-f]*'),
                  github_run_id INTEGER NOT NULL
                      CHECK (github_run_id >= 1),
                  artifact_id INTEGER NOT NULL
                      CHECK (artifact_id >= 1),
                  artifact_name TEXT NOT NULL
                      CHECK (artifact_name = 'pitcrew-image-candidate'),
                  artifact_digest TEXT NOT NULL
                      CHECK (length(artifact_digest) = 71
                          AND artifact_digest GLOB 'sha256:*'
                          AND substr(artifact_digest, 8)
                              NOT GLOB '*[^0-9a-f]*'),
                  report_hash TEXT NOT NULL
                      CHECK (length(report_hash) = 64
                          AND report_hash NOT GLOB '*[^0-9a-f]*'),
                  report_json TEXT NOT NULL
                      CHECK (length(report_json) BETWEEN 2 AND 32768
                          AND json_valid(report_json)),
                  image_reference TEXT NOT NULL
                      CHECK (length(image_reference) BETWEEN 1 AND 512),
                  digest TEXT NULL
                      CHECK (digest IS NULL
                          OR (length(digest) = 71
                              AND digest GLOB 'sha256:*'
                              AND substr(digest, 8)
                                  NOT GLOB '*[^0-9a-f]*')),
                  immutable_reference TEXT NULL
                      CHECK (immutable_reference IS NULL
                          OR (length(immutable_reference) BETWEEN 72 AND 584
                              AND immutable_reference GLOB '*@sha256:*'
                              AND substr(
                                  immutable_reference,
                                  length(immutable_reference) - 63)
                                  NOT GLOB '*[^0-9a-f]*')),
                  platform TEXT NOT NULL
                      CHECK (platform IN ('linux/amd64', 'linux/arm64')),
                  output_mode TEXT NOT NULL
                      CHECK (output_mode IN ('registry', 'oci')),
                  failure_category TEXT NULL
                      CHECK (failure_category IS NULL OR failure_category IN (
                          'build-failed',
                          'digest-unavailable',
                          'registry-verification-failed',
                          'registry-digest-mismatch',
                          'oci-verification-failed',
                          'oci-digest-mismatch',
                          'oci-manifest-missing',
                          'builder-cleanup-failed')),
                  failure_detail TEXT NULL
                      CHECK (failure_detail IS NULL OR failure_detail IN (
                          'Image build did not complete.',
                          'BuildKit did not return an immutable image digest.',
                          'Registry digest verification failed.',
                          'Registry digest did not match BuildKit digest.',
                          'OCI output verification failed.',
                          'OCI output digest did not match BuildKit digest.',
                          'OCI output omitted its declared manifest blob.',
                          'BuildKit cleanup did not reach an empty state.')),
                  created_at TEXT NOT NULL,
                  stored_at TEXT NOT NULL,
                  UNIQUE (tenant_id, candidate_id),
                  UNIQUE (tenant_id, request_id),
                  UNIQUE (tenant_id, github_run_id, artifact_id),
                  CHECK ((outcome = 'ready'
                          AND digest IS NOT NULL
                          AND failure_category IS NULL
                          AND failure_detail IS NULL)
                      OR (outcome = 'failed'
                          AND failure_category IS NOT NULL
                          AND failure_detail IS NOT NULL)),
                  CHECK ((output_mode = 'registry'
                          AND (outcome = 'failed'
                              OR immutable_reference IS NOT NULL))
                      OR (output_mode = 'oci'
                          AND immutable_reference IS NULL)),
                  FOREIGN KEY (tenant_id)
                      REFERENCES tenants(tenant_id) ON DELETE CASCADE,
                  FOREIGN KEY (tenant_id, request_id)
                      REFERENCES image_build_requests_migration24 (tenant_id, request_id)
                      ON DELETE CASCADE
              );

              CREATE TABLE image_candidate_qualifications_migration24 (
                  candidate_id TEXT NOT NULL,
                  name TEXT NOT NULL
                      CHECK (name IN (
                          'image-build',
                          'buildkit-digest',
                          'registry-digest',
                          'oci-manifest',
                          'builder-cleanup')),
                  status TEXT NOT NULL
                      CHECK (status IN (
                          'passed',
                          'failed',
                          'unavailable')),
                  PRIMARY KEY (candidate_id, name),
                  FOREIGN KEY (candidate_id)
                      REFERENCES image_candidates_migration24(candidate_id)
                      ON DELETE CASCADE
              );

              INSERT INTO image_recipe_versions_migration24 (
                  tenant_id,
                  registration_id,
                  version,
                  github_installation_id,
                  github_repository_id,
                  github_workflow_id,
                  repository_owner,
                  repository_name,
                  workflow_path,
                  workflow_blob_sha,
                  dispatch_ref,
                  recipe_id,
                  candidate_schema_version,
                  source_ref_policy_json,
                  input_schema_json,
                  created_by_github_user_id,
                  created_at,
                  disabled_by_github_user_id,
                  disabled_at)
              SELECT
                  tenant_id,
                  registration_id,
                  version,
                  github_installation_id,
                  github_repository_id,
                  github_workflow_id,
                  repository_owner,
                  repository_name,
                  workflow_path,
                  workflow_blob_sha,
                  dispatch_ref,
                  recipe_id,
                  candidate_schema_version,
                  source_ref_policy_json,
                  input_schema_json,
                  created_by_github_user_id,
                  created_at,
                  disabled_by_github_user_id,
                  disabled_at
              FROM image_recipe_versions;

              INSERT INTO image_build_requests_migration24 (
                  request_id,
                  tenant_id,
                  registration_id,
                  registration_version,
                  recipe_id,
                  source_repository,
                  source_commit,
                  input_values_json,
                  input_values_sha256,
                  requested_by_github_user_id,
                  requested_at,
                  status,
                  github_run_id,
                  github_run_url,
                  terminal_category,
                  terminal_detail,
                  updated_at)
              SELECT
                  request_id,
                  tenant_id,
                  registration_id,
                  registration_version,
                  recipe_id,
                  source_repository,
                  source_commit,
                  input_values_json,
                  input_values_sha256,
                  requested_by_github_user_id,
                  requested_at,
                  status,
                  github_run_id,
                  github_run_url,
                  terminal_category,
                  terminal_detail,
                  updated_at
              FROM image_build_requests;

              INSERT INTO image_candidates_migration24 (
                  candidate_id,
                  tenant_id,
                  request_id,
                  outcome,
                  recipe_id,
                  source_repository,
                  source_commit,
                  github_run_id,
                  artifact_id,
                  artifact_name,
                  artifact_digest,
                  report_hash,
                  report_json,
                  image_reference,
                  digest,
                  immutable_reference,
                  platform,
                  output_mode,
                  failure_category,
                  failure_detail,
                  created_at,
                  stored_at)
              SELECT
                  candidate_id,
                  tenant_id,
                  request_id,
                  outcome,
                  recipe_id,
                  source_repository,
                  source_commit,
                  github_run_id,
                  artifact_id,
                  artifact_name,
                  artifact_digest,
                  report_hash,
                  report_json,
                  image_reference,
                  digest,
                  immutable_reference,
                  platform,
                  output_mode,
                  failure_category,
                  failure_detail,
                  created_at,
                  stored_at
              FROM image_candidates;

              INSERT INTO image_candidate_qualifications_migration24 (
                  candidate_id,
                  name,
                  status)
              SELECT
                  candidate_id,
                  name,
                  status
              FROM image_candidate_qualifications;

              DROP TABLE image_candidate_qualifications;
              DROP TABLE image_candidates;
              DROP TABLE image_build_requests;
              DROP TABLE image_recipe_versions;

              ALTER TABLE image_recipe_versions_migration24
                  RENAME TO image_recipe_versions;
              ALTER TABLE image_build_requests_migration24
                  RENAME TO image_build_requests;
              ALTER TABLE image_candidates_migration24
                  RENAME TO image_candidates;
              ALTER TABLE image_candidate_qualifications_migration24
                  RENAME TO image_candidate_qualifications;

              CREATE INDEX ix_image_recipe_versions_tenant_recipe
                  ON image_recipe_versions (
                      tenant_id,
                      recipe_id,
                      version DESC);

              CREATE INDEX ix_image_recipe_versions_tenant_active
                  ON image_recipe_versions (
                      tenant_id,
                      recipe_id,
                      registration_id,
                      version DESC)
                  WHERE disabled_at IS NULL;

              CREATE TRIGGER trg_image_recipe_versions_immutable
              BEFORE UPDATE ON image_recipe_versions
              FOR EACH ROW
              WHEN OLD.tenant_id <> NEW.tenant_id
                OR OLD.registration_id <> NEW.registration_id
                OR OLD.version <> NEW.version
                OR OLD.github_installation_id <> NEW.github_installation_id
                OR OLD.github_repository_id <> NEW.github_repository_id
                OR OLD.github_workflow_id <> NEW.github_workflow_id
                OR OLD.repository_owner <> NEW.repository_owner
                OR OLD.repository_name <> NEW.repository_name
                OR OLD.workflow_path <> NEW.workflow_path
                OR OLD.workflow_blob_sha <> NEW.workflow_blob_sha
                OR OLD.dispatch_ref <> NEW.dispatch_ref
                OR OLD.recipe_id <> NEW.recipe_id
                OR OLD.candidate_schema_version <> NEW.candidate_schema_version
                OR OLD.source_ref_policy_json <> NEW.source_ref_policy_json
                OR OLD.input_schema_json <> NEW.input_schema_json
                OR OLD.created_by_github_user_id
                    <> NEW.created_by_github_user_id
                OR OLD.created_at <> NEW.created_at
                OR (OLD.disabled_at IS NOT NULL
                    AND (OLD.disabled_at <> NEW.disabled_at
                        OR OLD.disabled_by_github_user_id
                            <> NEW.disabled_by_github_user_id))
                OR (OLD.disabled_at IS NULL
                    AND ((NEW.disabled_at IS NULL
                          AND NEW.disabled_by_github_user_id IS NOT NULL)
                        OR (NEW.disabled_at IS NOT NULL
                          AND NEW.disabled_by_github_user_id IS NULL)))
              BEGIN
                  SELECT RAISE(
                      ABORT,
                      'image recipe registration identity is immutable');
              END;

              CREATE INDEX ix_image_build_requests_tenant_requested
                  ON image_build_requests (
                      tenant_id,
                      requested_at DESC,
                      request_id DESC);

              CREATE INDEX ix_image_build_requests_tenant_status
                  ON image_build_requests (
                      tenant_id,
                      status,
                      requested_at DESC,
                      request_id DESC);

              CREATE INDEX ix_image_build_requests_active
                  ON image_build_requests (
                      status,
                      updated_at,
                      tenant_id,
                      request_id)
                  WHERE status IN (
                      'requested',
                      'dispatching',
                      'building',
                      'qualifying');

              CREATE TRIGGER trg_image_build_requests_identity_immutable
              BEFORE UPDATE ON image_build_requests
              FOR EACH ROW
              WHEN OLD.request_id <> NEW.request_id
                OR OLD.tenant_id <> NEW.tenant_id
                OR OLD.registration_id <> NEW.registration_id
                OR OLD.registration_version <> NEW.registration_version
                OR OLD.recipe_id <> NEW.recipe_id
                OR OLD.source_repository <> NEW.source_repository
                OR OLD.source_commit <> NEW.source_commit
                OR OLD.input_values_json <> NEW.input_values_json
                OR OLD.input_values_sha256 <> NEW.input_values_sha256
                OR OLD.requested_by_github_user_id
                    <> NEW.requested_by_github_user_id
                OR OLD.requested_at <> NEW.requested_at
                OR (OLD.github_run_id IS NOT NULL
                    AND (OLD.github_run_id <> NEW.github_run_id
                        OR OLD.github_run_url <> NEW.github_run_url))
              BEGIN
                  SELECT RAISE(
                      ABORT,
                      'image build request identity is immutable');
              END;

              CREATE TRIGGER trg_image_build_requests_monotonic
              BEFORE UPDATE ON image_build_requests
              FOR EACH ROW
              WHEN NEW.updated_at < OLD.updated_at
                OR OLD.status IN ('ready', 'blocked', 'failed')
                OR NOT (
                    (OLD.status = 'requested'
                        AND NEW.status = 'dispatching')
                    OR (OLD.status = 'dispatching'
                        AND NEW.status = 'building')
                    OR (OLD.status = 'building'
                        AND NEW.status = 'qualifying')
                    OR (OLD.status = 'qualifying'
                        AND NEW.status IN ('ready', 'blocked', 'failed')))
              BEGIN
                  SELECT RAISE(
                      ABORT,
                      'invalid image build request transition');
              END;

              CREATE INDEX ix_image_candidates_tenant_stored
                  ON image_candidates (
                      tenant_id,
                      stored_at DESC,
                      candidate_id DESC);

              CREATE TRIGGER trg_image_candidates_immutable_update
              BEFORE UPDATE ON image_candidates
              FOR EACH ROW
              BEGIN
                  SELECT RAISE(ABORT, 'image candidates are immutable');
              END;

              CREATE TRIGGER trg_image_candidate_qualifications_ready
              BEFORE INSERT ON image_candidate_qualifications
              FOR EACH ROW
              WHEN NEW.status <> 'passed'
                AND EXISTS (
                    SELECT 1
                    FROM image_candidates
                    WHERE candidate_id = NEW.candidate_id
                      AND outcome = 'ready')
              BEGIN
                  SELECT RAISE(
                      ABORT,
                      'ready image candidate qualifications must pass');
              END;

              CREATE TRIGGER trg_image_candidate_qualifications_immutable_update
              BEFORE UPDATE ON image_candidate_qualifications
              FOR EACH ROW
              BEGIN
                  SELECT RAISE(
                      ABORT,
                      'image candidate qualifications are immutable');
              END;

              CREATE TEMP TABLE migration24_image_recipe_fk_check AS
              SELECT *
              FROM pragma_foreign_key_check;

              CREATE TEMP TABLE migration24_image_recipe_fk_guard (
                  fk_ok INTEGER NOT NULL CHECK (fk_ok = 1)
              );

              INSERT INTO migration24_image_recipe_fk_guard (fk_ok)
              SELECT CASE
                  WHEN EXISTS (
                      SELECT 1
                      FROM migration24_image_recipe_fk_check)
                  THEN 0
                  ELSE 1
              END;

              DROP TABLE migration24_image_recipe_fk_guard;
              DROP TABLE migration24_image_recipe_fk_check;

              """),
        new(
              25,
              "restart-safe-image-build-execution",
              """
              ALTER TABLE image_build_requests
                  ADD COLUMN source_ref TEXT NOT NULL DEFAULT ''
                      CHECK (length(source_ref) <= 255);

              ALTER TABLE image_build_requests
                  ADD COLUMN github_run_api_url TEXT NULL
                      CHECK (github_run_api_url IS NULL
                          OR length(github_run_api_url) BETWEEN 1 AND 512);

              ALTER TABLE image_build_requests
                  ADD COLUMN next_attempt_at TEXT NOT NULL
                      DEFAULT '1970-01-01T00:00:00.0000000+00:00';

              ALTER TABLE image_build_requests
                  ADD COLUMN lease_owner TEXT NULL
                      CHECK (lease_owner IS NULL
                          OR length(lease_owner) BETWEEN 1 AND 128);

              ALTER TABLE image_build_requests
                  ADD COLUMN lease_expires_at TEXT NULL;

              ALTER TABLE image_build_requests
                  ADD COLUMN dispatch_safe_to_retry INTEGER NOT NULL DEFAULT 0
                      CHECK (dispatch_safe_to_retry IN (0, 1));

              ALTER TABLE image_build_requests
                  ADD COLUMN dispatch_started_at TEXT NULL;

              ALTER TABLE image_build_requests
                  ADD COLUMN dispatch_attempts INTEGER NOT NULL DEFAULT 0
                      CHECK (dispatch_attempts >= 0);

              ALTER TABLE image_build_requests
                  ADD COLUMN poll_attempts INTEGER NOT NULL DEFAULT 0
                      CHECK (poll_attempts >= 0);

              ALTER TABLE image_build_requests
                  ADD COLUMN run_not_found_attempts INTEGER NOT NULL DEFAULT 0
                      CHECK (run_not_found_attempts >= 0);

              ALTER TABLE image_build_requests
                  ADD COLUMN revision_not_found_attempts INTEGER NOT NULL DEFAULT 0
                      CHECK (revision_not_found_attempts >= 0);

              ALTER TABLE image_build_requests
                  ADD COLUMN last_external_status TEXT NULL
                      CHECK (last_external_status IS NULL
                          OR length(last_external_status) BETWEEN 1 AND 64);

              DROP INDEX ix_image_build_requests_active;

              CREATE INDEX ix_image_build_requests_due
                  ON image_build_requests (
                      next_attempt_at,
                      requested_at,
                      tenant_id,
                      request_id)
                  WHERE status IN ('requested', 'dispatching', 'building');

              DROP TRIGGER trg_image_build_requests_identity_immutable;

              CREATE TRIGGER trg_image_build_requests_identity_immutable
              BEFORE UPDATE ON image_build_requests
              FOR EACH ROW
              WHEN OLD.request_id <> NEW.request_id
                OR OLD.tenant_id <> NEW.tenant_id
                OR OLD.registration_id <> NEW.registration_id
                OR OLD.registration_version <> NEW.registration_version
                OR OLD.recipe_id <> NEW.recipe_id
                OR OLD.source_repository <> NEW.source_repository
                OR OLD.source_commit <> NEW.source_commit
                OR OLD.source_ref <> NEW.source_ref
                OR OLD.input_values_json <> NEW.input_values_json
                OR OLD.input_values_sha256 <> NEW.input_values_sha256
                OR OLD.requested_by_github_user_id
                    <> NEW.requested_by_github_user_id
                OR OLD.requested_at <> NEW.requested_at
                OR (OLD.github_run_id IS NOT NULL
                    AND (OLD.github_run_id <> NEW.github_run_id
                        OR OLD.github_run_url <> NEW.github_run_url
                        OR OLD.github_run_api_url <> NEW.github_run_api_url))
              BEGIN
                  SELECT RAISE(
                      ABORT,
                      'image build request identity is immutable');
              END;

              DROP TRIGGER trg_image_build_requests_monotonic;

              CREATE TRIGGER trg_image_build_requests_monotonic
              BEFORE UPDATE ON image_build_requests
              FOR EACH ROW
              WHEN NEW.updated_at < OLD.updated_at
                OR OLD.status IN ('ready', 'blocked', 'failed')
                OR NOT (
                    OLD.status = NEW.status
                    OR (OLD.status = 'requested'
                        AND NEW.status IN ('dispatching', 'blocked', 'failed'))
                    OR (OLD.status = 'dispatching'
                        AND NEW.status IN ('building', 'blocked', 'failed'))
                    OR (OLD.status = 'building'
                        AND NEW.status IN ('qualifying', 'blocked', 'failed'))
                    OR (OLD.status = 'qualifying'
                        AND NEW.status IN ('ready', 'blocked', 'failed')))
              BEGIN
                  SELECT RAISE(
                      ABORT,
                      'invalid image build request transition');
              END;

              CREATE TRIGGER trg_image_build_requests_execution_invariants
              BEFORE UPDATE ON image_build_requests
              FOR EACH ROW
              WHEN (NEW.lease_owner IS NULL) <> (NEW.lease_expires_at IS NULL)
                OR (NEW.status IN ('ready', 'blocked', 'failed', 'qualifying')
                    AND (NEW.lease_owner IS NOT NULL
                        OR NEW.dispatch_safe_to_retry <> 0))
                OR (NEW.dispatch_safe_to_retry = 1
                    AND NEW.status <> 'dispatching')
                OR (NEW.status IN ('building', 'qualifying')
                    AND (NEW.github_run_id IS NULL
                        OR NEW.github_run_url IS NULL
                        OR NEW.github_run_api_url IS NULL))
                OR ((NEW.github_run_id IS NULL)
                    <> (NEW.github_run_api_url IS NULL))
              BEGIN
                  SELECT RAISE(
                      ABORT,
                      'invalid image build request execution state');
              END;

              CREATE TRIGGER trg_image_build_requests_insert_execution
              BEFORE INSERT ON image_build_requests
              FOR EACH ROW
              WHEN NEW.status <> 'requested'
                OR length(NEW.source_ref) < 1
                OR NEW.github_run_api_url IS NOT NULL
                OR NEW.lease_owner IS NOT NULL
                OR NEW.lease_expires_at IS NOT NULL
                OR NEW.dispatch_safe_to_retry <> 0
                OR NEW.dispatch_started_at IS NOT NULL
                OR NEW.dispatch_attempts <> 0
                OR NEW.poll_attempts <> 0
                OR NEW.run_not_found_attempts <> 0
                OR NEW.revision_not_found_attempts <> 0
                OR NEW.last_external_status IS NOT NULL
              BEGIN
                  SELECT RAISE(
                      ABORT,
                      'image build requests must start as unclaimed requested work');
              END;

              UPDATE image_build_requests
              SET status = 'blocked',
                  terminal_category = 'migration-source-ref-missing',
                  terminal_detail = 'The legacy request lacks exact source-ref authority.',
                  last_external_status = 'migration-source-ref-missing',
                  github_run_api_url = CASE
                      WHEN github_run_id IS NULL THEN NULL
                      ELSE github_run_url
                  END
              WHERE source_ref = ''
                AND status IN (
                    'requested',
                    'dispatching',
                    'building',
                    'qualifying');

              """),
        new(
              26,
              "support-session-lifecycle-projection",
              """
              ALTER TABLE support_sessions
                  ADD COLUMN dispatched_at TEXT NULL;

              ALTER TABLE support_sessions
                  ADD COLUMN rejection_disposition TEXT NULL
                      CHECK (rejection_disposition IS NULL
                          OR rejection_disposition IN (
                              'envelope-unsupported',
                              'envelope-signature-rejected',
                              'envelope-payload-rejected',
                              'request-malformed',
                              'session-mismatch',
                              'wrong-tenant-or-node',
                              'unsupported-capability',
                              'unsupported-diagnostic-mode',
                              'request-expired',
                              'invalid-nonce',
                              'request-replay',
                              'replay-pending',
                              'broker-markdown-rejected',
                              'broker-report-rejected',
                              'validation-rejected',
                              'result-unavailable'));

              CREATE TRIGGER
                  trg_support_sessions_rejection_disposition_insert
              BEFORE INSERT ON support_sessions
              FOR EACH ROW
              WHEN (NEW.status = 'rejected')
                  <> (NEW.rejection_disposition IS NOT NULL)
              BEGIN
                  SELECT RAISE(
                      ABORT,
                      'support rejection disposition must match rejected status');
              END;

              CREATE TRIGGER
                  trg_support_sessions_rejection_disposition_update
              BEFORE UPDATE OF status, rejection_disposition
                  ON support_sessions
              FOR EACH ROW
              WHEN (NEW.status = 'rejected')
                  <> (NEW.rejection_disposition IS NOT NULL)
              BEGIN
                  SELECT RAISE(
                      ABORT,
                      'support rejection disposition must match rejected status');
              END;
              """),
        new(
              27,
              "support-session-broker-rejection-dispositions",
              """
              ALTER TABLE support_sessions
                  ADD COLUMN rejection_disposition_v2 TEXT NULL
                      CHECK (rejection_disposition_v2 IS NULL
                          OR rejection_disposition_v2 IN (
                              'envelope-unsupported',
                              'envelope-signature-rejected',
                              'envelope-payload-rejected',
                              'request-malformed',
                              'session-mismatch',
                              'wrong-tenant-or-node',
                              'unsupported-capability',
                              'unsupported-diagnostic-mode',
                              'request-expired',
                              'invalid-nonce',
                              'request-replay',
                              'replay-pending',
                              'broker-markdown-rejected',
                              'broker-report-rejected',
                              'broker-invalid-mode',
                              'broker-invalid-profile',
                              'broker-script-missing',
                              'broker-evidence-access-denied',
                              'broker-execution-failed',
                              'broker-response-invalid',
                              'broker-io-unavailable',
                              'broker-timeout',
                              'validation-rejected',
                              'result-unavailable'));

              UPDATE support_sessions
              SET rejection_disposition_v2 =
                  rejection_disposition;

              DROP TRIGGER
                  trg_support_sessions_rejection_disposition_insert;

              DROP TRIGGER
                  trg_support_sessions_rejection_disposition_update;

              ALTER TABLE support_sessions
                  DROP COLUMN rejection_disposition;

              ALTER TABLE support_sessions
                  RENAME COLUMN rejection_disposition_v2
                      TO rejection_disposition;

              CREATE TRIGGER
                  trg_support_sessions_rejection_disposition_insert
              BEFORE INSERT ON support_sessions
              FOR EACH ROW
              WHEN (NEW.status = 'rejected')
                  <> (NEW.rejection_disposition IS NOT NULL)
              BEGIN
                  SELECT RAISE(
                      ABORT,
                      'support rejection disposition must match rejected status');
              END;

              CREATE TRIGGER
                  trg_support_sessions_rejection_disposition_update
              BEFORE UPDATE OF status, rejection_disposition
                  ON support_sessions
              FOR EACH ROW
              WHEN (NEW.status = 'rejected')
                  <> (NEW.rejection_disposition IS NOT NULL)
              BEGIN
                  SELECT RAISE(
                      ABORT,
                      'support rejection disposition must match rejected status');
              END;
              """),
        new(
              28,
              "qualifying-image-candidate-execution",
              """
              DROP INDEX ix_image_build_requests_due;

              CREATE INDEX ix_image_build_requests_due
                  ON image_build_requests (
                      next_attempt_at,
                      requested_at,
                      tenant_id,
                      request_id)
                  WHERE status IN (
                      'requested',
                      'dispatching',
                      'building',
                      'qualifying');

              DROP TRIGGER
                  trg_image_build_requests_execution_invariants;

              CREATE TRIGGER trg_image_build_requests_execution_invariants
              BEFORE UPDATE ON image_build_requests
              FOR EACH ROW
              WHEN (NEW.lease_owner IS NULL) <> (NEW.lease_expires_at IS NULL)
                OR (NEW.status IN ('ready', 'blocked', 'failed')
                    AND (NEW.lease_owner IS NOT NULL
                        OR NEW.dispatch_safe_to_retry <> 0))
                OR (NEW.dispatch_safe_to_retry = 1
                    AND NEW.status <> 'dispatching')
                OR (NEW.status IN ('building', 'qualifying')
                    AND (NEW.github_run_id IS NULL
                        OR NEW.github_run_url IS NULL
                        OR NEW.github_run_api_url IS NULL))
                OR ((NEW.github_run_id IS NULL)
                    <> (NEW.github_run_api_url IS NULL))
              BEGIN
                  SELECT RAISE(
                      ABORT,
                      'invalid image build request execution state');
              END;
              """),
        new(
              29,
              "typed-profile-image-rollout",
              """
              DROP TRIGGER IF EXISTS trg_capacity_commands_require_operation_slot;
              DROP TRIGGER IF EXISTS trg_recovery_commands_require_operation_slot;

              CREATE TABLE profile_active_operations_next (
                  node_id TEXT NOT NULL,
                  profile_id TEXT NOT NULL,
                  operation_kind TEXT NOT NULL
                      CHECK (operation_kind IN (
                          'capacity',
                          'recovery',
                          'image-rollout')),
                  command_id TEXT NOT NULL UNIQUE,
                  acquired_at TEXT NOT NULL,
                  PRIMARY KEY (node_id, profile_id),
                  FOREIGN KEY (node_id)
                      REFERENCES nodes(node_id) ON DELETE CASCADE
              );

              INSERT INTO profile_active_operations_next (
                  node_id,
                  profile_id,
                  operation_kind,
                  command_id,
                  acquired_at)
              SELECT
                  node_id,
                  profile_id,
                  operation_kind,
                  command_id,
                  acquired_at
              FROM profile_active_operations;

              DROP TABLE profile_active_operations;

              ALTER TABLE profile_active_operations_next
                  RENAME TO profile_active_operations;

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

              ALTER TABLE nodes
                  ADD COLUMN image_rollout_capability_json TEXT NULL;

              ALTER TABLE nodes
                  ADD COLUMN image_rollout_capability_at TEXT NULL;

              CREATE TABLE image_rollout_commands (
                  command_id TEXT PRIMARY KEY,
                  node_id TEXT NOT NULL,
                  profile_id TEXT NOT NULL,
                  candidate_id TEXT NOT NULL,
                  recipe_id TEXT NOT NULL
                      CHECK (length(recipe_id) BETWEEN 1 AND 100),
                  target_digest TEXT NOT NULL
                      CHECK (length(target_digest) = 71
                          AND substr(target_digest, 1, 7) = 'sha256:'),
                  target_platform TEXT NOT NULL
                      CHECK (target_platform IN (
                          'linux/amd64',
                          'linux/arm64')),
                  expected_current_image_reference TEXT NULL
                      CHECK (expected_current_image_reference IS NULL
                          OR length(expected_current_image_reference)
                              BETWEEN 1 AND 512),
                  expected_current_image_digest TEXT NULL
                      CHECK (expected_current_image_digest IS NULL
                          OR (length(expected_current_image_digest) = 71
                              AND substr(
                                  expected_current_image_digest, 1, 7)
                                  = 'sha256:')),
                  expected_current_local_image_id TEXT NULL
                      CHECK (expected_current_local_image_id IS NULL
                          OR (length(expected_current_local_image_id) = 71
                              AND substr(
                                  expected_current_local_image_id, 1, 7)
                                  = 'sha256:')),
                  expected_current_worker_revision TEXT NULL
                      CHECK (expected_current_worker_revision IS NULL
                          OR length(expected_current_worker_revision) = 64),
                  expected_static_fingerprint TEXT NOT NULL
                      CHECK (length(expected_static_fingerprint) = 64),
                  expected_preserved_configuration_fingerprint TEXT NOT NULL
                      CHECK (length(
                          expected_preserved_configuration_fingerprint) = 64),
                  expected_routing_fingerprint TEXT NOT NULL
                      CHECK (length(expected_routing_fingerprint) = 64),
                  expected_desired_generation INTEGER NOT NULL
                      CHECK (expected_desired_generation >= 0),
                  expected_desired_state_hash TEXT NULL
                      CHECK (expected_desired_state_hash IS NULL
                          OR length(expected_desired_state_hash) = 64),
                  previous_image_reference TEXT NULL
                      CHECK (previous_image_reference IS NULL
                          OR length(previous_image_reference)
                              BETWEEN 1 AND 512),
                  previous_image_digest TEXT NULL
                      CHECK (previous_image_digest IS NULL
                          OR (length(previous_image_digest) = 71
                              AND substr(previous_image_digest, 1, 7)
                                  = 'sha256:')),
                  previous_worker_revision TEXT NULL
                      CHECK (previous_worker_revision IS NULL
                          OR length(previous_worker_revision) = 64),
                  previous_candidate_id TEXT NULL
                      CHECK (previous_candidate_id IS NULL
                          OR length(previous_candidate_id) = 36),
                  previous_recipe_id TEXT NULL
                      CHECK (previous_recipe_id IS NULL
                          OR length(previous_recipe_id) BETWEEN 1 AND 100),
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
                              'recipe-not-allowed',
                              'registry-not-allowed',
                              'stale-fence',
                              'expired',
                              'unsupported',
                              'unsupported-architecture',
                              'unsupported-topology',
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
                  target_worker_revision TEXT NULL
                      CHECK (target_worker_revision IS NULL
                          OR length(target_worker_revision) = 64),
                  manager_convergence_status TEXT NULL
                      CHECK (manager_convergence_status IS NULL
                          OR manager_convergence_status IN (
                              'current',
                              'rolling',
                              'degraded')),
                  current_workers INTEGER NULL
                      CHECK (current_workers IS NULL
                          OR current_workers >= 0),
                  stale_workers INTEGER NULL
                      CHECK (stale_workers IS NULL
                          OR stale_workers >= 0),
                  last_error TEXT NULL
                      CHECK (last_error IS NULL
                          OR length(last_error) <= 128),
                  result_message TEXT NULL
                      CHECK (result_message IS NULL
                          OR length(result_message) <= 512),
                  idempotency_key TEXT NOT NULL
                      CHECK (length(idempotency_key) BETWEEN 8 AND 200),
                  idempotency_signature TEXT NOT NULL
                      CHECK (length(idempotency_signature) = 64),
                  FOREIGN KEY (node_id)
                      REFERENCES nodes(node_id) ON DELETE CASCADE,
                  FOREIGN KEY (requested_by_github_user_id)
                      REFERENCES dashboard_users(github_user_id)
              );

              CREATE UNIQUE INDEX ix_image_rollout_commands_profile_active
                  ON image_rollout_commands (node_id, profile_id)
                  WHERE status IN ('queued', 'claimed', 'started');

              CREATE UNIQUE INDEX ix_image_rollout_commands_idempotency
                  ON image_rollout_commands (
                      node_id,
                      requested_by_github_user_id,
                      idempotency_key);

              CREATE INDEX ix_image_rollout_commands_node_requested
                  ON image_rollout_commands (
                      node_id,
                      profile_id,
                      requested_at DESC);

              CREATE INDEX ix_image_rollout_commands_candidate
                  ON image_rollout_commands (candidate_id);

              CREATE TRIGGER trg_image_rollout_commands_require_operation_slot
              BEFORE INSERT ON image_rollout_commands
              FOR EACH ROW
              WHEN NOT EXISTS (
                  SELECT 1
                  FROM profile_active_operations
                  WHERE node_id = NEW.node_id
                    AND profile_id = NEW.profile_id
                    AND command_id = NEW.command_id
                    AND operation_kind = 'image-rollout')
              BEGIN
                  SELECT RAISE(
                      ABORT,
                      'image rollout command requires an exclusive profile operation');
              END;

              CREATE TRIGGER trg_image_rollout_commands_insert_queued
              BEFORE INSERT ON image_rollout_commands
              FOR EACH ROW
              WHEN NEW.status <> 'queued'
                OR NEW.delivered_at IS NOT NULL
                OR NEW.claimed_at IS NOT NULL
                OR NEW.started_at IS NOT NULL
                OR NEW.completed_at IS NOT NULL
                OR NEW.failure_category IS NOT NULL
                OR NEW.target_worker_revision IS NOT NULL
                OR NEW.manager_convergence_status IS NOT NULL
                OR NEW.current_workers IS NOT NULL
                OR NEW.stale_workers IS NOT NULL
                OR NEW.last_error IS NOT NULL
                OR NEW.result_message IS NOT NULL
              BEGIN
                  SELECT RAISE(
                      ABORT,
                      'image rollout commands must be inserted as queued');
              END;

              CREATE TRIGGER trg_image_rollout_commands_immutable
              BEFORE UPDATE ON image_rollout_commands
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
                OR OLD.candidate_id <> NEW.candidate_id
                OR OLD.recipe_id <> NEW.recipe_id
                OR OLD.target_digest <> NEW.target_digest
                OR OLD.target_platform <> NEW.target_platform
                OR OLD.requested_by_github_user_id
                    <> NEW.requested_by_github_user_id
                OR OLD.requested_at <> NEW.requested_at
                OR OLD.expires_at <> NEW.expires_at
                OR IFNULL(OLD.expected_current_image_reference, '')
                    <> IFNULL(NEW.expected_current_image_reference, '')
                OR IFNULL(OLD.expected_current_image_digest, '')
                    <> IFNULL(NEW.expected_current_image_digest, '')
                OR IFNULL(OLD.expected_current_local_image_id, '')
                    <> IFNULL(NEW.expected_current_local_image_id, '')
                OR IFNULL(OLD.expected_current_worker_revision, '')
                    <> IFNULL(NEW.expected_current_worker_revision, '')
                OR OLD.expected_static_fingerprint
                    <> NEW.expected_static_fingerprint
                OR OLD.expected_preserved_configuration_fingerprint
                    <> NEW.expected_preserved_configuration_fingerprint
                OR OLD.expected_routing_fingerprint
                    <> NEW.expected_routing_fingerprint
                OR OLD.expected_desired_generation
                    <> NEW.expected_desired_generation
                OR IFNULL(OLD.expected_desired_state_hash, '')
                    <> IFNULL(NEW.expected_desired_state_hash, '')
                OR IFNULL(OLD.previous_image_reference, '')
                    <> IFNULL(NEW.previous_image_reference, '')
                OR IFNULL(OLD.previous_image_digest, '')
                    <> IFNULL(NEW.previous_image_digest, '')
                OR IFNULL(OLD.previous_worker_revision, '')
                    <> IFNULL(NEW.previous_worker_revision, '')
                OR IFNULL(OLD.previous_candidate_id, '')
                    <> IFNULL(NEW.previous_candidate_id, '')
                OR IFNULL(OLD.previous_recipe_id, '')
                    <> IFNULL(NEW.previous_recipe_id, '')
                OR OLD.idempotency_key <> NEW.idempotency_key
                OR OLD.idempotency_signature <> NEW.idempotency_signature
              BEGIN
                  SELECT RAISE(
                      ABORT,
                      'image rollout audit data and terminal outcomes are immutable');
              END;

              CREATE TRIGGER trg_image_rollout_commands_transitions
              BEFORE UPDATE OF status ON image_rollout_commands
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
                      'image rollout lifecycle transition is not allowed');
              END;

              CREATE TRIGGER trg_image_rollout_commands_terminal_evidence
              BEFORE UPDATE ON image_rollout_commands
              FOR EACH ROW
              WHEN NEW.status IN (
                      'succeeded',
                      'rejected',
                      'failed',
                      'expired',
                      'indeterminate')
                AND (NEW.completed_at IS NULL
                  OR (NEW.status = 'succeeded'
                      AND NEW.failure_category IS NOT NULL)
                  OR (NEW.status <> 'succeeded'
                      AND NEW.failure_category IS NULL)
                  OR (NEW.status = 'succeeded'
                      AND (NEW.target_worker_revision IS NULL
                          OR NEW.current_workers IS NULL
                          OR NEW.stale_workers IS NULL
                          OR NEW.manager_convergence_status IS NULL)))
              BEGIN
                  SELECT RAISE(
                      ABORT,
                      'image rollout terminal state requires bounded evidence');
              END;
              """),
        new(
              30,
              "frozen-image-rollout-campaigns",
              """
              CREATE TABLE image_rollout_campaigns (
                  campaign_id TEXT PRIMARY KEY
                      CHECK (length(campaign_id) = 36),
                  tenant_id TEXT NOT NULL,
                  kind TEXT NOT NULL
                      CHECK (kind IN ('forward', 'rollback')),
                  source_campaign_id TEXT NULL
                      CHECK (source_campaign_id IS NULL
                          OR length(source_campaign_id) = 36),
                  candidate_id TEXT NULL
                      CHECK (candidate_id IS NULL
                          OR length(candidate_id) = 36),
                  recipe_id TEXT NULL
                      CHECK (recipe_id IS NULL
                          OR length(recipe_id) BETWEEN 1 AND 100),
                  target_digest TEXT NULL
                      CHECK (target_digest IS NULL
                          OR (length(target_digest) = 71
                              AND substr(target_digest, 1, 7) = 'sha256:'
                              AND substr(target_digest, 8)
                                  NOT GLOB '*[^0-9a-f]*')),
                  target_platform TEXT NULL
                      CHECK (target_platform IS NULL
                          OR target_platform IN (
                              'linux/amd64',
                              'linux/arm64')),
                  target_set_hash TEXT NOT NULL
                      CHECK (length(target_set_hash) = 64
                          AND target_set_hash NOT GLOB '*[^0-9a-f]*'),
                  status TEXT NOT NULL
                      CHECK (status IN (
                          'draft',
                          'awaiting-approval',
                          'running',
                          'paused',
                          'complete',
                          'partial',
                          'blocked',
                          'cancelled')),
                  revision INTEGER NOT NULL DEFAULT 0
                      CHECK (revision >= 0),
                  wave_size INTEGER NULL
                      CHECK (wave_size IS NULL
                          OR wave_size BETWEEN 1 AND 100),
                  requested_by_github_user_id TEXT NOT NULL
                      CHECK (length(requested_by_github_user_id)
                          BETWEEN 1 AND 64),
                  requested_at TEXT NOT NULL,
                  configured_by_github_user_id TEXT NULL
                      CHECK (configured_by_github_user_id IS NULL
                          OR length(configured_by_github_user_id)
                              BETWEEN 1 AND 64),
                  configured_at TEXT NULL,
                  paused_at TEXT NULL,
                  cancelled_at TEXT NULL,
                  completed_at TEXT NULL,
                  UNIQUE (tenant_id, campaign_id),
                  CHECK (
                      (kind = 'forward'
                       AND source_campaign_id IS NULL
                       AND candidate_id IS NOT NULL
                       AND recipe_id IS NOT NULL
                       AND target_digest IS NOT NULL
                       AND target_platform IS NOT NULL)
                      OR
                      (kind = 'rollback'
                       AND source_campaign_id IS NOT NULL
                       AND candidate_id IS NULL
                       AND recipe_id IS NULL
                       AND target_digest IS NULL
                       AND target_platform IS NULL)),
                  CHECK (
                      (configured_by_github_user_id IS NULL
                       AND configured_at IS NULL
                       AND wave_size IS NULL)
                      OR
                      (configured_by_github_user_id IS NOT NULL
                       AND configured_at IS NOT NULL
                       AND wave_size IS NOT NULL)),
                  CHECK (
                      status NOT IN (
                          'awaiting-approval',
                          'running',
                          'paused',
                          'complete',
                          'partial')
                      OR configured_at IS NOT NULL),
                  CHECK (
                      (status IN (
                          'complete',
                          'partial',
                          'blocked',
                          'cancelled')
                       AND completed_at IS NOT NULL)
                      OR
                      (status NOT IN (
                          'complete',
                          'partial',
                          'blocked',
                          'cancelled')
                       AND completed_at IS NULL)),
                  CHECK ((status = 'cancelled') = (cancelled_at IS NOT NULL)),
                  FOREIGN KEY (tenant_id)
                      REFERENCES tenants(tenant_id) ON DELETE CASCADE,
                  FOREIGN KEY (requested_by_github_user_id)
                      REFERENCES dashboard_users(github_user_id),
                  FOREIGN KEY (configured_by_github_user_id)
                      REFERENCES dashboard_users(github_user_id),
                  FOREIGN KEY (tenant_id, source_campaign_id)
                      REFERENCES image_rollout_campaigns (
                          tenant_id,
                          campaign_id)
              );

              CREATE INDEX ix_image_rollout_campaigns_tenant_requested
                  ON image_rollout_campaigns (
                      tenant_id,
                      requested_at DESC,
                      campaign_id DESC);

              CREATE INDEX ix_image_rollout_campaigns_active
                  ON image_rollout_campaigns (
                      status,
                      requested_at,
                      tenant_id,
                      campaign_id)
                  WHERE status IN (
                      'draft',
                      'awaiting-approval',
                      'running',
                      'paused');

              CREATE TABLE image_rollout_campaign_targets (
                  target_id TEXT PRIMARY KEY
                      CHECK (length(target_id) = 36),
                  campaign_id TEXT NOT NULL
                      CHECK (length(campaign_id) = 36),
                  node_id TEXT NOT NULL
                      CHECK (length(node_id) = 36),
                  node_display_name TEXT NOT NULL
                      CHECK (length(node_display_name) BETWEEN 1 AND 128),
                  profile_id TEXT NOT NULL
                      CHECK (length(profile_id) BETWEEN 1 AND 32),
                  candidate_id TEXT NULL
                      CHECK (candidate_id IS NULL
                          OR length(candidate_id) = 36),
                  recipe_id TEXT NULL
                      CHECK (recipe_id IS NULL
                          OR length(recipe_id) BETWEEN 1 AND 100),
                  target_digest TEXT NULL
                      CHECK (target_digest IS NULL
                          OR (length(target_digest) = 71
                              AND substr(target_digest, 1, 7) = 'sha256:'
                              AND substr(target_digest, 8)
                                  NOT GLOB '*[^0-9a-f]*')),
                  target_platform TEXT NULL
                      CHECK (target_platform IS NULL
                          OR target_platform IN (
                              'linux/amd64',
                              'linux/arm64')),
                  expected_current_image_reference TEXT NULL
                      CHECK (expected_current_image_reference IS NULL
                          OR length(expected_current_image_reference)
                              BETWEEN 1 AND 512),
                  expected_current_image_digest TEXT NULL
                      CHECK (expected_current_image_digest IS NULL
                          OR (length(expected_current_image_digest) = 71
                              AND substr(
                                  expected_current_image_digest,
                                  1,
                                  7) = 'sha256:')),
                  expected_current_local_image_id TEXT NULL
                      CHECK (expected_current_local_image_id IS NULL
                          OR (length(expected_current_local_image_id) = 71
                              AND substr(
                                  expected_current_local_image_id,
                                  1,
                                  7) = 'sha256:')),
                  expected_current_worker_revision TEXT NULL
                      CHECK (expected_current_worker_revision IS NULL
                          OR length(expected_current_worker_revision) = 64),
                  expected_static_fingerprint TEXT NULL
                      CHECK (expected_static_fingerprint IS NULL
                          OR length(expected_static_fingerprint) = 64),
                  expected_preserved_configuration_fingerprint TEXT NULL
                      CHECK (
                          expected_preserved_configuration_fingerprint IS NULL
                          OR length(
                              expected_preserved_configuration_fingerprint)
                              = 64),
                  expected_routing_fingerprint TEXT NULL
                      CHECK (expected_routing_fingerprint IS NULL
                          OR length(expected_routing_fingerprint) = 64),
                  expected_desired_generation INTEGER NULL
                      CHECK (expected_desired_generation IS NULL
                          OR expected_desired_generation >= 0),
                  expected_desired_state_hash TEXT NULL
                      CHECK (expected_desired_state_hash IS NULL
                          OR length(expected_desired_state_hash) = 64),
                  exclusion_category TEXT NULL
                      CHECK (exclusion_category IS NULL
                          OR exclusion_category IN (
                              'node-offline',
                              'node-revoked',
                              'capability-unavailable',
                              'stale-observed-state',
                              'unsupported-schema',
                              'unsupported-manager',
                              'unsupported-topology',
                              'unsupported-architecture',
                              'recipe-not-allowed',
                              'registry-not-allowed',
                              'policy-disabled',
                              'operation-active',
                              'already-current',
                              'insufficient-evidence',
                              'rollback-authority-unavailable')),
                  status TEXT NOT NULL
                      CHECK (status IN (
                          'eligible',
                          'excluded',
                          'queued',
                          'claimed',
                          'applying',
                          'rolling',
                          'complete',
                          'failed',
                          'blocked',
                          'indeterminate',
                          'cancelled')),
                  wave_number INTEGER NULL
                      CHECK (wave_number IS NULL OR wave_number >= 0),
                  is_canary INTEGER NOT NULL DEFAULT 0
                      CHECK (is_canary IN (0, 1)),
                  command_id TEXT NULL
                      CHECK (command_id IS NULL
                          OR length(command_id) = 36),
                  lease_owner TEXT NULL
                      CHECK (lease_owner IS NULL
                          OR length(lease_owner) BETWEEN 1 AND 128),
                  lease_expires_at TEXT NULL,
                  dispatch_attempts INTEGER NOT NULL DEFAULT 0
                      CHECK (dispatch_attempts >= 0),
                  failure_category TEXT NULL
                      CHECK (failure_category IS NULL
                          OR failure_category IN (
                              'not-allowed',
                              'recipe-not-allowed',
                              'registry-not-allowed',
                              'stale-fence',
                              'expired',
                              'unsupported',
                              'unsupported-architecture',
                              'unsupported-topology',
                              'operation-active',
                              'node-not-found',
                              'idempotency-key-conflict',
                              'wave-blocked',
                              'rate-limited',
                              'timeout',
                              'process-failure',
                              'unknown')),
                  result_message TEXT NULL
                      CHECK (result_message IS NULL
                          OR length(result_message) <= 512),
                  target_worker_revision TEXT NULL
                      CHECK (target_worker_revision IS NULL
                          OR length(target_worker_revision) = 64),
                  manager_convergence_status TEXT NULL
                      CHECK (manager_convergence_status IS NULL
                          OR manager_convergence_status IN (
                              'current',
                              'rolling',
                              'degraded')),
                  current_workers INTEGER NULL
                      CHECK (current_workers IS NULL
                          OR current_workers >= 0),
                  stale_workers INTEGER NULL
                      CHECK (stale_workers IS NULL
                          OR stale_workers >= 0),
                  claimed_at TEXT NULL,
                  started_at TEXT NULL,
                  completed_at TEXT NULL,
                  previous_candidate_id TEXT NULL
                      CHECK (previous_candidate_id IS NULL
                          OR length(previous_candidate_id) = 36),
                  previous_recipe_id TEXT NULL
                      CHECK (previous_recipe_id IS NULL
                          OR length(previous_recipe_id) BETWEEN 1 AND 100),
                  previous_image_reference TEXT NULL
                      CHECK (previous_image_reference IS NULL
                          OR length(previous_image_reference)
                              BETWEEN 1 AND 512),
                  previous_image_digest TEXT NULL
                      CHECK (previous_image_digest IS NULL
                          OR (length(previous_image_digest) = 71
                              AND substr(previous_image_digest, 1, 7)
                                  = 'sha256:')),
                  previous_worker_revision TEXT NULL
                      CHECK (previous_worker_revision IS NULL
                          OR length(previous_worker_revision) = 64),
                  UNIQUE (campaign_id, node_id, profile_id),
                  CHECK (
                      (exclusion_category IS NULL
                       AND candidate_id IS NOT NULL
                       AND recipe_id IS NOT NULL
                       AND target_digest IS NOT NULL
                       AND target_platform IS NOT NULL
                       AND expected_static_fingerprint IS NOT NULL
                       AND expected_preserved_configuration_fingerprint
                           IS NOT NULL
                       AND expected_routing_fingerprint IS NOT NULL
                       AND expected_desired_generation IS NOT NULL)
                      OR
                      (exclusion_category IS NOT NULL
                       AND status = 'excluded')),
                  CHECK (
                      (lease_owner IS NULL AND lease_expires_at IS NULL)
                      OR
                      (lease_owner IS NOT NULL
                       AND lease_expires_at IS NOT NULL)),
                  CHECK (
                      wave_number IS NULL
                      OR ((wave_number = 0) = (is_canary = 1))),
                  CHECK (
                      status NOT IN (
                          'claimed',
                          'applying',
                          'rolling',
                          'complete',
                          'failed',
                          'indeterminate')
                      OR command_id IS NOT NULL),
                  CHECK (
                      status <> 'complete'
                      OR (target_worker_revision IS NOT NULL
                          AND manager_convergence_status = 'current'
                          AND current_workers IS NOT NULL
                          AND stale_workers = 0)),
                  CHECK (
                      (status IN (
                          'complete',
                          'failed',
                          'blocked',
                          'indeterminate',
                          'cancelled')
                       AND completed_at IS NOT NULL)
                      OR status NOT IN (
                          'complete',
                          'failed',
                          'blocked',
                          'indeterminate',
                          'cancelled')),
                  CHECK (
                      (status IN ('failed', 'blocked', 'indeterminate')
                       AND failure_category IS NOT NULL)
                      OR
                      (status NOT IN ('failed', 'blocked', 'indeterminate')
                       AND failure_category IS NULL)),
                  FOREIGN KEY (campaign_id)
                      REFERENCES image_rollout_campaigns(campaign_id)
                      ON DELETE CASCADE
              );

              CREATE UNIQUE INDEX ix_image_rollout_campaign_targets_command
                  ON image_rollout_campaign_targets (command_id)
                  WHERE command_id IS NOT NULL;

              CREATE UNIQUE INDEX ix_image_rollout_campaign_targets_canary
                  ON image_rollout_campaign_targets (campaign_id)
                  WHERE is_canary = 1;

              CREATE UNIQUE INDEX ix_image_rollout_campaign_targets_wave_zero
                  ON image_rollout_campaign_targets (campaign_id)
                  WHERE wave_number = 0;

              CREATE INDEX ix_image_rollout_campaign_targets_campaign_wave
                  ON image_rollout_campaign_targets (
                      campaign_id,
                      wave_number,
                      status,
                      node_id,
                      profile_id);

              CREATE INDEX ix_image_rollout_campaign_targets_due
                  ON image_rollout_campaign_targets (
                      status,
                      lease_expires_at,
                      campaign_id,
                      wave_number,
                      node_id,
                      profile_id)
                  WHERE status = 'queued'
                    AND command_id IS NULL;

              CREATE INDEX ix_image_rollout_campaign_targets_node_active
                  ON image_rollout_campaign_targets (
                      node_id,
                      status,
                      campaign_id)
                  WHERE status IN (
                      'queued',
                      'claimed',
                      'applying',
                      'rolling');

              CREATE TABLE image_rollout_campaign_waves (
                  campaign_id TEXT NOT NULL,
                  wave_number INTEGER NOT NULL
                      CHECK (wave_number >= 0),
                  status TEXT NOT NULL
                      CHECK (status IN (
                          'pending',
                          'approved',
                          'running',
                          'complete',
                          'blocked',
                          'cancelled')),
                  target_count INTEGER NOT NULL
                      CHECK (target_count >= 1),
                  approved_by_github_user_id TEXT NULL
                      CHECK (approved_by_github_user_id IS NULL
                          OR length(approved_by_github_user_id)
                              BETWEEN 1 AND 64),
                  approved_at TEXT NULL,
                  completed_at TEXT NULL,
                  PRIMARY KEY (campaign_id, wave_number),
                  CHECK (
                      (approved_by_github_user_id IS NULL
                       AND approved_at IS NULL)
                      OR
                      (approved_by_github_user_id IS NOT NULL
                       AND approved_at IS NOT NULL)),
                  CHECK (
                      status = 'cancelled'
                      OR ((status = 'pending')
                          = (approved_at IS NULL))),
                  CHECK (
                      (status IN ('complete', 'blocked', 'cancelled')
                       AND completed_at IS NOT NULL)
                      OR
                      (status NOT IN ('complete', 'blocked', 'cancelled')
                       AND completed_at IS NULL)),
                  FOREIGN KEY (campaign_id)
                      REFERENCES image_rollout_campaigns(campaign_id)
                      ON DELETE CASCADE,
                  FOREIGN KEY (approved_by_github_user_id)
                      REFERENCES dashboard_users(github_user_id)
              );

              CREATE INDEX ix_image_rollout_campaign_waves_pending
                  ON image_rollout_campaign_waves (
                      campaign_id,
                      status,
                      wave_number);

              CREATE TRIGGER trg_image_rollout_campaign_waves_target_count
              BEFORE INSERT ON image_rollout_campaign_waves
              FOR EACH ROW
              WHEN NEW.target_count <> (
                  SELECT COUNT(*)
                  FROM image_rollout_campaign_targets
                  WHERE campaign_id = NEW.campaign_id
                    AND wave_number = NEW.wave_number)
              BEGIN
                  SELECT RAISE(
                      ABORT,
                      'campaign wave target count must match frozen assignments');
              END;

              CREATE TABLE image_rollout_campaign_idempotency (
                  tenant_id TEXT NOT NULL,
                  actor_github_user_id TEXT NOT NULL
                      CHECK (length(actor_github_user_id)
                          BETWEEN 1 AND 64),
                  idempotency_key TEXT NOT NULL
                      CHECK (length(idempotency_key) BETWEEN 8 AND 200),
                  action TEXT NOT NULL
                      CHECK (action IN (
                          'create-forward',
                          'create-rollback',
                          'configure',
                          'approve-wave',
                          'pause',
                          'resume',
                          'cancel')),
                  signature TEXT NOT NULL
                      CHECK (length(signature) = 64
                          AND signature NOT GLOB '*[^0-9a-f]*'),
                  campaign_id TEXT NOT NULL
                      CHECK (length(campaign_id) = 36),
                  recorded_at TEXT NOT NULL,
                  PRIMARY KEY (
                      tenant_id,
                      actor_github_user_id,
                      idempotency_key),
                  FOREIGN KEY (tenant_id)
                      REFERENCES tenants(tenant_id) ON DELETE CASCADE,
                  FOREIGN KEY (actor_github_user_id)
                      REFERENCES dashboard_users(github_user_id),
                  FOREIGN KEY (campaign_id)
                      REFERENCES image_rollout_campaigns(campaign_id)
                      ON DELETE CASCADE
              );

              CREATE INDEX ix_image_rollout_campaign_idempotency_campaign
                  ON image_rollout_campaign_idempotency (
                      campaign_id,
                      recorded_at);

              CREATE TRIGGER trg_image_rollout_campaigns_immutable
              BEFORE UPDATE ON image_rollout_campaigns
              FOR EACH ROW
              WHEN OLD.campaign_id <> NEW.campaign_id
                OR OLD.tenant_id <> NEW.tenant_id
                OR OLD.kind <> NEW.kind
                OR IFNULL(OLD.source_campaign_id, '')
                    <> IFNULL(NEW.source_campaign_id, '')
                OR IFNULL(OLD.candidate_id, '')
                    <> IFNULL(NEW.candidate_id, '')
                OR IFNULL(OLD.recipe_id, '')
                    <> IFNULL(NEW.recipe_id, '')
                OR IFNULL(OLD.target_digest, '')
                    <> IFNULL(NEW.target_digest, '')
                OR IFNULL(OLD.target_platform, '')
                    <> IFNULL(NEW.target_platform, '')
                OR OLD.target_set_hash <> NEW.target_set_hash
                OR OLD.requested_by_github_user_id
                    <> NEW.requested_by_github_user_id
                OR OLD.requested_at <> NEW.requested_at
                OR (OLD.wave_size IS NOT NULL
                    AND OLD.wave_size <> NEW.wave_size)
                OR (OLD.configured_by_github_user_id IS NOT NULL
                    AND (OLD.configured_by_github_user_id
                            <> NEW.configured_by_github_user_id
                        OR OLD.configured_at <> NEW.configured_at))
              BEGIN
                  SELECT RAISE(
                      ABORT,
                      'image rollout campaign authority is immutable');
              END;

              CREATE TRIGGER trg_image_rollout_campaigns_transitions
              BEFORE UPDATE OF status ON image_rollout_campaigns
              FOR EACH ROW
              WHEN NOT (
                  OLD.status = NEW.status
                  OR (OLD.status = 'draft'
                      AND NEW.status IN (
                          'awaiting-approval',
                          'cancelled'))
                  OR (OLD.status = 'awaiting-approval'
                      AND NEW.status IN (
                          'running',
                          'paused',
                          'blocked',
                          'cancelled'))
                  OR (OLD.status = 'running'
                      AND NEW.status IN (
                          'awaiting-approval',
                          'paused',
                          'complete',
                          'partial',
                          'blocked',
                          'cancelled'))
                  OR (OLD.status = 'paused'
                      AND NEW.status IN (
                          'awaiting-approval',
                          'running',
                          'complete',
                          'partial',
                          'blocked',
                          'cancelled')))
              BEGIN
                  SELECT RAISE(
                      ABORT,
                      'image rollout campaign transition is not allowed');
              END;

              CREATE TRIGGER trg_image_rollout_campaign_targets_immutable
              BEFORE UPDATE ON image_rollout_campaign_targets
              FOR EACH ROW
              WHEN OLD.target_id <> NEW.target_id
                OR OLD.campaign_id <> NEW.campaign_id
                OR OLD.node_id <> NEW.node_id
                OR OLD.node_display_name <> NEW.node_display_name
                OR OLD.profile_id <> NEW.profile_id
                OR IFNULL(OLD.candidate_id, '')
                    <> IFNULL(NEW.candidate_id, '')
                OR IFNULL(OLD.recipe_id, '')
                    <> IFNULL(NEW.recipe_id, '')
                OR IFNULL(OLD.target_digest, '')
                    <> IFNULL(NEW.target_digest, '')
                OR IFNULL(OLD.target_platform, '')
                    <> IFNULL(NEW.target_platform, '')
                OR IFNULL(OLD.expected_current_image_reference, '')
                    <> IFNULL(NEW.expected_current_image_reference, '')
                OR IFNULL(OLD.expected_current_image_digest, '')
                    <> IFNULL(NEW.expected_current_image_digest, '')
                OR IFNULL(OLD.expected_current_local_image_id, '')
                    <> IFNULL(NEW.expected_current_local_image_id, '')
                OR IFNULL(OLD.expected_current_worker_revision, '')
                    <> IFNULL(NEW.expected_current_worker_revision, '')
                OR IFNULL(OLD.expected_static_fingerprint, '')
                    <> IFNULL(NEW.expected_static_fingerprint, '')
                OR IFNULL(
                        OLD.expected_preserved_configuration_fingerprint,
                        '')
                    <> IFNULL(
                        NEW.expected_preserved_configuration_fingerprint,
                        '')
                OR IFNULL(OLD.expected_routing_fingerprint, '')
                    <> IFNULL(NEW.expected_routing_fingerprint, '')
                OR IFNULL(OLD.expected_desired_generation, -1)
                    <> IFNULL(NEW.expected_desired_generation, -1)
                OR IFNULL(OLD.expected_desired_state_hash, '')
                    <> IFNULL(NEW.expected_desired_state_hash, '')
                OR IFNULL(OLD.exclusion_category, '')
                    <> IFNULL(NEW.exclusion_category, '')
                OR (OLD.wave_number IS NOT NULL
                    AND OLD.wave_number <> NEW.wave_number)
                OR (OLD.is_canary = 1 AND NEW.is_canary <> 1)
                OR (OLD.command_id IS NOT NULL
                    AND OLD.command_id <> NEW.command_id)
                OR (OLD.previous_candidate_id IS NOT NULL
                    AND OLD.previous_candidate_id
                        <> NEW.previous_candidate_id)
                OR (OLD.previous_recipe_id IS NOT NULL
                    AND OLD.previous_recipe_id <> NEW.previous_recipe_id)
                OR (OLD.previous_image_reference IS NOT NULL
                    AND OLD.previous_image_reference
                        <> NEW.previous_image_reference)
                OR (OLD.previous_image_digest IS NOT NULL
                    AND OLD.previous_image_digest
                        <> NEW.previous_image_digest)
                OR (OLD.previous_worker_revision IS NOT NULL
                    AND OLD.previous_worker_revision
                        <> NEW.previous_worker_revision)
              BEGIN
                  SELECT RAISE(
                      ABORT,
                      'image rollout campaign target authority is immutable');
              END;

              CREATE TRIGGER trg_image_rollout_campaign_targets_transitions
              BEFORE UPDATE OF status ON image_rollout_campaign_targets
              FOR EACH ROW
              WHEN NOT (
                  OLD.status = NEW.status
                  OR (OLD.status = 'eligible'
                      AND NEW.status IN (
                          'queued',
                          'blocked',
                          'cancelled'))
                  OR (OLD.status = 'queued'
                      AND NEW.status IN (
                          'claimed',
                          'applying',
                          'rolling',
                          'complete',
                          'failed',
                          'blocked',
                          'indeterminate',
                          'cancelled'))
                  OR (OLD.status = 'claimed'
                      AND NEW.status IN (
                          'applying',
                          'rolling',
                          'complete',
                          'failed',
                          'blocked',
                          'indeterminate'))
                  OR (OLD.status = 'applying'
                      AND NEW.status IN (
                          'rolling',
                          'complete',
                          'failed',
                          'blocked',
                          'indeterminate'))
                  OR (OLD.status = 'rolling'
                      AND NEW.status = 'complete'))
              BEGIN
                  SELECT RAISE(
                      ABORT,
                      'image rollout campaign target transition is not allowed');
              END;

              CREATE TRIGGER
                  trg_image_rollout_campaign_targets_cancel_before_command
              BEFORE UPDATE OF status ON image_rollout_campaign_targets
              FOR EACH ROW
              WHEN NEW.status = 'cancelled'
                AND OLD.command_id IS NOT NULL
              BEGIN
                  SELECT RAISE(
                      ABORT,
                      'campaign targets with commands cannot be cancelled');
              END;

              CREATE TRIGGER trg_image_rollout_campaign_waves_immutable
              BEFORE UPDATE ON image_rollout_campaign_waves
              FOR EACH ROW
              WHEN OLD.campaign_id <> NEW.campaign_id
                OR OLD.wave_number <> NEW.wave_number
                OR OLD.target_count <> NEW.target_count
                OR (OLD.approved_by_github_user_id IS NOT NULL
                    AND (OLD.approved_by_github_user_id
                            <> NEW.approved_by_github_user_id
                        OR OLD.approved_at <> NEW.approved_at))
              BEGIN
                  SELECT RAISE(
                      ABORT,
                      'image rollout campaign wave authority is immutable');
              END;

              CREATE TRIGGER trg_image_rollout_campaign_waves_transitions
              BEFORE UPDATE OF status ON image_rollout_campaign_waves
              FOR EACH ROW
              WHEN NOT (
                  OLD.status = NEW.status
                  OR (OLD.status = 'pending'
                      AND NEW.status IN ('approved', 'cancelled'))
                  OR (OLD.status = 'approved'
                      AND NEW.status IN (
                          'running',
                          'complete',
                          'blocked',
                          'cancelled'))
                  OR (OLD.status = 'running'
                      AND NEW.status IN (
                          'complete',
                          'blocked',
                          'cancelled')))
              BEGIN
                  SELECT RAISE(
                      ABORT,
                      'image rollout campaign wave transition is not allowed');
              END;

              CREATE TRIGGER trg_image_rollout_campaign_idempotency_immutable
              BEFORE UPDATE ON image_rollout_campaign_idempotency
              FOR EACH ROW
              BEGIN
                  SELECT RAISE(
                      ABORT,
                      'image rollout campaign idempotency is immutable');
              END;
              """),
    ];
}
