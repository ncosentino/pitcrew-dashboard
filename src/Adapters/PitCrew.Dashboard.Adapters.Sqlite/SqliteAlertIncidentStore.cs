using System.Globalization;

using Microsoft.Data.Sqlite;

using PitCrew.Dashboard.Features.Fleet.Abstractions;

namespace PitCrew.Dashboard.Adapters.Sqlite;

internal sealed class SqliteAlertIncidentStore(
    SqliteConnectionFactory _connectionFactory) : IAlertIncidentStore
{
  public async Task ReconcileAsync(
      IReadOnlyList<AlertCandidate> candidates,
      IReadOnlyList<AlertSuppression> suppressions,
      DateTimeOffset evaluatedAt,
      DateTimeOffset resolvedBefore,
      int maximumResolvedPerTenant,
      CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(candidates);
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

    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var transaction = (SqliteTransaction)
        await connection.BeginTransactionAsync(cancellationToken);
    var open = await LoadOpenAsync(
        connection,
        transaction,
        cancellationToken);

    foreach (var candidate in distinct.Values.OrderBy(
        item => item.Key,
        StringComparer.Ordinal))
    {
      var firstObservedAt = candidate.FirstObservedAt > evaluatedAt
          ? evaluatedAt
          : candidate.FirstObservedAt;
      var triggerAfter = firstObservedAt + candidate.Debounce;
      if (open.Remove(candidate.Key, out var existing))
      {
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
        await ResolveAsync(
            connection,
            transaction,
            stale.IncidentId,
            evaluatedAt,
            cancellationToken);
      }
    }

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
    await transaction.CommitAsync(cancellationToken);
  }

  public async Task<AlertIncidentPage> GetAsync(
      string tenantId,
      AlertIncidentFilter filter,
      int limit,
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
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var command = connection.CreateCommand();
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
            resolved_at
        FROM alert_incidents
        WHERE tenant_id = $tenantId
          AND {statusClause}
        ORDER BY
            COALESCE(resolved_at, triggered_at) DESC,
            incident_id DESC
        LIMIT $fetchLimit;
        """;
    command.Parameters.AddWithValue("$tenantId", tenantId);
    command.Parameters.AddWithValue("$fetchLimit", checked(limit + 1));
    var incidents = new List<AlertIncident>();
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
          row.OptionalTime("resolved_at")));
    }

    var truncated = incidents.Count > limit;
    if (truncated)
    {
      incidents.RemoveAt(incidents.Count - 1);
    }
    return new AlertIncidentPage(generatedAt, incidents, truncated);
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
    await using var transaction = (SqliteTransaction)
        await connection.BeginTransactionAsync(cancellationToken);
    await using (var command = connection.CreateCommand())
    {
      command.Transaction = transaction;
      command.CommandText =
          """
          UPDATE alert_incidents
          SET status = 'acknowledged',
              acknowledged_at = $acknowledgedAt,
              acknowledged_by_github_user_id = $acknowledgedBy,
              updated_at = $acknowledgedAt
          WHERE tenant_id = $tenantId
            AND incident_id = $incidentId
            AND status = 'triggered';
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
      if (await command.ExecuteNonQueryAsync(cancellationToken) == 1)
      {
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

  public async Task<AlertUnacknowledgeStatus> UnacknowledgeAsync(
      string tenantId,
      Guid incidentId,
      DateTimeOffset unacknowledgedAt,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var transaction = (SqliteTransaction)
        await connection.BeginTransactionAsync(cancellationToken);
    await using (var command = connection.CreateCommand())
    {
      command.Transaction = transaction;
      command.CommandText =
          """
          UPDATE alert_incidents
          SET status = 'triggered',
              acknowledged_at = NULL,
              acknowledged_by_github_user_id = NULL,
              updated_at = $now
          WHERE tenant_id = $tenantId
            AND incident_id = $incidentId
            AND status = 'acknowledged';
          """;
      command.Parameters.AddWithValue("$tenantId", tenantId);
      command.Parameters.AddWithValue(
          "$incidentId",
          incidentId.ToString("D"));
      command.Parameters.AddWithValue(
          "$now",
          Utc(unacknowledgedAt));
      if (await command.ExecuteNonQueryAsync(cancellationToken) == 1)
      {
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
            trigger_after
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
              row.Time("trigger_after")));
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
        "$firstObservedAt",
        Utc(firstObservedAt));
    command.Parameters.AddWithValue("$triggerAfter", Utc(triggerAfter));
    command.Parameters.AddWithValue("$lastObservedAt", Utc(evaluatedAt));
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
      DateTimeOffset resolvedAt,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        UPDATE alert_incidents
        SET status = 'resolved',
            resolved_at = $resolvedAt,
            updated_at = $resolvedAt
        WHERE incident_id = $incidentId
          AND status IN ('triggered', 'acknowledged');
        """;
    command.Parameters.AddWithValue("$incidentId", incidentId);
    command.Parameters.AddWithValue("$resolvedAt", Utc(resolvedAt));
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
          incident.ProfileId is null ||
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

  private sealed record OpenIncident(
      string IncidentId,
      string Key,
      string NodeId,
      string? ProfileId,
      string Kind,
      string Status,
      DateTimeOffset FirstObservedAt,
      DateTimeOffset TriggerAfter);
}
