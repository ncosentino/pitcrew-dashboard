using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Microsoft.Data.Sqlite;

using PitCrew.Dashboard.Features.Fleet.Abstractions;

namespace PitCrew.Dashboard.Adapters.Sqlite;

internal sealed partial class SqliteAlertIncidentStore
{
  public Task<AlertIncidentPage> GetPageAsync(
      string tenantId,
      AlertIncidentFilter filter,
      int limit,
      AlertIncidentCursor? cursor,
      DateTimeOffset generatedAt,
      CancellationToken cancellationToken) =>
      GetProjectedPageAsync(
          tenantId,
          filter,
          limit,
          cursor,
          generatedAt,
          cancellationToken);

  public async Task<AlertIncident?> GetByIdAsync(
      string tenantId,
      Guid incidentId,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        $"""
        {ProjectionSelect}
        WHERE episode.tenant_id = $tenantId
          AND episode.episode_id = $incidentId;
        """;
    command.Parameters.AddWithValue("$tenantId", tenantId);
    command.Parameters.AddWithValue(
        "$incidentId",
        incidentId.ToString("D"));
    await using var reader = await command.ExecuteReaderAsync(
        cancellationToken);
    return await reader.ReadAsync(cancellationToken)
        ? ReadProjectedIncident(new SqliteRowReader(reader))
        : null;
  }

  public async Task<string> GetHistoryStateAsync(
      string tenantId,
      Guid incidentId,
      DateTimeOffset evaluatedAt,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        SELECT expiry_category
        FROM actionable_incident_expiry_locators
        WHERE tenant_id = $tenantId
          AND external_id = $incidentId
          AND expires_at > $evaluatedAt;
        """;
    command.Parameters.AddWithValue("$tenantId", tenantId);
    command.Parameters.AddWithValue(
        "$incidentId",
        incidentId.ToString("D"));
    command.Parameters.AddWithValue("$evaluatedAt", Utc(evaluatedAt));
    var state = await command.ExecuteScalarAsync(cancellationToken);
    return state is null
        ? "history-unavailable"
        : "history-expired";
  }

  public async Task<AlertAcknowledgeStatus> AcknowledgeAsync(
      string tenantId,
      Guid incidentId,
      string acknowledgedByGitHubUserId,
      DateTimeOffset acknowledgedAt,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var transaction =
        await _connectionFactory.BeginWriteTransactionAsync(
            connection,
            cancellationToken);
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        UPDATE actionable_incident_episodes
        SET operator_state = 'acknowledged',
            acknowledged_at = $acknowledgedAt,
            acknowledged_by_github_user_id = $acknowledgedBy,
            acknowledged_revision = incident_revision,
            acknowledged_covered_severity = current_severity,
            updated_at = $acknowledgedAt
        WHERE tenant_id = $tenantId
          AND episode_id = $incidentId
          AND status IN (
              'active',
              'waiting-for-evidence',
              'recovering',
              'monitoring-ended')
        RETURNING incident_revision;
        """;
    command.Parameters.AddWithValue("$tenantId", tenantId);
    command.Parameters.AddWithValue(
        "$incidentId",
        incidentId.ToString("D"));
    command.Parameters.AddWithValue(
        "$acknowledgedAt",
        Utc(acknowledgedAt));
    command.Parameters.AddWithValue(
        "$acknowledgedBy",
        acknowledgedByGitHubUserId);
    var revision = await command.ExecuteScalarAsync(cancellationToken);
    if (revision is not null)
    {
      await UpdateLegacyAcknowledgementAsync(
          connection,
          transaction,
          tenantId,
          incidentId,
          acknowledgedByGitHubUserId,
          acknowledgedAt,
          cancellationToken);
      await transaction.CommitAsync(cancellationToken);
      return AlertAcknowledgeStatus.Succeeded;
    }

    var status = await LoadProjectedStatusAsync(
        connection,
        transaction,
        tenantId,
        incidentId,
        cancellationToken);
    await transaction.CommitAsync(cancellationToken);
    return status == "resolved" ||
        status == "legacy-resolution-unverified"
        ? AlertAcknowledgeStatus.Resolved
        : AlertAcknowledgeStatus.NotFound;
  }

  public async Task<AlertUnacknowledgeStatus> UnacknowledgeAsync(
      string tenantId,
      Guid incidentId,
      string unacknowledgedByGitHubUserId,
      DateTimeOffset unacknowledgedAt,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var transaction =
        await _connectionFactory.BeginWriteTransactionAsync(
            connection,
            cancellationToken);
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        UPDATE actionable_incident_episodes
        SET operator_state = 'unowned',
            acknowledged_at = NULL,
            acknowledged_by_github_user_id = NULL,
            acknowledged_revision = NULL,
            acknowledged_covered_severity = NULL,
            updated_at = $changedAt
        WHERE tenant_id = $tenantId
          AND episode_id = $incidentId
          AND operator_state = 'acknowledged'
          AND status IN (
              'active',
              'waiting-for-evidence',
              'recovering',
              'monitoring-ended');
        """;
    command.Parameters.AddWithValue("$tenantId", tenantId);
    command.Parameters.AddWithValue(
        "$incidentId",
        incidentId.ToString("D"));
    command.Parameters.AddWithValue("$changedAt", Utc(unacknowledgedAt));
    var changed = await command.ExecuteNonQueryAsync(cancellationToken);
    if (changed == 1)
    {
      await UpdateLegacyUnacknowledgementAsync(
          connection,
          transaction,
          tenantId,
          incidentId,
          unacknowledgedByGitHubUserId,
          unacknowledgedAt,
          cancellationToken);
      await transaction.CommitAsync(cancellationToken);
      return AlertUnacknowledgeStatus.Succeeded;
    }

    var status = await LoadProjectedStatusAsync(
        connection,
        transaction,
        tenantId,
        incidentId,
        cancellationToken);
    var operatorState = await LoadProjectedOperatorStateAsync(
        connection,
        transaction,
        tenantId,
        incidentId,
        cancellationToken);
    await transaction.CommitAsync(cancellationToken);
    return status switch
    {
      "resolved" or "legacy-resolution-unverified" =>
          AlertUnacknowledgeStatus.Resolved,
      not null when operatorState == "unowned" =>
          AlertUnacknowledgeStatus.AlreadyTriggered,
      _ => AlertUnacknowledgeStatus.NotFound,
    };
  }

  private static async Task UpdateLegacyAcknowledgementAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      string tenantId,
      Guid incidentId,
      string actor,
      DateTimeOffset acknowledgedAt,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        UPDATE alert_incidents
        SET status = 'acknowledged',
            operator_state = 'acknowledged',
            acknowledged_at = $acknowledgedAt,
            acknowledged_by_github_user_id = $actor,
            acknowledged_revision = incident_revision,
            updated_at = $acknowledgedAt
        WHERE tenant_id = $tenantId
          AND incident_id = $incidentId
          AND status = 'triggered'
        RETURNING incident_revision;
        """;
    command.Parameters.AddWithValue("$tenantId", tenantId);
    command.Parameters.AddWithValue(
        "$incidentId",
        incidentId.ToString("D"));
    command.Parameters.AddWithValue("$acknowledgedAt", Utc(acknowledgedAt));
    command.Parameters.AddWithValue("$actor", actor);
    var revision = await command.ExecuteScalarAsync(cancellationToken);
    if (revision is not null)
    {
      await InsertAcknowledgementEventAsync(
          connection,
          transaction,
          tenantId,
          incidentId,
          "acknowledged",
          actor,
          Convert.ToInt32(revision, CultureInfo.InvariantCulture),
          acknowledgedAt,
          cancellationToken);
    }
  }

  private static async Task UpdateLegacyUnacknowledgementAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      string tenantId,
      Guid incidentId,
      string actor,
      DateTimeOffset unacknowledgedAt,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        UPDATE alert_incidents
        SET status = 'triggered',
            operator_state = 'unowned',
            acknowledged_at = NULL,
            acknowledged_by_github_user_id = NULL,
            acknowledged_revision = NULL,
            updated_at = $unacknowledgedAt
        WHERE tenant_id = $tenantId
          AND incident_id = $incidentId
          AND status = 'acknowledged'
        RETURNING incident_revision;
        """;
    command.Parameters.AddWithValue("$tenantId", tenantId);
    command.Parameters.AddWithValue(
        "$incidentId",
        incidentId.ToString("D"));
    command.Parameters.AddWithValue(
        "$unacknowledgedAt",
        Utc(unacknowledgedAt));
    var revision = await command.ExecuteScalarAsync(cancellationToken);
    if (revision is not null)
    {
      await InsertAcknowledgementEventAsync(
          connection,
          transaction,
          tenantId,
          incidentId,
          "unacknowledged",
          actor,
          Convert.ToInt32(revision, CultureInfo.InvariantCulture),
          unacknowledgedAt,
          cancellationToken);
    }
  }

  public async Task<bool> SetSuppressionAsync(
      string tenantId,
      Guid incidentId,
      string? reason,
      DateTimeOffset? expiresAt,
      DateTimeOffset changedAt,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        UPDATE actionable_incident_episodes
        SET suppression_reason = $reason,
            suppression_expires_at = $expiresAt,
            suppression_covered_severity = CASE
                WHEN $reason IS NULL THEN NULL
                ELSE current_severity
            END,
            suppression_condition_count = CASE
                WHEN $reason IS NULL THEN NULL
                ELSE condition_count
            END,
            updated_at = $changedAt
        WHERE tenant_id = $tenantId
          AND episode_id = $incidentId
          AND status IN (
              'active',
              'waiting-for-evidence',
              'recovering',
              'monitoring-ended');
        """;
    command.Parameters.AddWithValue("$tenantId", tenantId);
    command.Parameters.AddWithValue(
        "$incidentId",
        incidentId.ToString("D"));
    command.Parameters.AddWithValue(
        "$reason",
        reason is null ? DBNull.Value : reason);
    command.Parameters.AddWithValue(
        "$expiresAt",
        expiresAt is null ? DBNull.Value : Utc(expiresAt.Value));
    command.Parameters.AddWithValue("$changedAt", Utc(changedAt));
    return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
  }

  private async Task<AlertIncidentPage> GetProjectedPageAsync(
      string tenantId,
      AlertIncidentFilter filter,
      int limit,
      AlertIncidentCursor? cursor,
      DateTimeOffset generatedAt,
      CancellationToken cancellationToken)
  {
    var statusClause = filter switch
    {
      AlertIncidentFilter.Active =>
          "projected.projected_status IN ('active', 'waiting-for-evidence', 'recovering', 'monitoring-ended')",
      AlertIncidentFilter.Resolved =>
          "projected.projected_status IN ('resolved', 'legacy-resolution-unverified') AND projected.history_state = 'retained'",
      AlertIncidentFilter.All => "1 = 1",
      _ => throw new ArgumentOutOfRangeException(nameof(filter)),
    };
    const string attentionRank =
        """
        CASE
            WHEN projected.projected_status IN (
                'resolved',
                'legacy-resolution-unverified') THEN 7
            WHEN projected.suppression_reason IS NOT NULL
              AND (
                  projected.suppression_expires_at IS NULL
                  OR projected.suppression_expires_at > $generatedAt) THEN 6
            WHEN projected.current_severity = 'critical'
              AND projected.operator_state = 'unowned' THEN 0
            WHEN projected.current_severity = 'critical' THEN 1
            WHEN projected.current_severity = 'warning'
              AND projected.operator_state = 'unowned' THEN 2
            WHEN projected.current_severity IS NULL
              AND projected.operator_state = 'unowned' THEN 3
            WHEN projected.current_severity = 'warning' THEN 4
            ELSE 5
        END
        """;
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var transaction = (SqliteTransaction)
        await connection.BeginTransactionAsync(cancellationToken);
    var snapshotVersion = await LoadProjectionVersionAsync(
        connection,
        transaction,
        tenantId,
        cancellationToken);
    if (cursor is not null &&
        (cursor.SnapshotVersion != snapshotVersion ||
         cursor.SnapshotAt is null))
    {
      await transaction.CommitAsync(cancellationToken);
      return new AlertIncidentPage(generatedAt, [], false)
      {
        CursorInvalidated = true,
      };
    }
    var totals = await LoadProjectedTotalsAsync(
        connection,
        transaction,
        tenantId,
        filter,
        cancellationToken);
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        $"""
        WITH projected AS (
            {ProjectionSelect}
        )
        SELECT
            projected.*,
            {attentionRank} AS attention_rank,
            COALESCE(
                projected.resolved_at,
                projected.triggered_at) AS sort_at
        FROM projected
        WHERE projected.tenant_id = $tenantId
          AND {statusClause}
          AND (
              $cursorRank IS NULL
              OR {attentionRank} > $cursorRank
              OR (
                  {attentionRank} = $cursorRank
                  AND (
                      COALESCE(
                          projected.resolved_at,
                          projected.triggered_at)
                          < $cursorSortAt
                      OR (
                          COALESCE(
                              projected.resolved_at,
                              projected.triggered_at) = $cursorSortAt
                          AND projected.episode_id < $cursorIncidentId))))
        ORDER BY
            attention_rank,
            COALESCE(
                projected.resolved_at,
                projected.triggered_at) DESC,
            projected.episode_id DESC
        LIMIT $fetchLimit;
        """;
    command.Parameters.AddWithValue("$tenantId", tenantId);
    var snapshotAt = cursor?.SnapshotAt ?? generatedAt;
    command.Parameters.AddWithValue("$generatedAt", Utc(snapshotAt));
    command.Parameters.AddWithValue("$fetchLimit", checked(limit + 1));
    command.Parameters.AddWithValue(
        "$cursorRank",
        cursor is null ? DBNull.Value : cursor.AttentionRank);
    command.Parameters.AddWithValue(
        "$cursorSortAt",
        cursor is null ? DBNull.Value : Utc(cursor.SortAt));
    command.Parameters.AddWithValue(
        "$cursorIncidentId",
        cursor is null
            ? DBNull.Value
            : cursor.IncidentId.ToString("D"));
    var incidents = new List<AlertIncident>();
    var ranks = new List<int>();
    var sortTimes = new List<DateTimeOffset>();
    await using var reader = await command.ExecuteReaderAsync(
        cancellationToken);
    SqliteRowReader? row = null;
    while (await reader.ReadAsync(cancellationToken))
    {
      row ??= new SqliteRowReader(reader);
      incidents.Add(ReadProjectedIncident(row));
      ranks.Add(row.Int32("attention_rank"));
      sortTimes.Add(row.Time("sort_at"));
    }

    var truncated = incidents.Count > limit;
    if (truncated)
    {
      incidents.RemoveAt(incidents.Count - 1);
      ranks.RemoveAt(ranks.Count - 1);
      sortTimes.RemoveAt(sortTimes.Count - 1);
    }
    await transaction.CommitAsync(cancellationToken);
    return new AlertIncidentPage(generatedAt, incidents, truncated)
    {
      TotalCount = totals.Total,
      CriticalCount = totals.Critical,
      WarningCount = totals.Warning,
      NextCursor = truncated
          ? new AlertIncidentCursor(
              ranks[^1],
              sortTimes[^1],
              incidents[^1].IncidentId)
          {
            SnapshotVersion = snapshotVersion,
            SnapshotAt = snapshotAt,
          }.ToString()
          : null,
    };
  }

  private static async Task<long> LoadProjectionVersionAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      string tenantId,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        SELECT projection_version
        FROM actionable_incident_projection_versions
        WHERE tenant_id = $tenantId;
        """;
    command.Parameters.AddWithValue("$tenantId", tenantId);
    var value = await command.ExecuteScalarAsync(cancellationToken);
    return value is null
        ? 0
        : Convert.ToInt64(value, CultureInfo.InvariantCulture);
  }

  private static async Task SynchronizeActionableProjectionAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      DateTimeOffset evaluatedAt,
      CancellationToken cancellationToken)
  {
    var conditions = await LoadProjectionConditionsAsync(
        connection,
        transaction,
        cancellationToken);
    var reportingLossNodes = conditions
        .Where(condition =>
            condition.Status != "resolved" &&
            condition.Kind == "connector-offline")
        .Select(condition => (condition.TenantId, condition.NodeId))
        .ToHashSet();
    var memberships = await LoadMembershipsAsync(
        connection,
        transaction,
        cancellationToken);
    var upsertedSeries = new HashSet<Guid>();
    var openEpisodes = new Dictionary<Guid, string>();

    foreach (var condition in conditions)
    {
      var grouping = ResolveGrouping(condition, reportingLossNodes);
      var seriesId = DeterministicId($"series|{grouping.CanonicalKey}");
      if (upsertedSeries.Add(seriesId))
      {
        await UpsertSeriesAsync(
            connection,
            transaction,
            seriesId,
            condition,
            grouping,
            evaluatedAt,
            cancellationToken);
      }

      memberships.TryGetValue(condition.IncidentId, out var membership);
      var keepExisting =
          membership is not null &&
          (condition.Status == "resolved" ||
           membership.SeriesId == seriesId);
      string episodeId;
      if (keepExisting)
      {
        episodeId = membership!.EpisodeId;
        if (condition.Status != "resolved" &&
            membership.Status is not
                ("resolved" or "legacy-resolution-unverified"))
        {
          openEpisodes.TryAdd(seriesId, episodeId);
        }
      }
      else if (!openEpisodes.TryGetValue(seriesId, out episodeId!))
      {
        episodeId = await GetOrCreateEpisodeAsync(
              connection,
              transaction,
              seriesId,
              condition,
              grouping,
              evaluatedAt,
              cancellationToken);
        openEpisodes.Add(seriesId, episodeId);
      }

      await UpsertMembershipAsync(
          connection,
          transaction,
          episodeId,
          condition,
          grouping,
          cancellationToken);
    }

    await DeleteEmptyOpenEpisodesAsync(
        connection,
        transaction,
        cancellationToken);
    await RecomputeEpisodesAsync(
        connection,
        transaction,
        evaluatedAt,
        cancellationToken);
  }

  private static async Task CompactActionableHistoryAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      DateTimeOffset evaluatedAt,
      DateTimeOffset resolvedBefore,
      int maximumResolvedPerTenant,
      TimeSpan expiryLocatorRetention,
      int maximumExpiryLocatorsPerTenant,
      CancellationToken cancellationToken)
  {
    await using (var mark = connection.CreateCommand())
    {
      mark.Transaction = transaction;
      mark.CommandText =
          """
          UPDATE actionable_incident_episodes
          SET history_state = 'history-pruned',
              title = 'Incident history pruned',
              summary = 'Detailed incident history is no longer retained.',
              reason = 'history-pruned',
              evidence = NULL,
              updated_at = $evaluatedAt
          WHERE status IN ('resolved', 'legacy-resolution-unverified')
            AND COALESCE(resolved_at, last_observed_at) < $resolvedBefore;
          """;
      mark.Parameters.AddWithValue("$evaluatedAt", Utc(evaluatedAt));
      mark.Parameters.AddWithValue("$resolvedBefore", Utc(resolvedBefore));
      await mark.ExecuteNonQueryAsync(cancellationToken);
    }

    var locatorExpiresAt = evaluatedAt + expiryLocatorRetention;
    await using (var locate = connection.CreateCommand())
    {
      locate.Transaction = transaction;
      locate.CommandText =
          """
          WITH ranked AS (
              SELECT
                  episode_id,
                  tenant_id,
                  series_id,
                  episode_ordinal,
                  COALESCE(
                      resolved_at,
                      last_observed_at) AS terminal_at,
                  ROW_NUMBER() OVER (
                      PARTITION BY tenant_id
                      ORDER BY
                          COALESCE(resolved_at, last_observed_at) DESC,
                          episode_id DESC) AS rank_index
              FROM actionable_incident_episodes
              WHERE status IN (
                  'resolved',
                  'legacy-resolution-unverified'))
          INSERT INTO actionable_incident_expiry_locators (
              external_id,
              tenant_id,
              series_id,
              episode_ordinal,
              expiry_category,
              terminal_at,
              compacted_at,
              expires_at)
          SELECT
              episode_id,
              tenant_id,
              series_id,
              episode_ordinal,
              CASE
                  WHEN episode_ordinal = 0
                      THEN 'legacy-resolution-history-expired'
                  ELSE 'history-expired'
              END,
              terminal_at,
              $evaluatedAt,
              $locatorExpiresAt
          FROM ranked
          WHERE rank_index > $maximum
          ON CONFLICT (external_id) DO UPDATE SET
              terminal_at = excluded.terminal_at,
              compacted_at = excluded.compacted_at,
              expires_at = excluded.expires_at;
          """;
      locate.Parameters.AddWithValue(
          "$maximum",
          maximumResolvedPerTenant);
      locate.Parameters.AddWithValue(
          "$locatorExpiresAt",
          Utc(locatorExpiresAt));
      locate.Parameters.AddWithValue("$evaluatedAt", Utc(evaluatedAt));
      await locate.ExecuteNonQueryAsync(cancellationToken);
    }

    await using (var replaceLinks = connection.CreateCommand())
    {
      replaceLinks.Transaction = transaction;
      replaceLinks.CommandText =
          """
          WITH ranked AS (
              SELECT
                  episode_id,
                  episode_ordinal,
                  ROW_NUMBER() OVER (
                      PARTITION BY tenant_id
                      ORDER BY
                          COALESCE(resolved_at, last_observed_at) DESC,
                          episode_id DESC) AS rank_index
              FROM actionable_incident_episodes
              WHERE status IN (
                  'resolved',
                  'legacy-resolution-unverified'))
          UPDATE actionable_incident_episodes
          SET previous_history_state = CASE
                  WHEN (
                      SELECT episode_ordinal
                      FROM ranked
                      WHERE ranked.episode_id =
                          actionable_incident_episodes.previous_episode_id
                  ) = 0
                      THEN 'legacy-resolution-history-expired'
                  ELSE 'history-expired'
              END,
              previous_episode_id = NULL,
              updated_at = $evaluatedAt
          WHERE previous_episode_id IN (
              SELECT episode_id
              FROM ranked
              WHERE rank_index > $maximum);
          """;
      replaceLinks.Parameters.AddWithValue(
          "$evaluatedAt",
          Utc(evaluatedAt));
      replaceLinks.Parameters.AddWithValue(
          "$maximum",
          maximumResolvedPerTenant);
      await replaceLinks.ExecuteNonQueryAsync(cancellationToken);
    }

    await using (var delete = connection.CreateCommand())
    {
      delete.Transaction = transaction;
      delete.CommandText =
          """
          DELETE FROM actionable_incident_episodes
          WHERE episode_id IN (
              SELECT episode_id
              FROM (
                  SELECT
                      episode_id,
                      ROW_NUMBER() OVER (
                          PARTITION BY tenant_id
                          ORDER BY
                              COALESCE(resolved_at, last_observed_at) DESC,
                              episode_id DESC) AS rank_index
                  FROM actionable_incident_episodes
                  WHERE status IN (
                      'resolved',
                      'legacy-resolution-unverified'))
              WHERE rank_index > $maximum);
          """;
      delete.Parameters.AddWithValue(
          "$maximum",
          maximumResolvedPerTenant);
      await delete.ExecuteNonQueryAsync(cancellationToken);
    }

    await using var expireLocators = connection.CreateCommand();
    expireLocators.Transaction = transaction;
    expireLocators.CommandText =
        """
        DELETE FROM actionable_incident_expiry_locators
        WHERE expires_at <= $evaluatedAt;
        """;
    expireLocators.Parameters.AddWithValue(
        "$evaluatedAt",
        Utc(evaluatedAt));
    await expireLocators.ExecuteNonQueryAsync(cancellationToken);

    await using var boundLocators = connection.CreateCommand();
    boundLocators.Transaction = transaction;
    boundLocators.CommandText =
        """
        DELETE FROM actionable_incident_expiry_locators
        WHERE external_id IN (
            SELECT external_id
            FROM (
                SELECT
                    external_id,
                    ROW_NUMBER() OVER (
                        PARTITION BY tenant_id
                        ORDER BY
                            terminal_at DESC,
                            compacted_at DESC,
                            external_id DESC) AS rank_index
                FROM actionable_incident_expiry_locators)
            WHERE rank_index > $maximum);
        """;
    boundLocators.Parameters.AddWithValue(
        "$maximum",
        maximumExpiryLocatorsPerTenant);
    await boundLocators.ExecuteNonQueryAsync(cancellationToken);
  }

  private static async Task<IReadOnlyList<ProjectionCondition>>
      LoadProjectionConditionsAsync(
          SqliteConnection connection,
          SqliteTransaction transaction,
          CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        SELECT
            incident_id,
            alert_key,
            tenant_id,
            node_id,
            profile_id,
            kind,
            status,
            title,
            summary,
            reason,
            evidence,
            link,
            first_observed_at,
            triggered_at,
            last_observed_at,
            resolved_at,
            source_observed_at,
            dashboard_received_at,
            evaluated_at,
            resolution_source_observed_at,
            condition_state,
            operator_state,
            current_severity,
            last_confirmed_severity,
            peak_severity,
            incident_revision,
            acknowledged_at,
            acknowledged_by_github_user_id,
            rule_family,
            rule_interpretation_version,
            grouping_policy_version,
            incident_family,
            canonical_target_scope,
            investigation_class,
            evidence_dependency,
            grouping_reasons
            ,
            recovery_started_at
        FROM alert_incidents
        WHERE status IN ('triggered', 'acknowledged', 'resolved')
        ORDER BY tenant_id, alert_key, incident_id;
        """;
    var result = new List<ProjectionCondition>();
    await using var reader = await command.ExecuteReaderAsync(
        cancellationToken);
    var row = new SqliteRowReader(reader);
    while (await reader.ReadAsync(cancellationToken))
    {
      result.Add(new ProjectionCondition(
          row.String("incident_id"),
          row.String("alert_key"),
          row.String("tenant_id"),
          row.String("node_id"),
          row.OptionalString("profile_id"),
          row.String("kind"),
          row.String("status"),
          row.String("title"),
          row.String("summary"),
          row.String("reason"),
          row.OptionalString("evidence"),
          row.String("link"),
          row.Time("first_observed_at"),
          row.OptionalTime("triggered_at") ?? row.Time("first_observed_at"),
          row.Time("last_observed_at"),
          row.OptionalTime("resolved_at"),
          row.OptionalTime("source_observed_at"),
          row.OptionalTime("dashboard_received_at"),
          row.OptionalTime("evaluated_at"),
          row.OptionalTime("resolution_source_observed_at"),
          row.OptionalTime("recovery_started_at") is null
              ? row.String("condition_state")
              : "recovering",
          row.String("operator_state"),
          row.OptionalString("current_severity"),
          row.String("last_confirmed_severity"),
          row.String("peak_severity"),
          row.Int32("incident_revision"),
          row.OptionalTime("acknowledged_at"),
          row.OptionalString("acknowledged_by_github_user_id"),
          row.String("rule_family"),
          row.Int32("rule_interpretation_version"),
          row.Int32("grouping_policy_version"),
          row.String("incident_family"),
          row.String("canonical_target_scope"),
          row.String("investigation_class"),
          row.String("evidence_dependency"),
          row.String("grouping_reasons"),
          row.OptionalTime("recovery_started_at")));
    }
    return result;
  }

  private static ProjectionGrouping ResolveGrouping(
      ProjectionCondition condition,
      IReadOnlySet<(string TenantId, string NodeId)> reportingLossNodes)
  {
    if (condition.Kind == "connector-offline" ||
        condition.ConditionState == "waiting-for-evidence" &&
        condition.ProfileId is not null &&
        reportingLossNodes.Contains((condition.TenantId, condition.NodeId)))
    {
      var targetScope = $"node:{condition.NodeId}";
      return CreateGrouping(
          condition.TenantId,
          1,
          "reporting-visibility",
          targetScope,
          "restore-reporting",
          $"connector:{condition.NodeId}",
          "same-reporting-boundary|same-authoritative-target|shared-unavailable-evidence");
    }

    var legacyDefaults = condition.RuleFamily == "legacy-v1";
    return CreateGrouping(
        condition.TenantId,
        condition.GroupingPolicyVersion,
        legacyDefaults ? condition.Kind : condition.IncidentFamily,
        string.IsNullOrEmpty(condition.CanonicalTargetScope)
            ? $"condition:{condition.AlertKey}"
            : condition.CanonicalTargetScope,
        legacyDefaults
            ? $"condition:{condition.Kind}"
            : condition.InvestigationClass,
        legacyDefaults
            ? $"condition:{condition.AlertKey}"
            : condition.EvidenceDependency,
        string.IsNullOrEmpty(condition.GroupingReasons) || legacyDefaults
            ? "same-stable-condition"
            : condition.GroupingReasons);
  }

  private static ProjectionGrouping CreateGrouping(
      string tenantId,
      int groupingPolicyVersion,
      string incidentFamily,
      string canonicalTargetScope,
      string investigationClass,
      string evidenceDependency,
      string groupingReasons)
  {
    var canonicalKey = Canonicalize(
        tenantId,
        groupingPolicyVersion.ToString(CultureInfo.InvariantCulture),
        incidentFamily,
        canonicalTargetScope,
        investigationClass,
        evidenceDependency);
    return new ProjectionGrouping(
        canonicalKey,
        groupingPolicyVersion,
        incidentFamily,
        canonicalTargetScope,
        investigationClass,
        evidenceDependency,
        string.Join(
            "|",
            groupingReasons.Split(
                    '|',
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)));
  }

  private static async Task UpsertSeriesAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      Guid seriesId,
      ProjectionCondition condition,
      ProjectionGrouping grouping,
      DateTimeOffset evaluatedAt,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        INSERT INTO actionable_incident_series (
            series_id,
            tenant_id,
            canonical_key,
            grouping_policy_version,
            incident_family,
            canonical_target_scope,
            investigation_class,
            evidence_dependency,
            created_at)
        VALUES (
            $seriesId,
            $tenantId,
            $canonicalKey,
            $groupingPolicyVersion,
            $incidentFamily,
            $canonicalTargetScope,
            $investigationClass,
            $evidenceDependency,
            $createdAt)
        ON CONFLICT (tenant_id, canonical_key) DO NOTHING;
        """;
    command.Parameters.AddWithValue("$seriesId", seriesId.ToString("D"));
    command.Parameters.AddWithValue("$tenantId", condition.TenantId);
    command.Parameters.AddWithValue("$canonicalKey", grouping.CanonicalKey);
    command.Parameters.AddWithValue(
        "$groupingPolicyVersion",
        grouping.GroupingPolicyVersion);
    command.Parameters.AddWithValue(
        "$incidentFamily",
        grouping.IncidentFamily);
    command.Parameters.AddWithValue(
        "$canonicalTargetScope",
        grouping.CanonicalTargetScope);
    command.Parameters.AddWithValue(
        "$investigationClass",
        grouping.InvestigationClass);
    command.Parameters.AddWithValue(
        "$evidenceDependency",
        grouping.EvidenceDependency);
    command.Parameters.AddWithValue("$createdAt", Utc(evaluatedAt));
    await command.ExecuteNonQueryAsync(cancellationToken);
  }

  private static async Task<Dictionary<string, MembershipPlacement>>
      LoadMembershipsAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        SELECT
            membership.condition_incident_id,
            membership.episode_id,
            episode.series_id,
            episode.status
        FROM actionable_incident_memberships AS membership
        JOIN actionable_incident_episodes AS episode
          ON episode.episode_id = membership.episode_id;
        """;
    var result = new Dictionary<string, MembershipPlacement>(
        StringComparer.Ordinal);
    await using var reader = await command.ExecuteReaderAsync(
        cancellationToken);
    while (await reader.ReadAsync(cancellationToken))
    {
      result.Add(
          reader.GetString(0),
          new MembershipPlacement(
              reader.GetString(1),
              Guid.Parse(
                  reader.GetString(2),
                  CultureInfo.InvariantCulture),
              reader.GetString(3)));
    }
    return result;
  }

  private static async Task<string> GetOrCreateEpisodeAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      Guid seriesId,
      ProjectionCondition condition,
      ProjectionGrouping grouping,
      DateTimeOffset evaluatedAt,
      CancellationToken cancellationToken)
  {
    var open = await LoadOpenEpisodeIdAsync(
        connection,
        transaction,
        seriesId,
        cancellationToken);
    if (open is not null)
    {
      return open;
    }

    var previous = await LoadLatestEpisodeAsync(
        connection,
        transaction,
        seriesId,
        cancellationToken);
    var legacyUnverified =
        condition.Status == "resolved" &&
        condition.ResolutionSourceObservedAt is null;
    var historyBoundary =
        previous is null && !legacyUnverified
            ? await LoadLatestExpiryBoundaryAsync(
            connection,
            transaction,
            condition.TenantId,
            seriesId,
            evaluatedAt,
            cancellationToken)
            : null;
    var ordinal = legacyUnverified
        ? 0
        : await AllocateNextEpisodeOrdinalAsync(
            connection,
            transaction,
            seriesId,
            cancellationToken);
    if (historyBoundary is not null &&
        ordinal <= historyBoundary.EpisodeOrdinal)
    {
      throw new InvalidOperationException(
          $"Actionable incident series '{seriesId:D}' ordinal state is inconsistent with retained history.");
    }
    var transition = previous switch
    {
      { Status: "legacy-resolution-unverified" } =>
          "reopened-after-unverified-legacy-resolution",
      { Status: "resolved" } => "recurrence",
      null when historyBoundary?.Category ==
          "legacy-resolution-history-expired" =>
          "reopened-after-unverified-legacy-resolution",
      null when historyBoundary is not null => "recurrence",
      _ => null,
    };
    var episodeId = grouping.CanonicalTargetScope.StartsWith(
        "condition:",
        StringComparison.Ordinal)
        ? Guid.Parse(
            condition.IncidentId,
            CultureInfo.InvariantCulture)
        : DeterministicId(
            $"episode|{seriesId:D}|{ordinal.ToString(CultureInfo.InvariantCulture)}");
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        INSERT INTO actionable_incident_episodes (
            episode_id,
            series_id,
            tenant_id,
            episode_ordinal,
            status,
            node_id,
            profile_id,
            kind,
            title,
            summary,
            reason,
            evidence,
            link,
            first_observed_at,
            triggered_at,
            last_observed_at,
            resolved_at,
            current_severity,
            last_confirmed_severity,
            peak_severity,
            operator_state,
            acknowledged_at,
            acknowledged_by_github_user_id,
            acknowledged_revision,
            acknowledged_covered_severity,
            incident_revision,
            previous_episode_id,
            previous_history_state,
            transition,
            grouping_reasons,
            condition_count,
            history_state,
            resolution_provenance,
            created_at,
            updated_at)
        VALUES (
            $episodeId,
            $seriesId,
            $tenantId,
            $episodeOrdinal,
            $status,
            $nodeId,
            $profileId,
            $kind,
            $title,
            $summary,
            $reason,
            $evidence,
            $link,
            $firstObservedAt,
            $triggeredAt,
            $lastObservedAt,
            $resolvedAt,
            $currentSeverity,
            $lastConfirmedSeverity,
            $peakSeverity,
            $operatorState,
            $acknowledgedAt,
            $acknowledgedBy,
            $acknowledgedRevision,
            $acknowledgedCoveredSeverity,
            $incidentRevision,
            $previousEpisodeId,
            $previousHistoryState,
            $transition,
            $groupingReasons,
            1,
            $historyState,
            $resolutionProvenance,
            $createdAt,
            $updatedAt)
        ON CONFLICT (series_id, episode_ordinal) DO NOTHING;
        """;
    command.Parameters.AddWithValue("$episodeId", episodeId.ToString("D"));
    command.Parameters.AddWithValue("$seriesId", seriesId.ToString("D"));
    command.Parameters.AddWithValue("$tenantId", condition.TenantId);
    command.Parameters.AddWithValue("$episodeOrdinal", ordinal);
    command.Parameters.AddWithValue(
        "$status",
        legacyUnverified
            ? "legacy-resolution-unverified"
            : ProjectConditionState(condition.ConditionState));
    AddEpisodeConditionParameters(command, condition);
    command.Parameters.AddWithValue(
        "$resolvedAt",
        condition.ResolvedAt is null
            ? DBNull.Value
            : Utc(condition.ResolvedAt.Value));
    command.Parameters.AddWithValue(
        "$currentSeverity",
        condition.CurrentSeverity is null
            ? DBNull.Value
            : condition.CurrentSeverity);
    command.Parameters.AddWithValue(
        "$operatorState",
        legacyUnverified ? "unowned" : condition.OperatorState);
    command.Parameters.AddWithValue(
        "$acknowledgedAt",
        condition.AcknowledgedAt is null
            ? DBNull.Value
            : Utc(condition.AcknowledgedAt.Value));
    command.Parameters.AddWithValue(
        "$acknowledgedBy",
        condition.AcknowledgedBy is null
            ? DBNull.Value
            : condition.AcknowledgedBy);
    command.Parameters.AddWithValue(
        "$acknowledgedRevision",
        condition.OperatorState == "acknowledged"
            ? condition.Revision
            : DBNull.Value);
    command.Parameters.AddWithValue(
        "$acknowledgedCoveredSeverity",
        condition.OperatorState == "acknowledged" &&
        condition.CurrentSeverity is not null
            ? condition.CurrentSeverity
            : DBNull.Value);
    command.Parameters.AddWithValue("$incidentRevision", condition.Revision);
    command.Parameters.AddWithValue(
        "$previousEpisodeId",
        previous is null ? DBNull.Value : previous.EpisodeId);
    command.Parameters.AddWithValue(
        "$previousHistoryState",
        historyBoundary is null
            ? DBNull.Value
            : historyBoundary.Category);
    command.Parameters.AddWithValue(
        "$transition",
        transition is null ? DBNull.Value : transition);
    command.Parameters.AddWithValue(
        "$groupingReasons",
        grouping.GroupingReasons);
    command.Parameters.AddWithValue(
        "$historyState",
        legacyUnverified ? "history-pruned" : "retained");
    command.Parameters.AddWithValue(
        "$resolutionProvenance",
        legacyUnverified
            ? "legacy-resolution-unverified"
            : transition == "reopened-after-unverified-legacy-resolution"
                ? transition
                : "resolution-provenance-unavailable");
    command.Parameters.AddWithValue("$createdAt", Utc(evaluatedAt));
    command.Parameters.AddWithValue("$updatedAt", Utc(evaluatedAt));
    await command.ExecuteNonQueryAsync(cancellationToken);
    return episodeId.ToString("D");
  }

  private static void AddEpisodeConditionParameters(
      SqliteCommand command,
      ProjectionCondition condition)
  {
    command.Parameters.AddWithValue("$nodeId", condition.NodeId);
    command.Parameters.AddWithValue(
        "$profileId",
        condition.ProfileId is null ? DBNull.Value : condition.ProfileId);
    command.Parameters.AddWithValue("$kind", condition.Kind);
    command.Parameters.AddWithValue("$title", condition.Title);
    command.Parameters.AddWithValue("$summary", condition.Summary);
    command.Parameters.AddWithValue("$reason", condition.Reason);
    command.Parameters.AddWithValue(
        "$evidence",
        condition.Evidence is null ? DBNull.Value : condition.Evidence);
    command.Parameters.AddWithValue("$link", condition.Link);
    command.Parameters.AddWithValue(
        "$firstObservedAt",
        Utc(condition.FirstObservedAt));
    command.Parameters.AddWithValue(
        "$triggeredAt",
        Utc(condition.TriggeredAt));
    command.Parameters.AddWithValue(
        "$lastObservedAt",
        Utc(condition.LastObservedAt));
    command.Parameters.AddWithValue(
        "$lastConfirmedSeverity",
        condition.LastConfirmedSeverity);
    command.Parameters.AddWithValue(
        "$peakSeverity",
        condition.PeakSeverity);
  }

  private static async Task UpsertMembershipAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      string episodeId,
      ProjectionCondition condition,
      ProjectionGrouping grouping,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        INSERT INTO actionable_incident_memberships (
            episode_id,
            condition_incident_id,
            alert_key,
            rule_family,
            rule_interpretation_version,
            grouping_policy_version,
            grouping_reasons,
            membership_started_at,
            membership_ended_at)
        VALUES (
            $episodeId,
            $conditionIncidentId,
            $alertKey,
            $ruleFamily,
            $ruleInterpretationVersion,
            $groupingPolicyVersion,
            $groupingReasons,
            $membershipStartedAt,
            $membershipEndedAt)
        ON CONFLICT (condition_incident_id) DO UPDATE SET
            episode_id = excluded.episode_id,
            grouping_policy_version = excluded.grouping_policy_version,
            grouping_reasons = excluded.grouping_reasons,
            membership_ended_at = excluded.membership_ended_at;
        """;
    command.Parameters.AddWithValue("$episodeId", episodeId);
    command.Parameters.AddWithValue(
        "$conditionIncidentId",
        condition.IncidentId);
    command.Parameters.AddWithValue("$alertKey", condition.AlertKey);
    command.Parameters.AddWithValue("$ruleFamily", condition.RuleFamily);
    command.Parameters.AddWithValue(
        "$ruleInterpretationVersion",
        condition.RuleInterpretationVersion);
    command.Parameters.AddWithValue(
        "$groupingPolicyVersion",
        grouping.GroupingPolicyVersion);
    command.Parameters.AddWithValue(
        "$groupingReasons",
        grouping.GroupingReasons);
    command.Parameters.AddWithValue(
        "$membershipStartedAt",
        Utc(condition.TriggeredAt));
    command.Parameters.AddWithValue(
        "$membershipEndedAt",
        condition.ResolvedAt is null
            ? DBNull.Value
            : Utc(condition.ResolvedAt.Value));
    await command.ExecuteNonQueryAsync(cancellationToken);
  }

  private static async Task RecomputeEpisodesAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      DateTimeOffset evaluatedAt,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        WITH representatives AS (
            SELECT
                membership.episode_id,
                condition.node_id,
                condition.profile_id,
                condition.kind,
                condition.title,
                condition.summary,
                condition.reason,
                condition.evidence,
                condition.link,
                ROW_NUMBER() OVER (
                    PARTITION BY membership.episode_id
                    ORDER BY
                        condition.alert_key,
                        condition.incident_id) AS representative_index
            FROM actionable_incident_memberships AS membership
            JOIN alert_incidents AS condition
              ON condition.incident_id = membership.condition_incident_id
        ),
        aggregates AS (
            SELECT
                membership.episode_id,
                COUNT(*) AS condition_count,
                MIN(condition.first_observed_at) AS first_observed_at,
                MIN(condition.triggered_at) AS triggered_at,
                MAX(condition.last_observed_at) AS last_observed_at,
                CASE
                    WHEN SUM(CASE
                        WHEN condition.status <> 'resolved'
                          AND condition.condition_state = 'confirmed'
                            THEN 1 ELSE 0 END) > 0 THEN 'active'
                    WHEN SUM(CASE
                        WHEN condition.status <> 'resolved'
                          AND condition.condition_state = 'waiting-for-evidence'
                          AND condition.recovery_started_at IS NULL
                            THEN 1 ELSE 0 END) > 0 THEN 'waiting-for-evidence'
                    WHEN SUM(CASE
                        WHEN condition.status <> 'resolved'
                          AND condition.recovery_started_at IS NOT NULL
                            THEN 1 ELSE 0 END) > 0 THEN 'recovering'
                    WHEN SUM(CASE
                        WHEN condition.status <> 'resolved'
                          AND condition.condition_state = 'monitoring-ended'
                            THEN 1 ELSE 0 END) > 0 THEN 'monitoring-ended'
                    ELSE 'resolved'
                END AS projected_status,
                CASE
                    WHEN SUM(CASE
                        WHEN condition.current_severity = 'critical'
                            THEN 1 ELSE 0 END) > 0 THEN 'critical'
                    WHEN SUM(CASE
                        WHEN condition.current_severity = 'warning'
                            THEN 1 ELSE 0 END) > 0 THEN 'warning'
                    ELSE NULL
                END AS current_severity,
                CASE
                    WHEN SUM(CASE
                        WHEN condition.last_confirmed_severity = 'critical'
                            THEN 1 ELSE 0 END) > 0 THEN 'critical'
                    ELSE 'warning'
                END AS last_confirmed_severity,
                CASE
                    WHEN SUM(CASE
                        WHEN condition.peak_severity = 'critical'
                            THEN 1 ELSE 0 END) > 0 THEN 'critical'
                    ELSE 'warning'
                END AS peak_severity,
                MAX(condition.resolved_at) AS resolved_at
            FROM actionable_incident_memberships AS membership
            JOIN alert_incidents AS condition
              ON condition.incident_id = membership.condition_incident_id
            GROUP BY membership.episode_id
        )
        UPDATE actionable_incident_episodes AS episode
        SET status = aggregate.projected_status,
            node_id = representative.node_id,
            profile_id = representative.profile_id,
            kind = representative.kind,
            title = representative.title,
            summary = representative.summary,
            reason = representative.reason,
            evidence = representative.evidence,
            link = representative.link,
            condition_count = aggregate.condition_count,
            first_observed_at = aggregate.first_observed_at,
            triggered_at = aggregate.triggered_at,
            last_observed_at = aggregate.last_observed_at,
            resolved_at = CASE
                WHEN aggregate.projected_status = 'resolved'
                    THEN aggregate.resolved_at
                ELSE NULL
            END,
            current_severity = aggregate.current_severity,
            last_confirmed_severity = aggregate.last_confirmed_severity,
            peak_severity = aggregate.peak_severity,
            operator_state = CASE
                WHEN (
                    episode.last_confirmed_severity = 'warning'
                    AND aggregate.current_severity = 'critical')
                  OR aggregate.condition_count > episode.condition_count
                  OR (
                    episode.operator_state = 'acknowledged'
                    AND aggregate.current_severity IS NOT NULL
                    AND (
                      episode.acknowledged_covered_severity IS NULL
                      OR (
                        episode.acknowledged_covered_severity = 'warning'
                        AND aggregate.current_severity = 'critical')))
                  OR (
                    episode.suppression_reason IS NOT NULL
                    AND aggregate.current_severity IS NOT NULL
                    AND (
                      episode.suppression_covered_severity IS NULL
                      OR (
                        episode.suppression_covered_severity = 'warning'
                        AND aggregate.current_severity = 'critical')))
                  OR (
                    episode.suppression_expires_at IS NOT NULL
                    AND episode.suppression_expires_at <= $evaluatedAt)
                    THEN 'unowned'
                ELSE episode.operator_state
            END,
            acknowledged_at = CASE
                WHEN (
                    episode.last_confirmed_severity = 'warning'
                    AND aggregate.current_severity = 'critical')
                  OR aggregate.condition_count > episode.condition_count
                  OR (
                    episode.operator_state = 'acknowledged'
                    AND aggregate.current_severity IS NOT NULL
                    AND episode.acknowledged_covered_severity IS NULL)
                  OR (
                    episode.suppression_expires_at IS NOT NULL
                    AND episode.suppression_expires_at <= $evaluatedAt)
                    THEN NULL
                ELSE episode.acknowledged_at
            END,
            acknowledged_by_github_user_id = CASE
                WHEN (
                    episode.last_confirmed_severity = 'warning'
                    AND aggregate.current_severity = 'critical')
                  OR aggregate.condition_count > episode.condition_count
                  OR (
                    episode.operator_state = 'acknowledged'
                    AND aggregate.current_severity IS NOT NULL
                    AND episode.acknowledged_covered_severity IS NULL)
                  OR (
                    episode.suppression_expires_at IS NOT NULL
                    AND episode.suppression_expires_at <= $evaluatedAt)
                    THEN NULL
                ELSE episode.acknowledged_by_github_user_id
            END,
            acknowledged_revision = CASE
                WHEN (
                    episode.last_confirmed_severity = 'warning'
                    AND aggregate.current_severity = 'critical')
                  OR aggregate.condition_count > episode.condition_count
                  OR (
                    episode.operator_state = 'acknowledged'
                    AND aggregate.current_severity IS NOT NULL
                    AND episode.acknowledged_covered_severity IS NULL)
                  OR (
                    episode.suppression_expires_at IS NOT NULL
                    AND episode.suppression_expires_at <= $evaluatedAt)
                    THEN NULL
                ELSE episode.acknowledged_revision
            END,
            acknowledged_covered_severity = CASE
                WHEN (
                    episode.last_confirmed_severity = 'warning'
                    AND aggregate.current_severity = 'critical')
                  OR aggregate.condition_count > episode.condition_count
                  OR (
                    episode.operator_state = 'acknowledged'
                    AND aggregate.current_severity IS NOT NULL
                    AND episode.acknowledged_covered_severity IS NULL)
                  OR (
                    episode.suppression_expires_at IS NOT NULL
                    AND episode.suppression_expires_at <= $evaluatedAt)
                    THEN NULL
                ELSE episode.acknowledged_covered_severity
            END,
            incident_revision = episode.incident_revision + CASE
                WHEN episode.condition_count <> aggregate.condition_count
                  OR (
                    episode.last_confirmed_severity = 'warning'
                    AND aggregate.current_severity = 'critical')
                  OR (
                    episode.operator_state = 'acknowledged'
                    AND aggregate.current_severity IS NOT NULL
                    AND episode.acknowledged_covered_severity IS NULL)
                  OR (
                    episode.suppression_reason IS NOT NULL
                    AND aggregate.current_severity IS NOT NULL
                    AND episode.suppression_covered_severity IS NULL)
                  OR (
                    episode.suppression_expires_at IS NOT NULL
                    AND episode.suppression_expires_at <= $evaluatedAt)
                    THEN 1
                ELSE 0
            END,
            suppression_reason = CASE
                WHEN (
                    episode.suppression_expires_at IS NOT NULL
                    AND episode.suppression_expires_at <= $evaluatedAt)
                  OR (
                    episode.suppression_reason IS NOT NULL
                    AND aggregate.current_severity IS NOT NULL
                    AND (
                      episode.suppression_covered_severity IS NULL
                      OR (
                        episode.suppression_covered_severity = 'warning'
                        AND aggregate.current_severity = 'critical')))
                  OR (
                    episode.suppression_condition_count IS NOT NULL
                    AND aggregate.condition_count
                        > episode.suppression_condition_count)
                    THEN NULL
                ELSE episode.suppression_reason
            END,
            suppression_expires_at = CASE
                WHEN (
                    episode.suppression_expires_at IS NOT NULL
                    AND episode.suppression_expires_at <= $evaluatedAt)
                  OR (
                    episode.suppression_reason IS NOT NULL
                    AND aggregate.current_severity IS NOT NULL
                    AND (
                      episode.suppression_covered_severity IS NULL
                      OR (
                        episode.suppression_covered_severity = 'warning'
                        AND aggregate.current_severity = 'critical')))
                  OR (
                    episode.suppression_condition_count IS NOT NULL
                    AND aggregate.condition_count
                        > episode.suppression_condition_count)
                    THEN NULL
                ELSE episode.suppression_expires_at
            END,
            suppression_covered_severity = CASE
                WHEN (
                    episode.suppression_expires_at IS NOT NULL
                    AND episode.suppression_expires_at <= $evaluatedAt)
                  OR (
                    episode.suppression_reason IS NOT NULL
                    AND aggregate.current_severity IS NOT NULL
                    AND (
                      episode.suppression_covered_severity IS NULL
                      OR (
                        episode.suppression_covered_severity = 'warning'
                        AND aggregate.current_severity = 'critical')))
                  OR (
                    episode.suppression_condition_count IS NOT NULL
                    AND aggregate.condition_count
                        > episode.suppression_condition_count)
                    THEN NULL
                ELSE episode.suppression_covered_severity
            END,
            suppression_condition_count = CASE
                WHEN (
                    episode.suppression_expires_at IS NOT NULL
                    AND episode.suppression_expires_at <= $evaluatedAt)
                  OR (
                    episode.suppression_reason IS NOT NULL
                    AND aggregate.current_severity IS NOT NULL
                    AND (
                      episode.suppression_covered_severity IS NULL
                      OR (
                        episode.suppression_covered_severity = 'warning'
                        AND aggregate.current_severity = 'critical')))
                  OR (
                    episode.suppression_condition_count IS NOT NULL
                    AND aggregate.condition_count
                        > episode.suppression_condition_count)
                    THEN NULL
                ELSE episode.suppression_condition_count
            END,
            resolution_provenance = CASE
                WHEN aggregate.projected_status = 'resolved'
                  AND NOT EXISTS (
                      SELECT 1
                      FROM actionable_incident_memberships AS membership
                      JOIN alert_incidents AS condition
                        ON condition.incident_id =
                            membership.condition_incident_id
                      WHERE membership.episode_id = episode.episode_id
                        AND condition.resolution_source_observed_at IS NULL)
                    THEN 'resolution-proven'
                WHEN aggregate.projected_status = 'resolved'
                    THEN 'resolution-provenance-unavailable'
                WHEN aggregate.projected_status = 'monitoring-ended'
                    THEN 'monitoring-ended'
                ELSE episode.resolution_provenance
            END,
            updated_at = $evaluatedAt
        FROM aggregates AS aggregate
        JOIN representatives AS representative
          ON representative.episode_id = aggregate.episode_id
         AND representative.representative_index = 1
        WHERE episode.episode_id = aggregate.episode_id
          AND episode.status <> 'legacy-resolution-unverified';
        """;
    command.Parameters.AddWithValue("$evaluatedAt", Utc(evaluatedAt));
    await command.ExecuteNonQueryAsync(cancellationToken);
  }

  private static async Task DeleteEmptyOpenEpisodesAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        DELETE FROM actionable_incident_episodes
        WHERE status IN (
            'active',
            'waiting-for-evidence',
            'recovering',
            'monitoring-ended')
          AND NOT EXISTS (
              SELECT 1
              FROM actionable_incident_memberships AS membership
              WHERE membership.episode_id =
                  actionable_incident_episodes.episode_id)
        RETURNING series_id, episode_ordinal;
        """;
    var reclaimed = new List<(Guid SeriesId, int EpisodeOrdinal)>();
    await using (var reader = await command.ExecuteReaderAsync(
        cancellationToken))
    {
      while (await reader.ReadAsync(cancellationToken))
      {
        reclaimed.Add((
            Guid.Parse(reader.GetString(0), CultureInfo.InvariantCulture),
            reader.GetInt32(1)));
      }
    }

    foreach (var episode in reclaimed)
    {
      await using var reclaim = connection.CreateCommand();
      reclaim.Transaction = transaction;
      reclaim.CommandText =
          """
          UPDATE actionable_incident_series
          SET last_episode_ordinal = last_episode_ordinal - 1
          WHERE series_id = $seriesId
            AND last_episode_ordinal = $episodeOrdinal;
          """;
      reclaim.Parameters.AddWithValue(
          "$seriesId",
          episode.SeriesId.ToString("D"));
      reclaim.Parameters.AddWithValue(
          "$episodeOrdinal",
          episode.EpisodeOrdinal);
      if (await reclaim.ExecuteNonQueryAsync(cancellationToken) != 1)
      {
        throw new InvalidOperationException(
            $"Actionable incident series '{episode.SeriesId:D}' could not reclaim unresolved ordinal {episode.EpisodeOrdinal.ToString(CultureInfo.InvariantCulture)}.");
      }
    }
  }

  private static async Task<string?> LoadOpenEpisodeIdAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      Guid seriesId,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        SELECT episode_id
        FROM actionable_incident_episodes
        WHERE series_id = $seriesId
          AND status IN (
              'active',
              'waiting-for-evidence',
              'recovering',
              'monitoring-ended')
        ORDER BY episode_ordinal
        LIMIT 1;
        """;
    command.Parameters.AddWithValue("$seriesId", seriesId.ToString("D"));
    var value = await command.ExecuteScalarAsync(cancellationToken);
    return value is null
        ? null
        : Convert.ToString(value, CultureInfo.InvariantCulture);
  }

  private static async Task<ProjectedEpisode?> LoadLatestEpisodeAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      Guid seriesId,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        SELECT episode_id, episode_ordinal, status
        FROM actionable_incident_episodes
        WHERE series_id = $seriesId
        ORDER BY episode_ordinal DESC
        LIMIT 1;
        """;
    command.Parameters.AddWithValue("$seriesId", seriesId.ToString("D"));
    await using var reader = await command.ExecuteReaderAsync(
        cancellationToken);
    return await reader.ReadAsync(cancellationToken)
        ? new ProjectedEpisode(
            reader.GetString(0),
            reader.GetInt32(1),
            reader.GetString(2))
        : null;
  }

  private static async Task<int> AllocateNextEpisodeOrdinalAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      Guid seriesId,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        UPDATE actionable_incident_series
        SET last_episode_ordinal = last_episode_ordinal + 1
        WHERE series_id = $seriesId
        RETURNING last_episode_ordinal;
        """;
    command.Parameters.AddWithValue("$seriesId", seriesId.ToString("D"));
    var value = await command.ExecuteScalarAsync(cancellationToken);
    return value is null
        ? throw new InvalidOperationException(
            $"Actionable incident series '{seriesId:D}' was not found.")
        : Convert.ToInt32(value, CultureInfo.InvariantCulture);
  }

  private static async Task<ProjectedExpiryBoundary?>
      LoadLatestExpiryBoundaryAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      string tenantId,
      Guid seriesId,
      DateTimeOffset evaluatedAt,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        SELECT episode_ordinal, expiry_category
        FROM actionable_incident_expiry_locators
        WHERE tenant_id = $tenantId
          AND series_id = $seriesId
          AND expires_at > $evaluatedAt
        ORDER BY episode_ordinal DESC
        LIMIT 1;
        """;
    command.Parameters.AddWithValue("$tenantId", tenantId);
    command.Parameters.AddWithValue(
        "$seriesId",
        seriesId.ToString("D"));
    command.Parameters.AddWithValue("$evaluatedAt", Utc(evaluatedAt));
    await using var reader = await command.ExecuteReaderAsync(
        cancellationToken);
    return await reader.ReadAsync(cancellationToken)
        ? new ProjectedExpiryBoundary(
            reader.GetInt32(0),
            reader.GetString(1))
        : null;
  }

  private static async Task<string?> LoadProjectedStatusAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      string tenantId,
      Guid incidentId,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        SELECT status
        FROM actionable_incident_episodes
        WHERE tenant_id = $tenantId
          AND episode_id = $incidentId;
        """;
    command.Parameters.AddWithValue("$tenantId", tenantId);
    command.Parameters.AddWithValue(
        "$incidentId",
        incidentId.ToString("D"));
    var value = await command.ExecuteScalarAsync(cancellationToken);
    return value is null
        ? null
        : Convert.ToString(value, CultureInfo.InvariantCulture);
  }

  private static async Task<string?> LoadProjectedOperatorStateAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      string tenantId,
      Guid incidentId,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        SELECT operator_state
        FROM actionable_incident_episodes
        WHERE tenant_id = $tenantId
          AND episode_id = $incidentId;
        """;
    command.Parameters.AddWithValue("$tenantId", tenantId);
    command.Parameters.AddWithValue(
        "$incidentId",
        incidentId.ToString("D"));
    var value = await command.ExecuteScalarAsync(cancellationToken);
    return value is null
        ? null
        : Convert.ToString(value, CultureInfo.InvariantCulture);
  }

  private static async Task<IncidentTotals> LoadProjectedTotalsAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      string tenantId,
      AlertIncidentFilter filter,
      CancellationToken cancellationToken)
  {
    var statusClause = filter switch
    {
      AlertIncidentFilter.Active =>
          "episode.status IN ('active', 'waiting-for-evidence', 'recovering', 'monitoring-ended')",
      AlertIncidentFilter.Resolved =>
          "episode.status IN ('resolved', 'legacy-resolution-unverified') AND episode.history_state = 'retained'",
      AlertIncidentFilter.All => "1 = 1",
      _ => throw new ArgumentOutOfRangeException(nameof(filter)),
    };
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        $"""
        SELECT
            COUNT(*),
            COALESCE(SUM(CASE
                WHEN episode.current_severity = 'critical' THEN 1 ELSE 0 END), 0),
            COALESCE(SUM(CASE
                WHEN episode.current_severity = 'warning' THEN 1 ELSE 0 END), 0)
        FROM actionable_incident_episodes AS episode
        WHERE episode.tenant_id = $tenantId
          AND {statusClause};
        """;
    command.Parameters.AddWithValue("$tenantId", tenantId);
    await using var reader = await command.ExecuteReaderAsync(
        cancellationToken);
    await reader.ReadAsync(cancellationToken);
    return new IncidentTotals(
        reader.GetInt32(0),
        reader.GetInt32(1),
        reader.GetInt32(2));
  }

  private static AlertIncident ReadProjectedIncident(
      SqliteRowReader row)
  {
    var status = row.String("projected_status");
    var currentSeverity = row.OptionalString("current_severity");
    return new AlertIncident(
        Guid.Parse(
            row.String("episode_id"),
            CultureInfo.InvariantCulture),
        Guid.Parse(
            row.String("node_id"),
            CultureInfo.InvariantCulture),
        row.OptionalString("profile_id"),
        row.String("kind"),
        currentSeverity ?? row.String("last_confirmed_severity"),
        status is "resolved" or "legacy-resolution-unverified" ? "resolved" :
            row.String("operator_state") == "acknowledged"
                ? "acknowledged"
                : "triggered",
        row.String("title"),
        row.String("summary"),
        row.String("reason"),
        row.OptionalString("evidence"),
        row.String("link"),
        row.Time("first_observed_at"),
        row.Time("triggered_at"),
        row.Time("last_observed_at"),
        row.OptionalTime("acknowledged_at"),
        row.OptionalString("acknowledged_by_github_user_id"),
        row.OptionalTime("resolved_at"))
    {
      SourceObservedAt = row.OptionalTime("source_observed_at"),
      DashboardReceivedAt = row.OptionalTime("dashboard_received_at"),
      EvaluatedAt = row.OptionalTime("evaluated_at"),
      ResolutionEvidence = row.OptionalString("resolution_evidence"),
      ConditionState = status switch
      {
        "active" => "confirmed",
        "legacy-resolution-unverified" => "legacy-unverified",
        _ => status,
      },
      OperatorState = row.String("operator_state"),
      CurrentSeverity = currentSeverity,
      LastConfirmedSeverity = row.String("last_confirmed_severity"),
      PeakSeverity = row.String("peak_severity"),
      Revision = row.Int32("incident_revision"),
      SeriesId = Guid.Parse(
          row.String("series_id"),
          CultureInfo.InvariantCulture),
      EpisodeOrdinal = row.Int32("episode_ordinal"),
      GroupingPolicyVersion = row.Int32("grouping_policy_version"),
      GroupingReasons = row.String("grouping_reasons")
          .Split(
              '|',
              StringSplitOptions.RemoveEmptyEntries |
              StringSplitOptions.TrimEntries),
      ConditionCount = row.Int32("condition_count"),
      HistoryState = row.String("history_state"),
      PreviousIncidentId = row.OptionalString("previous_episode_id") is
          { } previous
          ? Guid.Parse(previous, CultureInfo.InvariantCulture)
          : null,
      PreviousHistoryState = row.OptionalString("previous_history_state"),
      Transition = row.OptionalString("transition"),
      SuppressionReason = row.OptionalString("suppression_reason"),
      SuppressedUntil = row.OptionalTime("suppression_expires_at"),
    };
  }

  private static string ProjectConditionState(string conditionState) =>
      conditionState switch
      {
        "confirmed" => "active",
        "waiting-for-evidence" => "waiting-for-evidence",
        "recovering" => "recovering",
        "monitoring-ended" => "monitoring-ended",
        "resolved" => "resolved",
        _ => "waiting-for-evidence",
      };

  private static string Canonicalize(params string[] values)
  {
    var builder = new StringBuilder();
    foreach (var value in values)
    {
      builder.Append(
          value.Length.ToString(CultureInfo.InvariantCulture));
      builder.Append(':');
      builder.Append(value);
    }
    return builder.ToString();
  }

  private static Guid DeterministicId(string value)
  {
    Span<byte> hash = stackalloc byte[32];
    SHA256.HashData(Encoding.UTF8.GetBytes(value), hash);
    Span<byte> identifier = stackalloc byte[16];
    hash[..16].CopyTo(identifier);
    identifier[7] = (byte)((identifier[7] & 0x0F) | 0x50);
    identifier[8] = (byte)((identifier[8] & 0x3F) | 0x80);
    return new Guid(identifier);
  }

  private const string ProjectionSelect =
      """
      SELECT
          episode.episode_id,
          episode.series_id,
          episode.tenant_id,
          episode.episode_ordinal,
          series.grouping_policy_version,
          episode.node_id,
          episode.profile_id,
          episode.kind,
          episode.title,
          episode.summary,
          episode.reason,
          episode.evidence,
          episode.link,
          episode.first_observed_at,
          episode.triggered_at,
          episode.last_observed_at,
          episode.acknowledged_at,
          episode.acknowledged_by_github_user_id,
          episode.resolved_at,
          episode.status AS projected_status,
          episode.operator_state,
          episode.current_severity,
          episode.last_confirmed_severity,
          episode.peak_severity,
          episode.incident_revision,
          episode.previous_episode_id,
          episode.previous_history_state,
          episode.transition,
          episode.grouping_reasons,
          episode.condition_count,
          episode.history_state,
          episode.suppression_reason,
          episode.suppression_expires_at,
          (
              SELECT MAX(condition.source_observed_at)
              FROM actionable_incident_memberships AS membership
              JOIN alert_incidents AS condition
                ON condition.incident_id =
                    membership.condition_incident_id
              WHERE membership.episode_id = episode.episode_id
          ) AS source_observed_at,
          (
              SELECT MAX(condition.dashboard_received_at)
              FROM actionable_incident_memberships AS membership
              JOIN alert_incidents AS condition
                ON condition.incident_id =
                    membership.condition_incident_id
              WHERE membership.episode_id = episode.episode_id
          ) AS dashboard_received_at,
          (
              SELECT MAX(condition.evaluated_at)
              FROM actionable_incident_memberships AS membership
              JOIN alert_incidents AS condition
                ON condition.incident_id =
                    membership.condition_incident_id
              WHERE membership.episode_id = episode.episode_id
          ) AS evaluated_at,
          CASE episode.resolution_provenance
              WHEN 'resolution-proven' THEN 'fresh'
              WHEN 'legacy-resolution-unverified' THEN 'legacy-unverified'
              WHEN 'resolution-provenance-unavailable' THEN 'legacy-unverified'
              ELSE NULL
          END AS resolution_evidence
      FROM actionable_incident_episodes AS episode
      JOIN actionable_incident_series AS series
        ON series.series_id = episode.series_id
      """;

  private sealed record ProjectionCondition(
      string IncidentId,
      string AlertKey,
      string TenantId,
      string NodeId,
      string? ProfileId,
      string Kind,
      string Status,
      string Title,
      string Summary,
      string Reason,
      string? Evidence,
      string Link,
      DateTimeOffset FirstObservedAt,
      DateTimeOffset TriggeredAt,
      DateTimeOffset LastObservedAt,
      DateTimeOffset? ResolvedAt,
      DateTimeOffset? SourceObservedAt,
      DateTimeOffset? DashboardReceivedAt,
      DateTimeOffset? EvaluatedAt,
      DateTimeOffset? ResolutionSourceObservedAt,
      string ConditionState,
      string OperatorState,
      string? CurrentSeverity,
      string LastConfirmedSeverity,
      string PeakSeverity,
      int Revision,
      DateTimeOffset? AcknowledgedAt,
      string? AcknowledgedBy,
      string RuleFamily,
      int RuleInterpretationVersion,
      int GroupingPolicyVersion,
      string IncidentFamily,
      string CanonicalTargetScope,
      string InvestigationClass,
      string EvidenceDependency,
      string GroupingReasons,
      DateTimeOffset? RecoveryStartedAt);

  private sealed record ProjectionGrouping(
      string CanonicalKey,
      int GroupingPolicyVersion,
      string IncidentFamily,
      string CanonicalTargetScope,
      string InvestigationClass,
      string EvidenceDependency,
      string GroupingReasons);

  private sealed record ProjectedEpisode(
      string EpisodeId,
      int EpisodeOrdinal,
      string Status);

  private sealed record ProjectedExpiryBoundary(
      int EpisodeOrdinal,
      string Category);

  private sealed record MembershipPlacement(
      string EpisodeId,
      Guid SeriesId,
      string Status);
}
