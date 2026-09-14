using System.Globalization;

using Microsoft.Data.Sqlite;

using PitCrew.Dashboard.Features.Fleet.Abstractions;

namespace PitCrew.Dashboard.Adapters.Sqlite;

internal sealed partial class SqliteAlertIncidentStore(
    SqliteConnectionFactory _connectionFactory) : IAlertIncidentStore
{
  public Task ReconcileAsync(
      IReadOnlyList<AlertCandidate> candidates,
      IReadOnlyList<AlertSuppression> suppressions,
      DateTimeOffset evaluatedAt,
      DateTimeOffset resolvedBefore,
      int maximumResolvedPerTenant,
      CancellationToken cancellationToken,
      TimeSpan? expiryLocatorRetention = null,
      int maximumExpiryLocatorsPerTenant = 10_000) =>
      ReconcileAsync(
          candidates,
          [],
          suppressions,
          evaluatedAt,
          resolvedBefore,
          maximumResolvedPerTenant,
          cancellationToken,
          expiryLocatorRetention,
          maximumExpiryLocatorsPerTenant);

  public async Task ReconcileAsync(
      IReadOnlyList<AlertCandidate> candidates,
      IReadOnlyList<AlertClearance> clearances,
      IReadOnlyList<AlertSuppression> suppressions,
      DateTimeOffset evaluatedAt,
      DateTimeOffset resolvedBefore,
      int maximumResolvedPerTenant,
      CancellationToken cancellationToken,
      TimeSpan? expiryLocatorRetention = null,
      int maximumExpiryLocatorsPerTenant = 10_000)
  {
    ArgumentNullException.ThrowIfNull(candidates);
    ArgumentNullException.ThrowIfNull(clearances);
    ArgumentNullException.ThrowIfNull(suppressions);
    var distinct = candidates
        .GroupBy(candidate => candidate.Key, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group =>
        {
          if (group.Count() != 1)
          {
            throw new InvalidOperationException(
                $"Alert candidate key '{group.Key}' was produced more than once.");
          }
          return group.Single();
        }, StringComparer.Ordinal);
    var clearByKey = clearances
        .GroupBy(clearance => clearance.Key, StringComparer.Ordinal)
        .ToDictionary(
            group => group.Key,
            group => group.MaxBy(clearance => clearance.SourceObservedAt)!,
            StringComparer.Ordinal);

    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var transaction =
        await _connectionFactory.BeginWriteTransactionAsync(
            connection,
            cancellationToken);
    var open = await LoadOpenAsync(
        connection,
        transaction,
        cancellationToken);
    var latestResolutions = await LoadLatestResolutionsAsync(
        connection,
        transaction,
        distinct.Values,
        cancellationToken);

    foreach (var candidate in distinct.Values.OrderBy(
        item => item.Key,
        StringComparer.Ordinal))
    {
      var firstObservedAt = candidate.FirstObservedAt > evaluatedAt
          ? evaluatedAt
          : candidate.FirstObservedAt;
      var triggerAfter = firstObservedAt + candidate.Debounce;
      if (open.TryGetValue(candidate.Key, out var existing))
      {
        if (!IsFreshCandidate(existing, candidate))
        {
          continue;
        }
        open.Remove(candidate.Key);
        firstObservedAt = existing.FirstObservedAt;
        triggerAfter = existing.TriggerAfter;
        await UpdateExistingAsync(
            connection,
            transaction,
            existing,
            candidate,
            firstObservedAt,
            triggerAfter,
            evaluatedAt,
            cancellationToken);
        continue;
      }

      if (latestResolutions.TryGetValue(
              (candidate.TenantId, candidate.Key),
              out var latestResolution) &&
          !IsAfterResolution(candidate, latestResolution))
      {
        continue;
      }
      await InsertAsync(
          connection,
          transaction,
          candidate,
          firstObservedAt,
          triggerAfter,
          evaluatedAt,
          cancellationToken);
    }

    foreach (var stale in open.Values)
    {
      if (IsSuppressed(stale, suppressions))
      {
        if (string.Equals(
            stale.Status,
            "pending",
            StringComparison.Ordinal))
        {
          await ResetPendingAsync(
              connection,
              transaction,
              stale.IncidentId,
              evaluatedAt,
              evaluatedAt + (stale.TriggerAfter - stale.FirstObservedAt),
              cancellationToken);
        }
        else
        {
          var suppression = FindSuppression(stale, suppressions);
          await MarkEvidenceUnavailableAsync(
              connection,
              transaction,
              stale.IncidentId,
              suppression?.ConditionState ?? "waiting-for-evidence",
              evaluatedAt,
              cancellationToken);
        }
        continue;
      }
      if (!clearByKey.TryGetValue(stale.Key, out var clearance))
      {
        if (string.Equals(
            stale.Status,
            "pending",
            StringComparison.Ordinal))
        {
          await ResetPendingAsync(
              connection,
              transaction,
              stale.IncidentId,
              evaluatedAt,
              evaluatedAt + (stale.TriggerAfter - stale.FirstObservedAt),
              cancellationToken);
        }
        else
        {
          await MarkEvidenceUnavailableAsync(
              connection,
              transaction,
              stale.IncidentId,
              "waiting-for-evidence",
              evaluatedAt,
              cancellationToken);
        }
        continue;
      }
      if (!IsFreshClearance(stale, clearance))
      {
        if (string.Equals(
            stale.Status,
            "pending",
            StringComparison.Ordinal))
        {
          await ResetPendingAsync(
              connection,
              transaction,
              stale.IncidentId,
              evaluatedAt,
              evaluatedAt + (stale.TriggerAfter - stale.FirstObservedAt),
              cancellationToken);
        }
        else
        {
          if (IsClearanceReplay(stale, clearance))
          {
            continue;
          }
          await MarkEvidenceUnavailableAsync(
              connection,
              transaction,
              stale.IncidentId,
              "waiting-for-evidence",
              evaluatedAt,
              cancellationToken);
        }
        continue;
      }
      if (string.Equals(
          stale.Status,
          "pending",
          StringComparison.Ordinal))
      {
        await DeletePendingAsync(
            connection,
            transaction,
            stale.IncidentId,
            cancellationToken);
      }
      else
      {
        await ApplyClearanceAsync(
            connection,
            transaction,
            stale,
            clearance,
            evaluatedAt,
            cancellationToken);
      }
    }

    await SynchronizeActionableProjectionAsync(
        connection,
        transaction,
        evaluatedAt,
        cancellationToken);
    await DeleteExpiredResolvedAsync(
        connection,
        transaction,
        resolvedBefore,
        cancellationToken);
    await BoundResolvedAsync(
        connection,
        transaction,
        maximumResolvedPerTenant,
        cancellationToken);
    await CompactActionableHistoryAsync(
        connection,
        transaction,
        evaluatedAt,
        resolvedBefore,
        maximumResolvedPerTenant,
        expiryLocatorRetention ??
            evaluatedAt - resolvedBefore,
        maximumExpiryLocatorsPerTenant,
        cancellationToken);
    await transaction.CommitAsync(cancellationToken);
  }

  public async Task<AlertIncidentPage> GetAsync(
      string tenantId,
      AlertIncidentFilter filter,
      int limit,
      DateTimeOffset generatedAt,
      CancellationToken cancellationToken) =>
      await GetProjectedPageAsync(
          tenantId,
          filter,
          limit,
          null,
          generatedAt,
          cancellationToken);

  private async Task<AlertIncidentPage> GetLegacyPageAsync(
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
          "status IN ('triggered', 'acknowledged')",
      AlertIncidentFilter.Resolved => "status = 'resolved'",
      AlertIncidentFilter.All =>
          "status IN ('triggered', 'acknowledged', 'resolved')",
      _ => throw new ArgumentOutOfRangeException(nameof(filter)),
    };
    const string attentionRank =
        """
        CASE
            WHEN status = 'resolved' THEN 6
            WHEN current_severity = 'critical'
              AND operator_state = 'unowned' THEN 0
            WHEN current_severity = 'critical' THEN 1
            WHEN current_severity = 'warning'
              AND operator_state = 'unowned' THEN 2
            WHEN current_severity IS NULL
              AND operator_state = 'unowned' THEN 3
            WHEN current_severity = 'warning' THEN 4
            ELSE 5
        END
        """;
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var transaction = (SqliteTransaction)
        await connection.BeginTransactionAsync(cancellationToken);
    var totals = await LoadTotalsAsync(
        connection,
        transaction,
        tenantId,
        statusClause,
        cancellationToken);
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        $"""
        SELECT
            incident_id,
            node_id,
            profile_id,
            kind,
            severity,
            status,
            title,
            summary,
            reason,
            evidence,
            link,
            first_observed_at,
            triggered_at,
            last_observed_at,
            acknowledged_at,
            acknowledged_by_github_user_id,
            resolved_at,
            source_observed_at,
            dashboard_received_at,
            evaluated_at,
            condition_state,
            operator_state,
            current_severity,
            last_confirmed_severity,
            peak_severity,
            incident_revision,
            CASE
                WHEN status = 'resolved'
                  AND resolution_source_observed_at IS NOT NULL
                  AND resolution_dashboard_received_at IS NOT NULL
                  AND resolution_evaluated_at IS NOT NULL
                    THEN 'fresh'
                WHEN status = 'resolved'
                    THEN 'legacy-unverified'
                ELSE NULL
            END AS resolution_evidence,
            {attentionRank} AS attention_rank,
            COALESCE(resolved_at, triggered_at) AS sort_at
        FROM alert_incidents
        WHERE tenant_id = $tenantId
          AND {statusClause}
          AND (
              $cursorRank IS NULL
              OR {attentionRank} > $cursorRank
              OR (
                  {attentionRank} = $cursorRank
                  AND (
                      COALESCE(resolved_at, triggered_at) < $cursorSortAt
                      OR (
                          COALESCE(resolved_at, triggered_at) = $cursorSortAt
                          AND incident_id < $cursorIncidentId))))
        ORDER BY
            attention_rank,
            COALESCE(resolved_at, triggered_at) DESC,
            incident_id DESC
        LIMIT $fetchLimit;
        """;
    command.Parameters.AddWithValue("$tenantId", tenantId);
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
    var attentionRanks = new List<int>();
    var sortTimes = new List<DateTimeOffset>();
    await using var reader = await command.ExecuteReaderAsync(
        cancellationToken);
    SqliteRowReader? row = null;
    while (await reader.ReadAsync(cancellationToken))
    {
      row ??= new SqliteRowReader(reader);
      incidents.Add(new AlertIncident(
          Guid.Parse(
              row.String("incident_id"),
              CultureInfo.InvariantCulture),
          Guid.Parse(
              row.String("node_id"),
              CultureInfo.InvariantCulture),
          row.OptionalString("profile_id"),
          row.String("kind"),
          row.String("severity"),
          row.String("status"),
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
        ConditionState = row.String("condition_state"),
        OperatorState = row.String("operator_state"),
        CurrentSeverity = row.OptionalString("current_severity"),
        LastConfirmedSeverity = row.String("last_confirmed_severity"),
        PeakSeverity = row.String("peak_severity"),
        Revision = row.Int32("incident_revision"),
      });
      attentionRanks.Add(row.Int32("attention_rank"));
      sortTimes.Add(row.Time("sort_at"));
    }

    var truncated = incidents.Count > limit;
    if (truncated)
    {
      incidents.RemoveAt(incidents.Count - 1);
      attentionRanks.RemoveAt(attentionRanks.Count - 1);
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
              attentionRanks[^1],
              sortTimes[^1],
              incidents[^1].IncidentId).ToString()
          : null,
    };
  }

  private async Task<AlertIncident?> GetLegacyByIdAsync(
      string tenantId,
      Guid incidentId,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        SELECT
            incident_id,
            node_id,
            profile_id,
            kind,
            severity,
            status,
            title,
            summary,
            reason,
            evidence,
            link,
            first_observed_at,
            triggered_at,
            last_observed_at,
            acknowledged_at,
            acknowledged_by_github_user_id,
            resolved_at,
            source_observed_at,
            dashboard_received_at,
            evaluated_at,
            condition_state,
            operator_state,
            current_severity,
            last_confirmed_severity,
            peak_severity,
            incident_revision,
            CASE
                WHEN status = 'resolved'
                  AND resolution_source_observed_at IS NOT NULL
                  AND resolution_dashboard_received_at IS NOT NULL
                  AND resolution_evaluated_at IS NOT NULL
                    THEN 'fresh'
                WHEN status = 'resolved'
                    THEN 'legacy-unverified'
                ELSE NULL
            END AS resolution_evidence
        FROM alert_incidents
        WHERE tenant_id = $tenantId
          AND incident_id = $incidentId;
        """;
    command.Parameters.AddWithValue("$tenantId", tenantId);
    command.Parameters.AddWithValue(
        "$incidentId",
        incidentId.ToString("D"));
    await using var reader = await command.ExecuteReaderAsync(
        cancellationToken);
    if (!await reader.ReadAsync(cancellationToken))
    {
      return null;
    }
    return ReadIncident(new SqliteRowReader(reader));
  }

  private static async Task<IncidentTotals> LoadTotalsAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      string tenantId,
      string statusClause,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        $"""
        SELECT
            COUNT(*) AS total,
            COALESCE(SUM(
                CASE WHEN current_severity = 'critical' THEN 1 ELSE 0 END), 0)
                AS critical,
            COALESCE(SUM(
                CASE WHEN current_severity = 'warning' THEN 1 ELSE 0 END), 0)
                AS warning
        FROM alert_incidents
        WHERE tenant_id = $tenantId
          AND {statusClause};
        """;
    command.Parameters.AddWithValue("$tenantId", tenantId);
    await using var reader = await command.ExecuteReaderAsync(
        cancellationToken);
    if (!await reader.ReadAsync(cancellationToken))
    {
      return new IncidentTotals(0, 0, 0);
    }
    return new IncidentTotals(
        reader.GetInt32(0),
        reader.GetInt32(1),
        reader.GetInt32(2));
  }

  private static AlertIncident ReadIncident(SqliteRowReader row) =>
      new(
          Guid.Parse(
              row.String("incident_id"),
              CultureInfo.InvariantCulture),
          Guid.Parse(
              row.String("node_id"),
              CultureInfo.InvariantCulture),
          row.OptionalString("profile_id"),
          row.String("kind"),
          row.String("severity"),
          row.String("status"),
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
        ConditionState = row.String("condition_state"),
        OperatorState = row.String("operator_state"),
        CurrentSeverity = row.OptionalString("current_severity"),
        LastConfirmedSeverity = row.String("last_confirmed_severity"),
        PeakSeverity = row.String("peak_severity"),
        Revision = row.Int32("incident_revision"),
      };

  private async Task<AlertAcknowledgeStatus> AcknowledgeLegacyAsync(
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
    await using (var command = connection.CreateCommand())
    {
      command.Transaction = transaction;
      command.CommandText =
          """
          UPDATE alert_incidents
          SET status = 'acknowledged',
              operator_state = 'acknowledged',
              acknowledged_at = $acknowledgedAt,
              acknowledged_by_github_user_id = $acknowledgedBy,
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
      command.Parameters.AddWithValue(
          "$acknowledgedAt",
          Utc(acknowledgedAt));
      command.Parameters.AddWithValue(
          "$acknowledgedBy",
          acknowledgedByGitHubUserId);
      var acknowledgedRevision = await command.ExecuteScalarAsync(
          cancellationToken);
      if (acknowledgedRevision is not null)
      {
        await InsertAcknowledgementEventAsync(
            connection,
            transaction,
            tenantId,
            incidentId,
            "acknowledged",
            acknowledgedByGitHubUserId,
            Convert.ToInt32(
                acknowledgedRevision,
                CultureInfo.InvariantCulture),
            acknowledgedAt,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return AlertAcknowledgeStatus.Succeeded;
      }
    }

    await using var query = connection.CreateCommand();
    query.Transaction = transaction;
    query.CommandText =
        """
        SELECT status
        FROM alert_incidents
        WHERE tenant_id = $tenantId
          AND incident_id = $incidentId;
        """;
    query.Parameters.AddWithValue("$tenantId", tenantId);
    query.Parameters.AddWithValue("$incidentId", incidentId.ToString("D"));
    var status = Convert.ToString(
        await query.ExecuteScalarAsync(cancellationToken),
        CultureInfo.InvariantCulture);
    await transaction.CommitAsync(cancellationToken);
    return status switch
    {
      "acknowledged" => AlertAcknowledgeStatus.Succeeded,
      "resolved" => AlertAcknowledgeStatus.Resolved,
      _ => AlertAcknowledgeStatus.NotFound,
    };
  }

  private async Task<AlertUnacknowledgeStatus> UnacknowledgeLegacyAsync(
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
    await using (var command = connection.CreateCommand())
    {
      command.Transaction = transaction;
      command.CommandText =
          """
          UPDATE alert_incidents
          SET status = 'triggered',
              operator_state = 'unowned',
              acknowledged_at = NULL,
              acknowledged_by_github_user_id = NULL,
              acknowledged_revision = NULL,
              updated_at = $now
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
          "$now",
          Utc(unacknowledgedAt));
      var unacknowledgedRevision = await command.ExecuteScalarAsync(
          cancellationToken);
      if (unacknowledgedRevision is not null)
      {
        await InsertAcknowledgementEventAsync(
            connection,
            transaction,
            tenantId,
            incidentId,
            "unacknowledged",
            unacknowledgedByGitHubUserId,
            Convert.ToInt32(
                unacknowledgedRevision,
                CultureInfo.InvariantCulture),
            unacknowledgedAt,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return AlertUnacknowledgeStatus.Succeeded;
      }

    }

    await using var query = connection.CreateCommand();
    query.Transaction = transaction;
    query.CommandText =
        """
        SELECT status
        FROM alert_incidents
        WHERE tenant_id = $tenantId
          AND incident_id = $incidentId;
        """;
    query.Parameters.AddWithValue("$tenantId", tenantId);
    query.Parameters.AddWithValue("$incidentId", incidentId.ToString("D"));
    var currentStatus = Convert.ToString(
        await query.ExecuteScalarAsync(cancellationToken),
        CultureInfo.InvariantCulture);
    await transaction.CommitAsync(cancellationToken);
    return currentStatus switch
    {
      "triggered" => AlertUnacknowledgeStatus.AlreadyTriggered,
      "resolved" => AlertUnacknowledgeStatus.Resolved,
      _ => AlertUnacknowledgeStatus.NotFound,
    };
  }

  private static async Task<Dictionary<string, OpenIncident>> LoadOpenAsync(
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
            node_id,
            profile_id,
            kind,
            status,
            first_observed_at,
            trigger_after,
            source_observed_at,
            dashboard_received_at,
            last_confirmed_severity,
            operator_state,
            condition_state,
            recovery_started_at,
            recovery_sample_count,
            recovery_source_observed_at,
            recovery_dashboard_received_at
        FROM alert_incidents
        WHERE status IN ('pending', 'triggered', 'acknowledged')
        ORDER BY alert_key;
        """;
    var result = new Dictionary<string, OpenIncident>(
        StringComparer.Ordinal);
    await using var reader = await command.ExecuteReaderAsync(
        cancellationToken);
    var row = new SqliteRowReader(reader);
    while (await reader.ReadAsync(cancellationToken))
    {
      result.Add(
          row.String("alert_key"),
          new OpenIncident(
              row.String("incident_id"),
              row.String("alert_key"),
              row.String("node_id"),
              row.OptionalString("profile_id"),
              row.String("kind"),
              row.String("status"),
              row.Time("first_observed_at"),
              row.Time("trigger_after"),
              row.OptionalTime("source_observed_at"),
              row.OptionalTime("dashboard_received_at"),
              row.String("last_confirmed_severity"),
              row.String("operator_state"),
              row.String("condition_state"),
              row.OptionalTime("recovery_started_at"),
              row.Int32("recovery_sample_count"),
              row.OptionalTime("recovery_source_observed_at"),
              row.OptionalTime("recovery_dashboard_received_at")));
    }
    return result;
  }

  private static async Task<
      Dictionary<(string TenantId, string Key), LatestResolution>>
      LoadLatestResolutionsAsync(
          SqliteConnection connection,
          SqliteTransaction transaction,
          IEnumerable<AlertCandidate> candidates,
          CancellationToken cancellationToken)
  {
    var candidateKeys = candidates
        .Select(candidate => (candidate.TenantId, candidate.Key))
        .ToHashSet();
    if (candidateKeys.Count == 0)
    {
      return [];
    }
    var tenantIds = candidateKeys
        .Select(candidate => candidate.TenantId)
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .ToArray();
    var tenantParameters = string.Join(
        ", ",
        tenantIds.Select((_, index) => $"$tenant{index}"));
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        $"""
        SELECT
            tenant_id,
            alert_key,
            resolution_source_observed_at,
            resolution_dashboard_received_at
        FROM (
            SELECT
                tenant_id,
                alert_key,
                resolution_source_observed_at,
                resolution_dashboard_received_at,
                ROW_NUMBER() OVER (
                    PARTITION BY tenant_id, alert_key
                    ORDER BY resolved_at DESC, incident_id DESC) AS rank_index
            FROM alert_incidents
            WHERE status = 'resolved'
              AND tenant_id IN ({tenantParameters}))
        WHERE rank_index = 1;
        """;
    for (var index = 0; index < tenantIds.Length; index++)
    {
      command.Parameters.AddWithValue($"$tenant{index}", tenantIds[index]);
    }
    var result =
        new Dictionary<(string TenantId, string Key), LatestResolution>();
    await using var reader = await command.ExecuteReaderAsync(
        cancellationToken);
    var row = new SqliteRowReader(reader);
    while (await reader.ReadAsync(cancellationToken))
    {
      var key = (row.String("tenant_id"), row.String("alert_key"));
      if (candidateKeys.Contains(key))
      {
        result[key] = new LatestResolution(
            row.OptionalTime("resolution_source_observed_at"),
            row.OptionalTime("resolution_dashboard_received_at"));
      }
    }
    return result;
  }

  private static async Task InsertAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      AlertCandidate candidate,
      DateTimeOffset firstObservedAt,
      DateTimeOffset triggerAfter,
      DateTimeOffset evaluatedAt,
      CancellationToken cancellationToken)
  {
    var triggered = evaluatedAt >= triggerAfter;
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        INSERT INTO alert_incidents (
            incident_id,
            alert_key,
            tenant_id,
            node_id,
            profile_id,
            kind,
            severity,
            status,
            title,
            summary,
            reason,
            evidence,
            link,
            first_observed_at,
            trigger_after,
            last_observed_at,
            triggered_at,
            source_observed_at,
            dashboard_received_at,
            evaluated_at,
            condition_state,
            operator_state,
            current_severity,
            last_confirmed_severity,
            peak_severity,
            incident_revision,
            rule_family,
            rule_interpretation_version,
            grouping_policy_version,
            incident_family,
            canonical_target_scope,
            investigation_class,
            evidence_dependency,
            grouping_reasons,
            recovery_started_at,
            recovery_sample_count,
            created_at,
            updated_at)
        VALUES (
            $incidentId,
            $alertKey,
            $tenantId,
            $nodeId,
            $profileId,
            $kind,
            $severity,
            $status,
            $title,
            $summary,
            $reason,
            $evidence,
            $link,
            $firstObservedAt,
            $triggerAfter,
            $lastObservedAt,
            $triggeredAt,
            $sourceObservedAt,
            $dashboardReceivedAt,
            $evaluatedAt,
            $conditionState,
            'unowned',
            $currentSeverity,
            $severity,
            $severity,
            1,
            $ruleFamily,
            $ruleInterpretationVersion,
            $groupingPolicyVersion,
            $incidentFamily,
            $canonicalTargetScope,
            $investigationClass,
            $evidenceDependency,
            $groupingReasons,
            NULL,
            0,
            $createdAt,
            $updatedAt);
        """;
    command.Parameters.AddWithValue(
        "$incidentId",
        Guid.NewGuid().ToString("D"));
    AddCandidateParameters(command, candidate);
    command.Parameters.AddWithValue(
        "$status",
        triggered ? "triggered" : "pending");
    command.Parameters.AddWithValue(
        "$firstObservedAt",
        Utc(firstObservedAt));
    command.Parameters.AddWithValue("$triggerAfter", Utc(triggerAfter));
    command.Parameters.AddWithValue("$lastObservedAt", Utc(evaluatedAt));
    command.Parameters.AddWithValue(
        "$sourceObservedAt",
        Utc(candidate.SourceObservedAt));
    command.Parameters.AddWithValue(
        "$dashboardReceivedAt",
        Utc(candidate.DashboardReceivedAt));
    command.Parameters.AddWithValue("$evaluatedAt", Utc(evaluatedAt));
    command.Parameters.AddWithValue(
        "$conditionState",
        triggered ? "confirmed" : "waiting-for-evidence");
    command.Parameters.AddWithValue(
        "$currentSeverity",
        triggered ? candidate.Severity : DBNull.Value);
    command.Parameters.AddWithValue(
        "$triggeredAt",
        triggered ? Utc(triggerAfter) : DBNull.Value);
    command.Parameters.AddWithValue("$createdAt", Utc(evaluatedAt));
    command.Parameters.AddWithValue("$updatedAt", Utc(evaluatedAt));
    await command.ExecuteNonQueryAsync(cancellationToken);
  }

  private static async Task UpdateExistingAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      OpenIncident existing,
      AlertCandidate candidate,
      DateTimeOffset firstObservedAt,
      DateTimeOffset triggerAfter,
      DateTimeOffset evaluatedAt,
      CancellationToken cancellationToken)
  {
    var shouldTrigger = string.Equals(
        existing.Status,
        "pending",
        StringComparison.Ordinal) &&
        evaluatedAt >= triggerAfter;
    var materialEscalation =
        !string.Equals(existing.Status, "pending", StringComparison.Ordinal) &&
        string.Equals(
            existing.LastConfirmedSeverity,
            "warning",
            StringComparison.Ordinal) &&
        string.Equals(candidate.Severity, "critical", StringComparison.Ordinal);
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        UPDATE alert_incidents
        SET tenant_id = $tenantId,
            node_id = $nodeId,
            profile_id = $profileId,
            kind = $kind,
            severity = $severity,
            status = CASE
                WHEN $materialEscalation = 1 THEN 'triggered'
                WHEN status = 'pending' AND $shouldTrigger = 1
                    THEN 'triggered'
                ELSE status
            END,
            title = $title,
            summary = $summary,
            reason = $reason,
            evidence = $evidence,
            link = $link,
            first_observed_at = $firstObservedAt,
            trigger_after = $triggerAfter,
            last_observed_at = $lastObservedAt,
            source_observed_at = $sourceObservedAt,
            dashboard_received_at = $dashboardReceivedAt,
            evaluated_at = $evaluatedAt,
            condition_state = CASE
                WHEN status = 'pending' AND $shouldTrigger = 0
                    THEN 'waiting-for-evidence'
                ELSE 'confirmed'
            END,
            operator_state = CASE
                WHEN $materialEscalation = 1 THEN 'unowned'
                ELSE operator_state
            END,
            current_severity = CASE
                WHEN status = 'pending' AND $shouldTrigger = 0 THEN NULL
                ELSE $severity
            END,
            last_confirmed_severity = CASE
                WHEN status = 'pending' AND $shouldTrigger = 0
                    THEN last_confirmed_severity
                ELSE $severity
            END,
            peak_severity = CASE
                WHEN $severity = 'critical' THEN 'critical'
                ELSE peak_severity
            END,
            recovery_started_at = NULL,
            recovery_sample_count = 0,
            recovery_source_observed_at = NULL,
            recovery_dashboard_received_at = NULL,
            incident_revision = incident_revision
                + CASE WHEN $materialEscalation = 1 THEN 1 ELSE 0 END,
            acknowledged_at = CASE
                WHEN $materialEscalation = 1 THEN NULL
                ELSE acknowledged_at
            END,
            acknowledged_by_github_user_id = CASE
                WHEN $materialEscalation = 1 THEN NULL
                ELSE acknowledged_by_github_user_id
            END,
            acknowledged_revision = CASE
                WHEN $materialEscalation = 1 THEN NULL
                ELSE acknowledged_revision
            END,
            triggered_at = CASE
                WHEN triggered_at IS NULL AND $shouldTrigger = 1
                    THEN $triggerAfter
                ELSE triggered_at
            END,
            updated_at = $updatedAt
        WHERE incident_id = $incidentId;
        """;
    AddCandidateParameters(command, candidate);
    command.Parameters.AddWithValue(
        "$incidentId",
        existing.IncidentId);
    command.Parameters.AddWithValue(
        "$shouldTrigger",
        shouldTrigger ? 1 : 0);
    command.Parameters.AddWithValue(
        "$materialEscalation",
        materialEscalation ? 1 : 0);
    command.Parameters.AddWithValue(
        "$firstObservedAt",
        Utc(firstObservedAt));
    command.Parameters.AddWithValue("$triggerAfter", Utc(triggerAfter));
    command.Parameters.AddWithValue("$lastObservedAt", Utc(evaluatedAt));
    command.Parameters.AddWithValue(
        "$sourceObservedAt",
        Utc(candidate.SourceObservedAt));
    command.Parameters.AddWithValue(
        "$dashboardReceivedAt",
        Utc(candidate.DashboardReceivedAt));
    command.Parameters.AddWithValue("$evaluatedAt", Utc(evaluatedAt));
    command.Parameters.AddWithValue("$updatedAt", Utc(evaluatedAt));
    await command.ExecuteNonQueryAsync(cancellationToken);
  }

  private static void AddCandidateParameters(
      SqliteCommand command,
      AlertCandidate candidate)
  {
    command.Parameters.AddWithValue("$alertKey", candidate.Key);
    command.Parameters.AddWithValue("$tenantId", candidate.TenantId);
    command.Parameters.AddWithValue(
        "$nodeId",
        candidate.NodeId.ToString("D"));
    command.Parameters.AddWithValue(
        "$profileId",
        candidate.ProfileId is null
            ? DBNull.Value
            : candidate.ProfileId);
    command.Parameters.AddWithValue("$kind", candidate.Kind);
    command.Parameters.AddWithValue("$severity", candidate.Severity);
    command.Parameters.AddWithValue("$title", candidate.Title);
    command.Parameters.AddWithValue("$summary", candidate.Summary);
    command.Parameters.AddWithValue("$reason", candidate.Reason);
    command.Parameters.AddWithValue(
        "$evidence",
        candidate.Evidence is null
            ? DBNull.Value
            : candidate.Evidence);
    command.Parameters.AddWithValue("$link", candidate.Link);
    command.Parameters.AddWithValue(
        "$ruleFamily",
        candidate.RuleFamily ?? candidate.Kind);
    command.Parameters.AddWithValue(
        "$ruleInterpretationVersion",
        candidate.RuleInterpretationVersion);
    command.Parameters.AddWithValue(
        "$groupingPolicyVersion",
        candidate.GroupingPolicyVersion);
    command.Parameters.AddWithValue(
        "$incidentFamily",
        candidate.IncidentFamily ?? candidate.Kind);
    command.Parameters.AddWithValue(
        "$canonicalTargetScope",
        candidate.CanonicalTargetScope ?? $"condition:{candidate.Key}");
    command.Parameters.AddWithValue(
        "$investigationClass",
        candidate.InvestigationClass ?? $"condition:{candidate.Kind}");
    command.Parameters.AddWithValue(
        "$evidenceDependency",
        candidate.EvidenceDependency ?? $"condition:{candidate.Key}");
    command.Parameters.AddWithValue(
        "$groupingReasons",
        string.Join(
            "|",
            candidate.GroupingReasons
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)));
  }

  private static async Task DeletePendingAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      string incidentId,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        DELETE FROM alert_incidents
        WHERE incident_id = $incidentId
          AND status = 'pending';
        """;
    command.Parameters.AddWithValue("$incidentId", incidentId);
    await command.ExecuteNonQueryAsync(cancellationToken);
  }

  private static async Task ResetPendingAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      string incidentId,
      DateTimeOffset firstObservedAt,
      DateTimeOffset triggerAfter,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        UPDATE alert_incidents
        SET first_observed_at = $firstObservedAt,
            trigger_after = $triggerAfter,
            last_observed_at = $firstObservedAt,
            updated_at = $firstObservedAt
        WHERE incident_id = $incidentId
          AND status = 'pending';
        """;
    command.Parameters.AddWithValue("$incidentId", incidentId);
    command.Parameters.AddWithValue(
        "$firstObservedAt",
        Utc(firstObservedAt));
    command.Parameters.AddWithValue("$triggerAfter", Utc(triggerAfter));
    await command.ExecuteNonQueryAsync(cancellationToken);
  }

  private static async Task ResolveAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      string incidentId,
      AlertClearance clearance,
      DateTimeOffset resolvedAt,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        UPDATE alert_incidents
        SET status = 'resolved',
            condition_state = 'resolved',
            current_severity = NULL,
            resolved_at = $resolvedAt,
            resolution_source_observed_at = $sourceObservedAt,
            resolution_dashboard_received_at = $dashboardReceivedAt,
            resolution_evaluated_at = $resolvedAt,
            updated_at = $resolvedAt
        WHERE incident_id = $incidentId
          AND status IN ('triggered', 'acknowledged');
        """;
    command.Parameters.AddWithValue("$incidentId", incidentId);
    command.Parameters.AddWithValue("$resolvedAt", Utc(resolvedAt));
    command.Parameters.AddWithValue(
        "$sourceObservedAt",
        Utc(clearance.SourceObservedAt));
    command.Parameters.AddWithValue(
        "$dashboardReceivedAt",
        Utc(clearance.DashboardReceivedAt));
    await command.ExecuteNonQueryAsync(cancellationToken);
  }

  private static async Task ApplyClearanceAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      OpenIncident incident,
      AlertClearance clearance,
      DateTimeOffset evaluatedAt,
      CancellationToken cancellationToken)
  {
    var recoveryStartedAt = incident.RecoveryStartedAt ?? evaluatedAt;
    var recoverySamples = incident.RecoverySampleCount + 1;
    if (recoverySamples >= clearance.RequiredSamples &&
        evaluatedAt - recoveryStartedAt >= clearance.RecoveryHysteresis)
    {
      await ResolveAsync(
          connection,
          transaction,
          incident.IncidentId,
          clearance,
          evaluatedAt,
          cancellationToken);
      return;
    }

    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        UPDATE alert_incidents
        SET condition_state = 'waiting-for-evidence',
            current_severity = NULL,
            recovery_started_at = $recoveryStartedAt,
            recovery_sample_count = $recoverySamples,
            recovery_source_observed_at = $sourceObservedAt,
            recovery_dashboard_received_at = $dashboardReceivedAt,
            evaluated_at = $evaluatedAt,
            updated_at = $evaluatedAt
        WHERE incident_id = $incidentId
          AND status IN ('triggered', 'acknowledged');
        """;
    command.Parameters.AddWithValue("$incidentId", incident.IncidentId);
    command.Parameters.AddWithValue(
        "$recoveryStartedAt",
        Utc(recoveryStartedAt));
    command.Parameters.AddWithValue("$recoverySamples", recoverySamples);
    command.Parameters.AddWithValue(
        "$sourceObservedAt",
        Utc(clearance.SourceObservedAt));
    command.Parameters.AddWithValue(
        "$dashboardReceivedAt",
        Utc(clearance.DashboardReceivedAt));
    command.Parameters.AddWithValue("$evaluatedAt", Utc(evaluatedAt));
    await command.ExecuteNonQueryAsync(cancellationToken);
  }

  private static async Task InsertAcknowledgementEventAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      string tenantId,
      Guid incidentId,
      string action,
      string actorGitHubUserId,
      int incidentRevision,
      DateTimeOffset occurredAt,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        INSERT INTO alert_incident_acknowledgement_events (
            event_id,
            tenant_id,
            incident_id,
            action,
            actor_github_user_id,
            incident_revision,
            occurred_at)
        VALUES (
            $eventId,
            $tenantId,
            $incidentId,
            $action,
            $actor,
            $incidentRevision,
            $occurredAt);
        """;
    command.Parameters.AddWithValue(
        "$eventId",
        Guid.NewGuid().ToString("D"));
    command.Parameters.AddWithValue("$tenantId", tenantId);
    command.Parameters.AddWithValue(
        "$incidentId",
        incidentId.ToString("D"));
    command.Parameters.AddWithValue("$action", action);
    command.Parameters.AddWithValue("$actor", actorGitHubUserId);
    command.Parameters.AddWithValue(
        "$incidentRevision",
        incidentRevision);
    command.Parameters.AddWithValue("$occurredAt", Utc(occurredAt));
    await command.ExecuteNonQueryAsync(cancellationToken);
  }

  private static async Task DeleteExpiredResolvedAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      DateTimeOffset resolvedBefore,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        DELETE FROM alert_incidents
        WHERE status = 'resolved'
          AND resolved_at < $resolvedBefore;
        """;
    command.Parameters.AddWithValue(
        "$resolvedBefore",
        Utc(resolvedBefore));
    await command.ExecuteNonQueryAsync(cancellationToken);
  }

  private static async Task BoundResolvedAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      int maximumResolvedPerTenant,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        DELETE FROM alert_incidents
        WHERE incident_id IN (
            SELECT incident_id
            FROM (
                SELECT
                    incident_id,
                    ROW_NUMBER() OVER (
                        PARTITION BY tenant_id
                        ORDER BY resolved_at DESC, incident_id DESC)
                        AS rank_index
                FROM alert_incidents
                WHERE status = 'resolved')
            WHERE rank_index > $maximum);
        """;
    command.Parameters.AddWithValue(
        "$maximum",
        maximumResolvedPerTenant);
    await command.ExecuteNonQueryAsync(cancellationToken);
  }

  private static string Utc(DateTimeOffset value) =>
      value.ToUniversalTime().ToString(
          "O",
          CultureInfo.InvariantCulture);

  private static bool IsSuppressed(
      OpenIncident incident,
      IReadOnlyList<AlertSuppression> suppressions)
  {
    foreach (var suppression in suppressions)
    {
      if (suppression.Key is not null)
      {
        if (string.Equals(
            suppression.Key,
            incident.Key,
            StringComparison.Ordinal))
        {
          return true;
        }
        continue;
      }
      if (!string.Equals(
          suppression.NodeId.ToString("D"),
          incident.NodeId,
          StringComparison.Ordinal) ||
          suppression.ProfileId is not null &&
          !string.Equals(
              suppression.ProfileId,
              incident.ProfileId,
              StringComparison.Ordinal) ||
          suppression.Kind is not null &&
          !string.Equals(
              suppression.Kind,
              incident.Kind,
              StringComparison.Ordinal))
      {
        continue;
      }
      return true;
    }
    return false;
  }

  private static AlertSuppression? FindSuppression(
      OpenIncident incident,
      IReadOnlyList<AlertSuppression> suppressions)
  {
    foreach (var suppression in suppressions)
    {
      if (suppression.Key is not null)
      {
        if (string.Equals(
            suppression.Key,
            incident.Key,
            StringComparison.Ordinal))
        {
          return suppression;
        }
        continue;
      }
      if (string.Equals(
          suppression.NodeId.ToString("D"),
          incident.NodeId,
          StringComparison.Ordinal) &&
          (suppression.ProfileId is null ||
           string.Equals(
               suppression.ProfileId,
               incident.ProfileId,
               StringComparison.Ordinal)) &&
          (suppression.Kind is null ||
           string.Equals(
               suppression.Kind,
               incident.Kind,
               StringComparison.Ordinal)))
      {
        return suppression;
      }
    }
    return null;
  }

  private static async Task MarkEvidenceUnavailableAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      string incidentId,
      string conditionState,
      DateTimeOffset evaluatedAt,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        UPDATE alert_incidents
        SET condition_state = $conditionState,
            current_severity = NULL,
            recovery_started_at = NULL,
            recovery_sample_count = 0,
            recovery_source_observed_at = NULL,
            recovery_dashboard_received_at = NULL,
            evaluated_at = $evaluatedAt,
            updated_at = $evaluatedAt
        WHERE incident_id = $incidentId
          AND status IN ('triggered', 'acknowledged');
        """;
    command.Parameters.AddWithValue("$incidentId", incidentId);
    command.Parameters.AddWithValue("$conditionState", conditionState);
    command.Parameters.AddWithValue("$evaluatedAt", Utc(evaluatedAt));
    await command.ExecuteNonQueryAsync(cancellationToken);
  }

  private static bool IsFreshCandidate(
      OpenIncident incident,
      AlertCandidate candidate)
  {
    if (incident.SourceObservedAt is null ||
        incident.DashboardReceivedAt is null)
    {
      return true;
    }
    var nonRegressing =
        candidate.SourceObservedAt >= incident.SourceObservedAt &&
        candidate.DashboardReceivedAt >= incident.DashboardReceivedAt;
    var advancesProvenance =
        candidate.SourceObservedAt > incident.SourceObservedAt ||
        candidate.DashboardReceivedAt > incident.DashboardReceivedAt;
    return nonRegressing &&
        (advancesProvenance ||
         string.Equals(
             incident.Status,
             "pending",
             StringComparison.Ordinal) ||
         string.Equals(
             incident.ConditionState,
             "confirmed",
             StringComparison.Ordinal));
  }

  private static bool IsAfterResolution(
      AlertCandidate candidate,
      LatestResolution resolution) =>
      resolution.SourceObservedAt is null ||
      resolution.DashboardReceivedAt is null ||
      (candidate.SourceObservedAt > resolution.SourceObservedAt &&
       candidate.DashboardReceivedAt >= resolution.DashboardReceivedAt);

  private static bool IsFreshClearance(
      OpenIncident incident,
      AlertClearance clearance)
  {
    var sourceObservedAt =
        incident.RecoverySourceObservedAt ?? incident.SourceObservedAt;
    var dashboardReceivedAt =
        incident.RecoveryDashboardReceivedAt ??
        incident.DashboardReceivedAt;
    return sourceObservedAt is null ||
        dashboardReceivedAt is null ||
        (clearance.SourceObservedAt > sourceObservedAt &&
         clearance.DashboardReceivedAt >= dashboardReceivedAt);
  }

  private static bool IsClearanceReplay(
      OpenIncident incident,
      AlertClearance clearance) =>
      incident.RecoverySourceObservedAt == clearance.SourceObservedAt &&
      incident.RecoveryDashboardReceivedAt ==
          clearance.DashboardReceivedAt;

  private sealed record OpenIncident(
      string IncidentId,
      string Key,
      string NodeId,
      string? ProfileId,
      string Kind,
      string Status,
      DateTimeOffset FirstObservedAt,
      DateTimeOffset TriggerAfter,
      DateTimeOffset? SourceObservedAt,
      DateTimeOffset? DashboardReceivedAt,
      string LastConfirmedSeverity,
      string OperatorState,
      string ConditionState,
      DateTimeOffset? RecoveryStartedAt,
      int RecoverySampleCount,
      DateTimeOffset? RecoverySourceObservedAt,
      DateTimeOffset? RecoveryDashboardReceivedAt);

  private sealed record LatestResolution(
      DateTimeOffset? SourceObservedAt,
      DateTimeOffset? DashboardReceivedAt);

  private sealed record IncidentTotals(
      int Total,
      int Critical,
      int Warning);
}
