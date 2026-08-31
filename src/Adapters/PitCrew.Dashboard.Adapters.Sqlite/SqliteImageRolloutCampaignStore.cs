using System.Globalization;
using System.Text.Json;

using Microsoft.Data.Sqlite;

using PitCrew.Dashboard.Kernel.ImageRollouts;

namespace PitCrew.Dashboard.Adapters.Sqlite;

internal sealed class SqliteImageRolloutCampaignStore(
    SqliteConnectionFactory _connectionFactory) : IImageRolloutCampaignStore
{
  private static readonly JsonSerializerOptions CampaignJsonOptions =
      new(JsonSerializerDefaults.Web);

  public async Task<ImageRolloutCampaignMutation> CreateAsync(
      ImageRolloutCampaignPlan plan,
      int maximumTargets,
      CancellationToken cancellationToken)
  {
    ArgumentOutOfRangeException.ThrowIfLessThan(maximumTargets, 1);

    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var transaction = (SqliteTransaction)
        await connection.BeginTransactionAsync(cancellationToken);
    var action = plan.Kind == ImageRolloutCampaignKind.Forward
        ? "create-forward"
        : "create-rollback";
    var replay = await ResolveIdempotencyAsync(
        connection,
        transaction,
        plan.TenantId,
        plan.RequestedByGitHubUserId,
        plan.IdempotencyKey,
        action,
        plan.IdempotencySignature,
        cancellationToken);
    if (replay is not null)
    {
      return replay;
    }
    if (plan.Targets.Count > maximumTargets)
    {
      return new ImageRolloutCampaignMutation(
          ImageRolloutCampaignMutationOutcome.TargetLimitExceeded,
          null);
    }

    var eligibleCount = plan.Targets.Count(
        static target => target.ExclusionCategory is null);
    var status = eligibleCount == 0 ? "blocked" : "draft";
    await using (var command = connection.CreateCommand())
    {
      command.Transaction = transaction;
      command.CommandText =
          """
          INSERT INTO image_rollout_campaigns (
              campaign_id,
              tenant_id,
              kind,
              source_campaign_id,
              candidate_id,
              recipe_id,
              target_digest,
              target_platform,
              target_set_hash,
              status,
              requested_by_github_user_id,
              requested_at,
              completed_at)
          VALUES (
              $campaignId,
              $tenantId,
              $kind,
              $sourceCampaignId,
              $candidateId,
              $recipeId,
              $targetDigest,
              $targetPlatform,
              $targetSetHash,
              $status,
              $requestedBy,
              $requestedAt,
              $completedAt);
          """;
      command.Parameters.AddWithValue(
          "$campaignId",
          plan.CampaignId.ToString("D"));
      command.Parameters.AddWithValue("$tenantId", plan.TenantId);
      command.Parameters.AddWithValue(
          "$kind",
          FormatCampaignKind(plan.Kind));
      command.Parameters.AddWithValue(
          "$sourceCampaignId",
          DbValue(plan.SourceCampaignId));
      command.Parameters.AddWithValue(
          "$candidateId",
          DbValue(plan.Candidate?.CandidateId));
      command.Parameters.AddWithValue(
          "$recipeId",
          DbValue(plan.Candidate?.RecipeId));
      command.Parameters.AddWithValue(
          "$targetDigest",
          DbValue(plan.Candidate?.TargetDigest));
      command.Parameters.AddWithValue(
          "$targetPlatform",
          DbValue(plan.Candidate?.TargetPlatform));
      command.Parameters.AddWithValue("$targetSetHash", plan.TargetSetHash);
      command.Parameters.AddWithValue("$status", status);
      command.Parameters.AddWithValue(
          "$requestedBy",
          plan.RequestedByGitHubUserId);
      command.Parameters.AddWithValue(
          "$requestedAt",
          FormatTimestamp(plan.RequestedAt));
      command.Parameters.AddWithValue(
          "$completedAt",
          eligibleCount == 0
              ? FormatTimestamp(plan.RequestedAt)
              : DBNull.Value);
      await command.ExecuteNonQueryAsync(cancellationToken);
    }

    if (plan.Targets.Count > 0)
    {
      await InsertTargetsAsync(
          connection,
          transaction,
          plan.CampaignId,
          plan.Targets,
          cancellationToken);
    }
    await RecordIdempotencyAsync(
        connection,
        transaction,
        plan.TenantId,
        plan.RequestedByGitHubUserId,
        plan.IdempotencyKey,
        action,
        plan.IdempotencySignature,
        plan.CampaignId,
        plan.RequestedAt,
        cancellationToken);
    var campaign = await LoadCampaignOrNullAsync(
        connection,
        transaction,
        plan.TenantId,
        plan.CampaignId,
        cancellationToken);
    await transaction.CommitAsync(cancellationToken);
    return new ImageRolloutCampaignMutation(
        ImageRolloutCampaignMutationOutcome.Succeeded,
        campaign);
  }

  public async Task<ImageRolloutCampaignMutation> ConfigureAsync(
      string tenantId,
      Guid campaignId,
      ImageRolloutCampaignConfiguration configuration,
      string actorGitHubUserId,
      string idempotencyKey,
      string idempotencySignature,
      DateTimeOffset configuredAt,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var transaction = (SqliteTransaction)
        await connection.BeginTransactionAsync(cancellationToken);
    var replay = await ResolveIdempotencyAsync(
        connection,
        transaction,
        tenantId,
        actorGitHubUserId,
        idempotencyKey,
        "configure",
        idempotencySignature,
        cancellationToken);
    if (replay is not null)
    {
      return replay;
    }

    var header = await ReadHeaderAsync(
        connection,
        transaction,
        tenantId,
        campaignId,
        cancellationToken);
    if (header is null)
    {
      return NotFound();
    }
    if (!MatchesFence(
            header,
            configuration.ExpectedRevision,
            configuration.ExpectedTargetSetHash))
    {
      return StaleFence();
    }
    if (header.Status != ImageRolloutCampaignStatus.Draft)
    {
      return InvalidState();
    }
    if (configuration.WaveSize is < 1 or >
        ImageRolloutCampaignConfiguration.MaximumWaveSize)
    {
      return InvalidState();
    }

    var targets = await LoadEligibleTargetIdentitiesAsync(
        connection,
        transaction,
        campaignId,
        cancellationToken);
    if (targets.Count == 0)
    {
      return InvalidState();
    }
    var canaryTargetId = targets.Count == 1
        ? targets[0].TargetId
        : configuration.CanaryTargetId;
    if (canaryTargetId is null ||
        !targets.Any(target => target.TargetId == canaryTargetId.Value))
    {
      return new ImageRolloutCampaignMutation(
          ImageRolloutCampaignMutationOutcome.InvalidCanary,
          null);
    }

    var assignments = new List<WaveAssignment>(targets.Count);
    assignments.Add(new WaveAssignment(canaryTargetId.Value, 0, true));
    var remaining = targets
        .Where(target => target.TargetId != canaryTargetId.Value)
        .ToArray();
    for (var index = 0; index < remaining.Length; index++)
    {
      assignments.Add(new WaveAssignment(
          remaining[index].TargetId,
          1 + index / configuration.WaveSize,
          false));
    }
    var waves = assignments
        .GroupBy(static assignment => assignment.WaveNumber)
        .OrderBy(static group => group.Key)
        .Select(group => new WaveInsert(group.Key, group.Count()))
        .ToArray();
    await AssignWavesAsync(
        connection,
        transaction,
        campaignId,
        assignments,
        waves,
        cancellationToken);

    await using (var update = connection.CreateCommand())
    {
      update.Transaction = transaction;
      update.CommandText =
          """
          UPDATE image_rollout_campaigns
          SET status = 'awaiting-approval',
              revision = revision + 1,
              wave_size = $waveSize,
              configured_by_github_user_id = $configuredBy,
              configured_at = $configuredAt
          WHERE tenant_id = $tenantId
            AND campaign_id = $campaignId
            AND status = 'draft'
            AND revision = $expectedRevision
            AND target_set_hash = $targetSetHash;
          """;
      update.Parameters.AddWithValue("$waveSize", configuration.WaveSize);
      update.Parameters.AddWithValue("$configuredBy", actorGitHubUserId);
      update.Parameters.AddWithValue(
          "$configuredAt",
          FormatTimestamp(configuredAt));
      update.Parameters.AddWithValue("$tenantId", tenantId);
      update.Parameters.AddWithValue(
          "$campaignId",
          campaignId.ToString("D"));
      update.Parameters.AddWithValue(
          "$expectedRevision",
          configuration.ExpectedRevision);
      update.Parameters.AddWithValue(
          "$targetSetHash",
          configuration.ExpectedTargetSetHash);
      if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
      {
        return StaleFence();
      }
    }

    await RecordIdempotencyAsync(
        connection,
        transaction,
        tenantId,
        actorGitHubUserId,
        idempotencyKey,
        "configure",
        idempotencySignature,
        campaignId,
        configuredAt,
        cancellationToken);
    var campaign = await LoadCampaignOrNullAsync(
        connection,
        transaction,
        tenantId,
        campaignId,
        cancellationToken);
    await transaction.CommitAsync(cancellationToken);
    return Succeeded(campaign);
  }

  public async Task<ImageRolloutCampaignMutation> ApproveWaveAsync(
      string tenantId,
      Guid campaignId,
      ImageRolloutCampaignWaveApproval approval,
      string actorGitHubUserId,
      string idempotencyKey,
      string idempotencySignature,
      DateTimeOffset approvedAt,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var transaction = (SqliteTransaction)
        await connection.BeginTransactionAsync(cancellationToken);
    var replay = await ResolveIdempotencyAsync(
        connection,
        transaction,
        tenantId,
        actorGitHubUserId,
        idempotencyKey,
        "approve-wave",
        idempotencySignature,
        cancellationToken);
    if (replay is not null)
    {
      return replay;
    }

    var header = await ReadHeaderAsync(
        connection,
        transaction,
        tenantId,
        campaignId,
        cancellationToken);
    if (header is null)
    {
      return NotFound();
    }
    if (!MatchesFence(
            header,
            approval.ExpectedRevision,
            approval.ExpectedTargetSetHash))
    {
      return StaleFence();
    }
    if (header.Status != ImageRolloutCampaignStatus.AwaitingApproval)
    {
      return InvalidState();
    }
    var nextWave = await ReadNextPendingWaveAsync(
        connection,
        transaction,
        campaignId,
        cancellationToken);
    if (nextWave != approval.WaveNumber)
    {
      return InvalidState();
    }

    await using (var command = connection.CreateCommand())
    {
      command.Transaction = transaction;
      command.CommandText =
          """
          UPDATE image_rollout_campaign_waves
          SET status = 'approved',
              approved_by_github_user_id = $approvedBy,
              approved_at = $approvedAt
          WHERE campaign_id = $campaignId
            AND wave_number = $waveNumber
            AND status = 'pending';

          UPDATE image_rollout_campaign_targets
          SET status = 'queued'
          WHERE campaign_id = $campaignId
            AND wave_number = $waveNumber
            AND status = 'eligible';

          UPDATE image_rollout_campaigns
          SET status = 'running',
              revision = revision + 1
          WHERE tenant_id = $tenantId
            AND campaign_id = $campaignId
            AND status = 'awaiting-approval'
            AND revision = $expectedRevision
            AND target_set_hash = $targetSetHash;
          """;
      command.Parameters.AddWithValue("$approvedBy", actorGitHubUserId);
      command.Parameters.AddWithValue(
          "$approvedAt",
          FormatTimestamp(approvedAt));
      command.Parameters.AddWithValue(
          "$campaignId",
          campaignId.ToString("D"));
      command.Parameters.AddWithValue("$waveNumber", approval.WaveNumber);
      command.Parameters.AddWithValue("$tenantId", tenantId);
      command.Parameters.AddWithValue(
          "$expectedRevision",
          approval.ExpectedRevision);
      command.Parameters.AddWithValue(
          "$targetSetHash",
          approval.ExpectedTargetSetHash);
      await command.ExecuteNonQueryAsync(cancellationToken);
    }

    await RecordIdempotencyAsync(
        connection,
        transaction,
        tenantId,
        actorGitHubUserId,
        idempotencyKey,
        "approve-wave",
        idempotencySignature,
        campaignId,
        approvedAt,
        cancellationToken);
    var campaign = await LoadCampaignOrNullAsync(
        connection,
        transaction,
        tenantId,
        campaignId,
        cancellationToken);
    await transaction.CommitAsync(cancellationToken);
    return Succeeded(campaign);
  }

  public Task<ImageRolloutCampaignMutation> PauseAsync(
      string tenantId,
      Guid campaignId,
      ImageRolloutCampaignMutationFence fence,
      string actorGitHubUserId,
      string idempotencyKey,
      string idempotencySignature,
      DateTimeOffset pausedAt,
      CancellationToken cancellationToken) =>
      ChangeCampaignStateAsync(
          tenantId,
          campaignId,
          fence,
          actorGitHubUserId,
          idempotencyKey,
          idempotencySignature,
          "pause",
          "paused",
          pausedAt,
          cancellationToken);

  public async Task<ImageRolloutCampaignMutation> ResumeAsync(
      string tenantId,
      Guid campaignId,
      ImageRolloutCampaignMutationFence fence,
      string actorGitHubUserId,
      string idempotencyKey,
      string idempotencySignature,
      DateTimeOffset resumedAt,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var transaction = (SqliteTransaction)
        await connection.BeginTransactionAsync(cancellationToken);
    var replay = await ResolveIdempotencyAsync(
        connection,
        transaction,
        tenantId,
        actorGitHubUserId,
        idempotencyKey,
        "resume",
        idempotencySignature,
        cancellationToken);
    if (replay is not null)
    {
      return replay;
    }
    var header = await ReadHeaderAsync(
        connection,
        transaction,
        tenantId,
        campaignId,
        cancellationToken);
    if (header is null)
    {
      return NotFound();
    }
    if (!MatchesFence(header, fence.ExpectedRevision, fence.ExpectedTargetSetHash))
    {
      return StaleFence();
    }
    if (header.Status != ImageRolloutCampaignStatus.Paused)
    {
      return InvalidState();
    }

    string? nextStatus;
    await using (var query = connection.CreateCommand())
    {
      query.Transaction = transaction;
      query.CommandText =
          """
          SELECT CASE
              WHEN EXISTS (
                  SELECT 1
                  FROM image_rollout_campaign_waves
                  WHERE campaign_id = $campaignId
                    AND status IN ('approved', 'running'))
              THEN 'running'
              WHEN EXISTS (
                  SELECT 1
                  FROM image_rollout_campaign_waves
                  WHERE campaign_id = $campaignId
                    AND status = 'pending')
              THEN 'awaiting-approval'
              ELSE NULL
          END;
          """;
      query.Parameters.AddWithValue(
          "$campaignId",
          campaignId.ToString("D"));
      nextStatus = await query.ExecuteScalarAsync(cancellationToken) as string;
    }
    if (nextStatus is null)
    {
      return InvalidState();
    }

    await using (var update = connection.CreateCommand())
    {
      update.Transaction = transaction;
      update.CommandText =
          """
          UPDATE image_rollout_campaigns
          SET status = $status,
              revision = revision + 1
          WHERE tenant_id = $tenantId
            AND campaign_id = $campaignId
            AND status = 'paused'
            AND revision = $expectedRevision
            AND target_set_hash = $targetSetHash;
          """;
      update.Parameters.AddWithValue("$status", nextStatus);
      update.Parameters.AddWithValue("$tenantId", tenantId);
      update.Parameters.AddWithValue(
          "$campaignId",
          campaignId.ToString("D"));
      update.Parameters.AddWithValue(
          "$expectedRevision",
          fence.ExpectedRevision);
      update.Parameters.AddWithValue(
          "$targetSetHash",
          fence.ExpectedTargetSetHash);
      if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
      {
        return StaleFence();
      }
    }
    await RecordIdempotencyAsync(
        connection,
        transaction,
        tenantId,
        actorGitHubUserId,
        idempotencyKey,
        "resume",
        idempotencySignature,
        campaignId,
        resumedAt,
        cancellationToken);
    var campaign = await LoadCampaignOrNullAsync(
        connection,
        transaction,
        tenantId,
        campaignId,
        cancellationToken);
    await transaction.CommitAsync(cancellationToken);
    return Succeeded(campaign);
  }

  public async Task<ImageRolloutCampaignMutation> CancelAsync(
      string tenantId,
      Guid campaignId,
      ImageRolloutCampaignMutationFence fence,
      string actorGitHubUserId,
      string idempotencyKey,
      string idempotencySignature,
      DateTimeOffset cancelledAt,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var transaction = (SqliteTransaction)
        await connection.BeginTransactionAsync(cancellationToken);
    var replay = await ResolveIdempotencyAsync(
        connection,
        transaction,
        tenantId,
        actorGitHubUserId,
        idempotencyKey,
        "cancel",
        idempotencySignature,
        cancellationToken);
    if (replay is not null)
    {
      return replay;
    }
    var header = await ReadHeaderAsync(
        connection,
        transaction,
        tenantId,
        campaignId,
        cancellationToken);
    if (header is null)
    {
      return NotFound();
    }
    if (!MatchesFence(header, fence.ExpectedRevision, fence.ExpectedTargetSetHash))
    {
      return StaleFence();
    }
    if (header.Status is
        ImageRolloutCampaignStatus.Complete or
        ImageRolloutCampaignStatus.Partial or
        ImageRolloutCampaignStatus.Blocked or
        ImageRolloutCampaignStatus.Cancelled)
    {
      return InvalidState();
    }
    if (await HasLeasedTargetAsync(
        connection,
        transaction,
        campaignId,
        cancelledAt,
        cancellationToken))
    {
      return InvalidState();
    }

    await using (var command = connection.CreateCommand())
    {
      command.Transaction = transaction;
      command.CommandText =
          """
          UPDATE image_rollout_campaign_targets
          SET status = 'cancelled',
              completed_at = $cancelledAt,
              lease_owner = NULL,
              lease_expires_at = NULL
          WHERE campaign_id = $campaignId
            AND command_id IS NULL
            AND (
                lease_owner IS NULL
                OR lease_expires_at <= $cancelledAt)
            AND status IN ('eligible', 'queued');

          UPDATE image_rollout_campaign_waves AS w
          SET status = 'cancelled',
              completed_at = $cancelledAt
          WHERE w.campaign_id = $campaignId
            AND w.status IN ('pending', 'approved', 'running')
            AND NOT EXISTS (
                SELECT 1
                FROM image_rollout_campaign_targets AS t
                WHERE t.campaign_id = w.campaign_id
                  AND t.wave_number = w.wave_number
                  AND t.command_id IS NOT NULL);

          UPDATE image_rollout_campaigns
          SET status = 'cancelled',
              revision = revision + 1,
              cancelled_at = $cancelledAt,
              completed_at = $cancelledAt
          WHERE tenant_id = $tenantId
            AND campaign_id = $campaignId
            AND revision = $expectedRevision
            AND target_set_hash = $targetSetHash;
          """;
      command.Parameters.AddWithValue(
          "$cancelledAt",
          FormatTimestamp(cancelledAt));
      command.Parameters.AddWithValue(
          "$campaignId",
          campaignId.ToString("D"));
      command.Parameters.AddWithValue("$tenantId", tenantId);
      command.Parameters.AddWithValue(
          "$expectedRevision",
          fence.ExpectedRevision);
      command.Parameters.AddWithValue(
          "$targetSetHash",
          fence.ExpectedTargetSetHash);
      await command.ExecuteNonQueryAsync(cancellationToken);
    }
    await RecordIdempotencyAsync(
        connection,
        transaction,
        tenantId,
        actorGitHubUserId,
        idempotencyKey,
        "cancel",
        idempotencySignature,
        campaignId,
        cancelledAt,
        cancellationToken);
    var campaign = await LoadCampaignOrNullAsync(
        connection,
        transaction,
        tenantId,
        campaignId,
        cancellationToken);
    await transaction.CommitAsync(cancellationToken);
    return Succeeded(campaign);
  }

  public async Task<IReadOnlyList<ImageRolloutCampaignSummary>> ListAsync(
      string tenantId,
      int limit,
      CancellationToken cancellationToken)
  {
    ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        SELECT
            c.campaign_id,
            c.kind,
            c.source_campaign_id,
            c.candidate_id,
            c.recipe_id,
            c.target_digest,
            c.target_platform,
            c.target_set_hash,
            c.status,
            c.revision,
            c.wave_size,
            SUM(CASE
                WHEN t.target_id IS NOT NULL
                 AND t.exclusion_category IS NULL
                THEN 1
                ELSE 0
            END),
            SUM(CASE WHEN t.exclusion_category IS NOT NULL THEN 1 ELSE 0 END),
            SUM(CASE WHEN t.status = 'complete' THEN 1 ELSE 0 END),
            SUM(CASE WHEN t.status IN (
                'failed',
                'blocked',
                'indeterminate') THEN 1 ELSE 0 END),
            (
                SELECT MIN(w.wave_number)
                FROM image_rollout_campaign_waves AS w
                WHERE w.campaign_id = c.campaign_id
                  AND w.status IN ('approved', 'running')),
            (
                SELECT MIN(w.wave_number)
                FROM image_rollout_campaign_waves AS w
                WHERE w.campaign_id = c.campaign_id
                  AND w.status = 'pending'),
            c.requested_by_github_user_id,
            c.requested_at,
            c.configured_at,
            c.completed_at
        FROM image_rollout_campaigns AS c
        LEFT JOIN image_rollout_campaign_targets AS t
            ON t.campaign_id = c.campaign_id
        WHERE c.tenant_id = $tenantId
        GROUP BY c.campaign_id
        ORDER BY c.requested_at DESC, c.campaign_id DESC
        LIMIT $limit;
        """;
    command.Parameters.AddWithValue("$tenantId", tenantId);
    command.Parameters.AddWithValue("$limit", limit);
    var campaigns = new List<ImageRolloutCampaignSummary>();
    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
    while (await reader.ReadAsync(cancellationToken))
    {
      campaigns.Add(new ImageRolloutCampaignSummary(
          ParseGuid(reader.GetString(0)),
          ParseCampaignKind(reader.GetString(1)),
          await ReadGuidOrNullAsync(reader, 2, cancellationToken),
          await ReadCandidateOrNullAsync(reader, 3, cancellationToken),
          reader.GetString(7),
          ParseCampaignStatus(reader.GetString(8)),
          reader.GetInt32(9),
          await ReadIntOrNullAsync(reader, 10, cancellationToken),
          reader.GetInt32(11),
          reader.GetInt32(12),
          reader.GetInt32(13),
          reader.GetInt32(14),
          await ReadIntOrNullAsync(reader, 15, cancellationToken),
          await ReadIntOrNullAsync(reader, 16, cancellationToken),
          reader.GetString(17),
          ParseTimestamp(reader.GetString(18)),
          await ReadTimestampOrNullAsync(reader, 19, cancellationToken),
          await ReadTimestampOrNullAsync(reader, 20, cancellationToken)));
    }
    return campaigns;
  }

  public async Task<ImageRolloutCampaignState?> GetOrNullAsync(
      string tenantId,
      Guid campaignId,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    return await LoadCampaignOrNullAsync(
        connection,
        transaction: null,
        tenantId,
        campaignId,
        cancellationToken);
  }

  public async Task ReconcileAsync(
      DateTimeOffset observedAt,
      int observedStateMaximumAgeSeconds,
      int maximumCampaigns,
      CancellationToken cancellationToken)
  {
    ArgumentOutOfRangeException.ThrowIfLessThan(
        observedStateMaximumAgeSeconds,
        1);
    ArgumentOutOfRangeException.ThrowIfLessThan(maximumCampaigns, 1);
    var observedAfter = observedAt.AddSeconds(
        -observedStateMaximumAgeSeconds);
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var transaction = (SqliteTransaction)
        await connection.BeginTransactionAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        UPDATE image_rollout_campaign_targets AS t
        SET status = CASE c.status
                WHEN 'queued' THEN 'queued'
                WHEN 'claimed' THEN 'claimed'
                WHEN 'started' THEN 'applying'
                WHEN 'succeeded' THEN
                    CASE WHEN c.stale_workers = 0
                              AND c.manager_convergence_status = 'current'
                         THEN 'complete'
                         ELSE 'rolling'
                    END
                WHEN 'failed' THEN 'failed'
                WHEN 'indeterminate' THEN 'indeterminate'
                ELSE 'blocked'
            END,
            failure_category = CASE
                WHEN c.status IN (
                    'failed',
                    'indeterminate',
                    'rejected',
                    'expired')
                THEN COALESCE(c.failure_category, 'unknown')
                ELSE NULL
            END,
            result_message = c.result_message,
            target_worker_revision = c.target_worker_revision,
            manager_convergence_status = c.manager_convergence_status,
            current_workers = c.current_workers,
            stale_workers = c.stale_workers,
            claimed_at = c.claimed_at,
            started_at = c.started_at,
            completed_at = c.completed_at,
            previous_candidate_id = COALESCE(
                t.previous_candidate_id,
                c.previous_candidate_id),
            previous_recipe_id = COALESCE(
                t.previous_recipe_id,
                c.previous_recipe_id),
            previous_image_reference = COALESCE(
                t.previous_image_reference,
                c.previous_image_reference),
            previous_image_digest = COALESCE(
                t.previous_image_digest,
                c.previous_image_digest),
            previous_worker_revision = COALESCE(
                t.previous_worker_revision,
                c.previous_worker_revision),
            lease_owner = NULL,
            lease_expires_at = NULL
        FROM image_rollout_commands AS c
        WHERE c.command_id = t.command_id
          AND t.campaign_id IN (
              SELECT campaign_id
              FROM image_rollout_campaigns
              WHERE status IN (
                  'awaiting-approval',
                  'running',
                  'paused',
                  'cancelled')
              ORDER BY requested_at, campaign_id
              LIMIT $maximumCampaigns)
          AND t.status IN (
              'queued',
              'claimed',
              'applying',
              'rolling');

        UPDATE image_rollout_campaign_targets AS t
        SET status = 'complete',
            manager_convergence_status = 'current',
            current_workers = (
                SELECT CAST(
                    json_extract(profile.value, '$.currentWorkers')
                    AS INTEGER)
                FROM nodes AS n,
                     json_each(
                         json_extract(
                             n.image_rollout_capability_json,
                             '$.profiles')) AS profile
                WHERE n.node_id = t.node_id
                  AND json_extract(profile.value, '$.profileId')
                      = t.profile_id
                LIMIT 1),
            stale_workers = 0
        WHERE t.status = 'rolling'
          AND t.target_worker_revision IS NOT NULL
          AND t.campaign_id IN (
              SELECT campaign_id
              FROM image_rollout_campaigns
              WHERE status IN (
                  'awaiting-approval',
                  'running',
                  'paused',
                  'cancelled')
              ORDER BY requested_at, campaign_id
              LIMIT $maximumCampaigns)
          AND EXISTS (
              SELECT 1
              FROM nodes AS n,
                   json_each(
                       json_extract(
                           n.image_rollout_capability_json,
                           '$.profiles')) AS profile
              WHERE n.node_id = t.node_id
                AND n.image_rollout_capability_at >= $observedAfter
                AND json_extract(profile.value, '$.profileId')
                    = t.profile_id
                AND json_extract(profile.value, '$.currentImageDigest')
                    = t.target_digest
                AND json_extract(profile.value, '$.currentWorkerRevision')
                    = t.target_worker_revision
                AND json_extract(
                    profile.value,
                    '$.managerConvergenceStatus') = 'current'
                AND json_extract(profile.value, '$.staleWorkers') = 0
                AND json_extract(profile.value, '$.currentWorkers')
                    IS NOT NULL
                AND (
                    json_extract(
                        profile.value,
                        '$.observedStateAgeSeconds')
                    + MAX(
                        0,
                        unixepoch($observedAt)
                        - unixepoch(n.image_rollout_capability_at))
                    <= $maximumObservedAge));

        UPDATE image_rollout_campaign_waves AS w
        SET status = 'blocked',
            completed_at = $observedAt
        WHERE w.status IN ('approved', 'running')
          AND EXISTS (
              SELECT 1
              FROM image_rollout_campaign_targets AS t
              WHERE t.campaign_id = w.campaign_id
                AND t.wave_number = w.wave_number
                AND t.status IN (
                    'failed',
                    'blocked',
                    'indeterminate'));

        UPDATE image_rollout_campaign_targets AS t
        SET status = 'blocked',
            failure_category = 'wave-blocked',
            result_message =
                'Another target in this approved wave ended adversely.',
            completed_at = $observedAt,
            lease_owner = NULL,
            lease_expires_at = NULL
        WHERE t.status = 'queued'
          AND t.command_id IS NULL
          AND EXISTS (
              SELECT 1
              FROM image_rollout_campaign_waves AS w
              WHERE w.campaign_id = t.campaign_id
                AND w.wave_number = t.wave_number
                AND w.status = 'blocked');

        UPDATE image_rollout_campaign_waves AS w
        SET status = 'complete',
            completed_at = $observedAt
        WHERE w.status IN ('approved', 'running')
          AND NOT EXISTS (
              SELECT 1
              FROM image_rollout_campaign_targets AS t
              WHERE t.campaign_id = w.campaign_id
                AND t.wave_number = w.wave_number
                AND t.status IN (
                    'eligible',
                    'queued',
                    'claimed',
                    'applying',
                    'rolling'));

        UPDATE image_rollout_campaign_waves AS w
        SET status = 'running'
        WHERE w.status = 'approved'
          AND EXISTS (
              SELECT 1
              FROM image_rollout_campaign_targets AS t
              WHERE t.campaign_id = w.campaign_id
                AND t.wave_number = w.wave_number
                AND t.command_id IS NOT NULL);

        UPDATE image_rollout_campaigns AS c
        SET status = CASE
                WHEN EXISTS (
                    SELECT 1
                    FROM image_rollout_campaign_waves AS w
                    WHERE w.campaign_id = c.campaign_id
                      AND w.status = 'blocked')
                  AND NOT EXISTS (
                    SELECT 1
                    FROM image_rollout_campaign_targets AS active
                    WHERE active.campaign_id = c.campaign_id
                      AND active.command_id IS NOT NULL
                      AND active.status IN (
                          'queued',
                          'claimed',
                          'applying',
                          'rolling'))
                THEN CASE
                    WHEN EXISTS (
                        SELECT 1
                        FROM image_rollout_campaign_targets AS t
                        WHERE t.campaign_id = c.campaign_id
                          AND t.status = 'complete')
                    THEN 'partial'
                    ELSE 'blocked'
                END
                WHEN EXISTS (
                    SELECT 1
                    FROM image_rollout_campaign_waves AS w
                    WHERE w.campaign_id = c.campaign_id
                      AND w.status = 'blocked')
                THEN CASE
                    WHEN c.status = 'paused' THEN 'paused'
                    ELSE 'running'
                END
                WHEN NOT EXISTS (
                    SELECT 1
                    FROM image_rollout_campaign_waves AS w
                    WHERE w.campaign_id = c.campaign_id
                      AND w.status <> 'complete')
                THEN 'complete'
                WHEN c.status = 'paused'
                THEN 'paused'
                WHEN EXISTS (
                    SELECT 1
                    FROM image_rollout_campaign_waves AS w
                    WHERE w.campaign_id = c.campaign_id
                      AND w.status IN ('approved', 'running'))
                THEN 'running'
                ELSE 'awaiting-approval'
            END,
            completed_at = CASE
                WHEN EXISTS (
                    SELECT 1
                    FROM image_rollout_campaign_waves AS w
                    WHERE w.campaign_id = c.campaign_id
                      AND w.status = 'blocked')
                  AND NOT EXISTS (
                    SELECT 1
                    FROM image_rollout_campaign_targets AS active
                    WHERE active.campaign_id = c.campaign_id
                      AND active.command_id IS NOT NULL
                      AND active.status IN (
                          'queued',
                          'claimed',
                          'applying',
                          'rolling'))
                  OR NOT EXISTS (
                    SELECT 1
                    FROM image_rollout_campaign_waves AS w
                    WHERE w.campaign_id = c.campaign_id
                      AND w.status <> 'complete')
                THEN $observedAt
                ELSE NULL
            END
        WHERE c.status IN ('awaiting-approval', 'running', 'paused');
        """;
    command.Parameters.AddWithValue(
        "$observedAt",
        FormatTimestamp(observedAt));
    command.Parameters.AddWithValue(
        "$observedAfter",
        FormatTimestamp(observedAfter));
    command.Parameters.AddWithValue(
        "$maximumObservedAge",
        observedStateMaximumAgeSeconds);
    command.Parameters.AddWithValue("$maximumCampaigns", maximumCampaigns);
    await command.ExecuteNonQueryAsync(cancellationToken);
    await transaction.CommitAsync(cancellationToken);
  }

  public async Task<IReadOnlyList<ImageRolloutCampaignDispatchClaim>>
      ClaimDueTargetsAsync(
          string leaseOwner,
          DateTimeOffset claimedAt,
          DateTimeOffset leaseExpiresAt,
          int maximumClaims,
          int maximumConcurrentTargetsPerCampaign,
          int maximumConcurrentTargetsPerNode,
          CancellationToken cancellationToken)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(leaseOwner);
    ArgumentOutOfRangeException.ThrowIfLessThan(maximumClaims, 1);
    ArgumentOutOfRangeException.ThrowIfLessThan(
        maximumConcurrentTargetsPerCampaign,
        1);
    ArgumentOutOfRangeException.ThrowIfLessThan(
        maximumConcurrentTargetsPerNode,
        1);

    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var transaction = (SqliteTransaction)
        await connection.BeginTransactionAsync(cancellationToken);
    var candidates = new List<DispatchCandidate>();
    await using (var command = connection.CreateCommand())
    {
      command.Transaction = transaction;
      command.CommandText =
          """
          SELECT
              t.campaign_id,
              c.tenant_id,
              t.target_id,
              t.node_id,
              t.profile_id,
              t.wave_number,
              t.candidate_id,
              t.recipe_id,
              t.target_digest,
              t.target_platform,
              t.expected_current_image_reference,
              t.expected_current_image_digest,
              t.expected_current_local_image_id,
              t.expected_current_worker_revision,
              t.expected_static_fingerprint,
              t.expected_preserved_configuration_fingerprint,
              t.expected_routing_fingerprint,
              t.expected_desired_generation,
              t.expected_desired_state_hash,
              w.approved_by_github_user_id,
              (
                  SELECT COUNT(*)
                  FROM image_rollout_campaign_targets AS active
                  WHERE active.campaign_id = t.campaign_id
                    AND (
                        (
                            active.command_id IS NOT NULL
                            AND active.status IN (
                                'queued',
                                'claimed',
                                'applying',
                                'rolling'))
                        OR (
                            active.command_id IS NULL
                            AND active.lease_owner IS NOT NULL
                            AND active.lease_expires_at > $claimedAt))),
              (
                  SELECT COUNT(*)
                  FROM image_rollout_campaign_targets AS active
                  WHERE active.node_id = t.node_id
                    AND (
                        (
                            active.command_id IS NOT NULL
                            AND active.status IN (
                                'queued',
                                'claimed',
                                'applying',
                                'rolling'))
                        OR (
                            active.command_id IS NULL
                            AND active.lease_owner IS NOT NULL
                            AND active.lease_expires_at > $claimedAt)))
          FROM image_rollout_campaign_targets AS t
          INNER JOIN image_rollout_campaigns AS c
              ON c.campaign_id = t.campaign_id
          INNER JOIN image_rollout_campaign_waves AS w
              ON w.campaign_id = t.campaign_id
             AND w.wave_number = t.wave_number
          WHERE c.status = 'running'
            AND w.status IN ('approved', 'running')
            AND t.status = 'queued'
            AND t.command_id IS NULL
            AND (t.lease_owner IS NULL OR t.lease_expires_at <= $claimedAt)
            AND (
                SELECT COUNT(*)
                FROM image_rollout_campaign_targets AS active
                WHERE active.campaign_id = t.campaign_id
                  AND (
                      (
                          active.command_id IS NOT NULL
                          AND active.status IN (
                              'queued',
                              'claimed',
                              'applying',
                              'rolling'))
                      OR (
                          active.command_id IS NULL
                          AND active.lease_owner IS NOT NULL
                          AND active.lease_expires_at > $claimedAt))
            ) < $maximumConcurrentTargetsPerCampaign
            AND (
                SELECT COUNT(*)
                FROM image_rollout_campaign_targets AS active
                WHERE active.node_id = t.node_id
                  AND (
                      (
                          active.command_id IS NOT NULL
                          AND active.status IN (
                              'queued',
                              'claimed',
                              'applying',
                              'rolling'))
                      OR (
                          active.command_id IS NULL
                          AND active.lease_owner IS NOT NULL
                          AND active.lease_expires_at > $claimedAt))
            ) < $maximumConcurrentTargetsPerNode
          ORDER BY
              c.requested_at,
              t.campaign_id,
              t.wave_number,
              t.node_id,
              t.profile_id
          LIMIT $scanLimit;
          """;
      command.Parameters.AddWithValue(
          "$claimedAt",
          FormatTimestamp(claimedAt));
      command.Parameters.AddWithValue(
          "$maximumConcurrentTargetsPerCampaign",
          maximumConcurrentTargetsPerCampaign);
      command.Parameters.AddWithValue(
          "$maximumConcurrentTargetsPerNode",
          maximumConcurrentTargetsPerNode);
      command.Parameters.AddWithValue(
          "$scanLimit",
          Math.Min(1000, maximumClaims * 20));
      await using var reader = await command.ExecuteReaderAsync(
          cancellationToken);
      while (await reader.ReadAsync(cancellationToken))
      {
        candidates.Add(await ReadDispatchCandidateAsync(
            reader,
            cancellationToken));
      }
    }

    var selected = new List<DispatchCandidate>(maximumClaims);
    var campaignCounts = new Dictionary<Guid, int>();
    var nodeCounts = new Dictionary<Guid, int>();
    foreach (var candidate in candidates)
    {
      var campaignCount = campaignCounts.TryGetValue(
          candidate.CampaignId,
          out var selectedCampaignCount)
          ? selectedCampaignCount
          : candidate.ActiveCampaignTargets;
      var nodeCount = nodeCounts.TryGetValue(
          candidate.NodeId,
          out var selectedNodeCount)
          ? selectedNodeCount
          : candidate.ActiveNodeTargets;
      if (campaignCount >= maximumConcurrentTargetsPerCampaign ||
          nodeCount >= maximumConcurrentTargetsPerNode)
      {
        continue;
      }
      selected.Add(candidate);
      campaignCounts[candidate.CampaignId] = campaignCount + 1;
      nodeCounts[candidate.NodeId] = nodeCount + 1;
      if (selected.Count == maximumClaims)
      {
        break;
      }
    }

    if (selected.Count > 0)
    {
      var targetIds = selected
          .Select(static candidate => candidate.TargetId.ToString("D"))
          .ToArray();
      await using var update = connection.CreateCommand();
      update.Transaction = transaction;
      update.CommandText =
          """
          UPDATE image_rollout_campaign_targets
          SET lease_owner = $leaseOwner,
              lease_expires_at = $leaseExpiresAt,
              dispatch_attempts = dispatch_attempts + 1
          WHERE target_id IN (
              SELECT value
              FROM json_each($targetIds))
            AND status = 'queued'
            AND command_id IS NULL
            AND (lease_owner IS NULL OR lease_expires_at <= $claimedAt);

          UPDATE image_rollout_campaign_waves
          SET status = 'running'
          WHERE status = 'approved'
            AND EXISTS (
                SELECT 1
                FROM image_rollout_campaign_targets AS t
                WHERE t.campaign_id =
                    image_rollout_campaign_waves.campaign_id
                  AND t.wave_number =
                    image_rollout_campaign_waves.wave_number
                  AND t.target_id IN (
                      SELECT value
                      FROM json_each($targetIds)));
          """;
      update.Parameters.AddWithValue("$leaseOwner", leaseOwner);
      update.Parameters.AddWithValue(
          "$leaseExpiresAt",
          FormatTimestamp(leaseExpiresAt));
      update.Parameters.AddWithValue(
          "$claimedAt",
          FormatTimestamp(claimedAt));
      update.Parameters.AddWithValue(
          "$targetIds",
          JsonSerializer.Serialize(targetIds, CampaignJsonOptions));
      await update.ExecuteNonQueryAsync(cancellationToken);
    }

    await transaction.CommitAsync(cancellationToken);
    return selected
        .Select(candidate => new ImageRolloutCampaignDispatchClaim(
            candidate.CampaignId,
            candidate.TenantId,
            candidate.TargetId,
            candidate.NodeId,
            candidate.ProfileId,
            candidate.WaveNumber,
            candidate.Candidate,
            candidate.Fences,
            candidate.ApprovedByGitHubUserId,
            $"campaign:{candidate.CampaignId:D}:{candidate.TargetId:D}"))
        .ToArray();
  }

  public async Task CompleteDispatchAsync(
      Guid campaignId,
      Guid targetId,
      string leaseOwner,
      ImageRolloutCommandQueueResult result,
      DateTimeOffset completedAt,
      CancellationToken cancellationToken)
  {
    var queued = result.Status is
        ImageRolloutCommandQueueStatus.Queued or
        ImageRolloutCommandQueueStatus.IdempotentReplay &&
        result.CommandId is not null;
    var failureCategory = queued
        ? null
        : MapQueueFailureCategory(result.Status);
    var resultMessage = queued
        ? null
        : MapQueueFailureMessage(result.Status);
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var transaction = (SqliteTransaction)
        await connection.BeginTransactionAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        UPDATE image_rollout_campaign_targets
        SET command_id = $commandId,
            status = CASE WHEN $queued = 1 THEN 'queued' ELSE 'blocked' END,
            failure_category = $failureCategory,
            result_message = $resultMessage,
            completed_at = CASE
                WHEN $queued = 1 THEN NULL
                ELSE $completedAt
            END,
            lease_owner = NULL,
            lease_expires_at = NULL
        WHERE campaign_id = $campaignId
          AND target_id = $targetId
          AND lease_owner = $leaseOwner
          AND status = 'queued'
          AND command_id IS NULL;
        """;
    command.Parameters.AddWithValue(
        "$commandId",
        queued ? result.CommandId!.Value.ToString("D") : DBNull.Value);
    command.Parameters.AddWithValue("$queued", queued ? 1 : 0);
    command.Parameters.AddWithValue(
        "$failureCategory",
        DbValue(failureCategory));
    command.Parameters.AddWithValue("$resultMessage", DbValue(resultMessage));
    command.Parameters.AddWithValue(
        "$completedAt",
        FormatTimestamp(completedAt));
    command.Parameters.AddWithValue(
        "$campaignId",
        campaignId.ToString("D"));
    command.Parameters.AddWithValue("$targetId", targetId.ToString("D"));
    command.Parameters.AddWithValue("$leaseOwner", leaseOwner);
    if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
    {
      throw new InvalidOperationException(
          "The campaign dispatch lease was not owned by this worker.");
    }
    await transaction.CommitAsync(cancellationToken);
  }

  public async Task PruneAsync(
      DateTimeOffset retainedAfter,
      int maximumCampaignsPerTenant,
      CancellationToken cancellationToken)
  {
    ArgumentOutOfRangeException.ThrowIfLessThan(
        maximumCampaignsPerTenant,
        1);
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        WITH ranked AS (
            SELECT
                campaign_id,
                tenant_id,
                ROW_NUMBER() OVER (
                    PARTITION BY tenant_id
                    ORDER BY completed_at DESC, campaign_id DESC) AS rank
            FROM image_rollout_campaigns
            WHERE status IN (
                'complete',
                'partial',
                'blocked',
                'cancelled')
        )
        DELETE FROM image_rollout_campaigns
        WHERE campaign_id IN (
            SELECT ranked.campaign_id
            FROM ranked
            WHERE ranked.rank > $maximumPerTenant
               OR EXISTS (
                   SELECT 1
                   FROM image_rollout_campaigns AS candidate
                   WHERE candidate.campaign_id = ranked.campaign_id
                     AND candidate.completed_at < $retainedAfter))
          AND NOT EXISTS (
              SELECT 1
              FROM image_rollout_campaigns AS rollback
              WHERE rollback.source_campaign_id =
                  image_rollout_campaigns.campaign_id);
        """;
    command.Parameters.AddWithValue(
        "$maximumPerTenant",
        maximumCampaignsPerTenant);
    command.Parameters.AddWithValue(
        "$retainedAfter",
        FormatTimestamp(retainedAfter));
    await command.ExecuteNonQueryAsync(cancellationToken);
  }

  private async Task<ImageRolloutCampaignMutation>
      ChangeCampaignStateAsync(
          string tenantId,
          Guid campaignId,
          ImageRolloutCampaignMutationFence fence,
          string actorGitHubUserId,
          string idempotencyKey,
          string idempotencySignature,
          string action,
          string targetStatus,
          DateTimeOffset changedAt,
          CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var transaction = (SqliteTransaction)
        await connection.BeginTransactionAsync(cancellationToken);
    var replay = await ResolveIdempotencyAsync(
        connection,
        transaction,
        tenantId,
        actorGitHubUserId,
        idempotencyKey,
        action,
        idempotencySignature,
        cancellationToken);
    if (replay is not null)
    {
      return replay;
    }
    var header = await ReadHeaderAsync(
        connection,
        transaction,
        tenantId,
        campaignId,
        cancellationToken);
    if (header is null)
    {
      return NotFound();
    }
    if (!MatchesFence(header, fence.ExpectedRevision, fence.ExpectedTargetSetHash))
    {
      return StaleFence();
    }
    if (action == "pause" &&
        header.Status is not (
            ImageRolloutCampaignStatus.Running or
            ImageRolloutCampaignStatus.AwaitingApproval))
    {
      return InvalidState();
    }
    if (action == "pause" &&
        await HasLeasedTargetAsync(
            connection,
            transaction,
            campaignId,
            changedAt,
            cancellationToken))
    {
      return InvalidState();
    }

    await using (var command = connection.CreateCommand())
    {
      command.Transaction = transaction;
      command.CommandText =
          """
          UPDATE image_rollout_campaigns
          SET status = $status,
              revision = revision + 1,
              paused_at = $changedAt
          WHERE tenant_id = $tenantId
            AND campaign_id = $campaignId
            AND revision = $expectedRevision
            AND target_set_hash = $targetSetHash;
          """;
      command.Parameters.AddWithValue("$status", targetStatus);
      command.Parameters.AddWithValue(
          "$changedAt",
          FormatTimestamp(changedAt));
      command.Parameters.AddWithValue("$tenantId", tenantId);
      command.Parameters.AddWithValue(
          "$campaignId",
          campaignId.ToString("D"));
      command.Parameters.AddWithValue(
          "$expectedRevision",
          fence.ExpectedRevision);
      command.Parameters.AddWithValue(
          "$targetSetHash",
          fence.ExpectedTargetSetHash);
      if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
      {
        return StaleFence();
      }
    }
    await RecordIdempotencyAsync(
        connection,
        transaction,
        tenantId,
        actorGitHubUserId,
        idempotencyKey,
        action,
        idempotencySignature,
        campaignId,
        changedAt,
        cancellationToken);
    var campaign = await LoadCampaignOrNullAsync(
        connection,
        transaction,
        tenantId,
        campaignId,
        cancellationToken);
    await transaction.CommitAsync(cancellationToken);
    return Succeeded(campaign);
  }

  private static async Task InsertTargetsAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      Guid campaignId,
      IReadOnlyList<ImageRolloutCampaignPlannedTarget> targets,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        INSERT INTO image_rollout_campaign_targets (
            target_id,
            campaign_id,
            node_id,
            node_display_name,
            profile_id,
            candidate_id,
            recipe_id,
            target_digest,
            target_platform,
            expected_current_image_reference,
            expected_current_image_digest,
            expected_current_local_image_id,
            expected_current_worker_revision,
            expected_static_fingerprint,
            expected_preserved_configuration_fingerprint,
            expected_routing_fingerprint,
            expected_desired_generation,
            expected_desired_state_hash,
            exclusion_category,
            status)
        SELECT
            json_extract(value, '$.targetId'),
            $campaignId,
            json_extract(value, '$.nodeId'),
            json_extract(value, '$.nodeDisplayName'),
            json_extract(value, '$.profileId'),
            json_extract(value, '$.candidate.candidateId'),
            json_extract(value, '$.candidate.recipeId'),
            json_extract(value, '$.candidate.targetDigest'),
            json_extract(value, '$.candidate.targetPlatform'),
            json_extract(value, '$.fences.expectedCurrentImageReference'),
            json_extract(value, '$.fences.expectedCurrentImageDigest'),
            json_extract(value, '$.fences.expectedCurrentLocalImageId'),
            json_extract(value, '$.fences.expectedCurrentWorkerRevision'),
            json_extract(value, '$.fences.expectedStaticFingerprint'),
            json_extract(
                value,
                '$.fences.expectedPreservedConfigurationFingerprint'),
            json_extract(value, '$.fences.expectedRoutingFingerprint'),
            json_extract(value, '$.fences.expectedDesiredGeneration'),
            json_extract(value, '$.fences.expectedDesiredStateHash'),
            json_extract(value, '$.exclusionCategory'),
            CASE
                WHEN json_extract(value, '$.exclusionCategory') IS NULL
                THEN 'eligible'
                ELSE 'excluded'
            END
        FROM json_each($targets);
        """;
    command.Parameters.AddWithValue(
        "$campaignId",
        campaignId.ToString("D"));
    command.Parameters.AddWithValue(
        "$targets",
        JsonSerializer.Serialize(targets, CampaignJsonOptions));
    await command.ExecuteNonQueryAsync(cancellationToken);
  }

  private static async Task AssignWavesAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      Guid campaignId,
      IReadOnlyList<WaveAssignment> assignments,
      IReadOnlyList<WaveInsert> waves,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        WITH assignments AS (
            SELECT
                json_extract(value, '$.targetId') AS target_id,
                json_extract(value, '$.waveNumber') AS wave_number,
                json_extract(value, '$.isCanary') AS is_canary
            FROM json_each($assignments)
        )
        UPDATE image_rollout_campaign_targets
        SET wave_number = (
                SELECT wave_number
                FROM assignments
                WHERE target_id =
                    image_rollout_campaign_targets.target_id),
            is_canary = (
                SELECT is_canary
                FROM assignments
                WHERE target_id =
                    image_rollout_campaign_targets.target_id)
        WHERE campaign_id = $campaignId
          AND target_id IN (
              SELECT target_id
              FROM assignments);

        INSERT INTO image_rollout_campaign_waves (
            campaign_id,
            wave_number,
            status,
            target_count)
        SELECT
            $campaignId,
            json_extract(value, '$.waveNumber'),
            'pending',
            json_extract(value, '$.targetCount')
        FROM json_each($waves);
        """;
    command.Parameters.AddWithValue(
        "$campaignId",
        campaignId.ToString("D"));
    command.Parameters.AddWithValue(
        "$assignments",
        JsonSerializer.Serialize(assignments, CampaignJsonOptions));
    command.Parameters.AddWithValue(
        "$waves",
        JsonSerializer.Serialize(waves, CampaignJsonOptions));
    await command.ExecuteNonQueryAsync(cancellationToken);
  }

  private static async Task<ImageRolloutCampaignMutation?>
      ResolveIdempotencyAsync(
          SqliteConnection connection,
          SqliteTransaction transaction,
          string tenantId,
          string actorGitHubUserId,
          string idempotencyKey,
          string action,
          string signature,
          CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        SELECT action, signature, campaign_id
        FROM image_rollout_campaign_idempotency
        WHERE tenant_id = $tenantId
          AND actor_github_user_id = $actor
          AND idempotency_key = $idempotencyKey;
        """;
    command.Parameters.AddWithValue("$tenantId", tenantId);
    command.Parameters.AddWithValue("$actor", actorGitHubUserId);
    command.Parameters.AddWithValue("$idempotencyKey", idempotencyKey);
    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
    if (!await reader.ReadAsync(cancellationToken))
    {
      return null;
    }
    if (!string.Equals(reader.GetString(0), action, StringComparison.Ordinal) ||
        !string.Equals(reader.GetString(1), signature, StringComparison.Ordinal))
    {
      return new ImageRolloutCampaignMutation(
          ImageRolloutCampaignMutationOutcome.IdempotencyKeyReuseConflict,
          null);
    }
    var campaignId = ParseGuid(reader.GetString(2));
    await reader.DisposeAsync();
    var campaign = await LoadCampaignOrNullAsync(
        connection,
        transaction,
        tenantId,
        campaignId,
        cancellationToken);
    if (campaign is null)
    {
      throw new InvalidDataException(
          "Campaign idempotency referenced a missing campaign.");
    }
    return new ImageRolloutCampaignMutation(
        ImageRolloutCampaignMutationOutcome.IdempotentReplay,
        campaign);
  }

  private static async Task RecordIdempotencyAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      string tenantId,
      string actorGitHubUserId,
      string idempotencyKey,
      string action,
      string signature,
      Guid campaignId,
      DateTimeOffset recordedAt,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        INSERT INTO image_rollout_campaign_idempotency (
            tenant_id,
            actor_github_user_id,
            idempotency_key,
            action,
            signature,
            campaign_id,
            recorded_at)
        VALUES (
            $tenantId,
            $actor,
            $idempotencyKey,
            $action,
            $signature,
            $campaignId,
            $recordedAt);
        """;
    command.Parameters.AddWithValue("$tenantId", tenantId);
    command.Parameters.AddWithValue("$actor", actorGitHubUserId);
    command.Parameters.AddWithValue("$idempotencyKey", idempotencyKey);
    command.Parameters.AddWithValue("$action", action);
    command.Parameters.AddWithValue("$signature", signature);
    command.Parameters.AddWithValue(
        "$campaignId",
        campaignId.ToString("D"));
    command.Parameters.AddWithValue(
        "$recordedAt",
        FormatTimestamp(recordedAt));
    await command.ExecuteNonQueryAsync(cancellationToken);
  }

  private static async Task<CampaignHeader?> ReadHeaderAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      string tenantId,
      Guid campaignId,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        SELECT status, revision, target_set_hash
        FROM image_rollout_campaigns
        WHERE tenant_id = $tenantId
          AND campaign_id = $campaignId;
        """;
    command.Parameters.AddWithValue("$tenantId", tenantId);
    command.Parameters.AddWithValue(
        "$campaignId",
        campaignId.ToString("D"));
    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
    return await reader.ReadAsync(cancellationToken)
        ? new CampaignHeader(
            ParseCampaignStatus(reader.GetString(0)),
            reader.GetInt32(1),
            reader.GetString(2))
        : null;
  }

  private static async Task<List<TargetIdentity>>
      LoadEligibleTargetIdentitiesAsync(
          SqliteConnection connection,
          SqliteTransaction transaction,
          Guid campaignId,
          CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        SELECT target_id, node_id, profile_id
        FROM image_rollout_campaign_targets
        WHERE campaign_id = $campaignId
          AND status = 'eligible'
        ORDER BY node_id, profile_id, target_id;
        """;
    command.Parameters.AddWithValue(
        "$campaignId",
        campaignId.ToString("D"));
    var targets = new List<TargetIdentity>();
    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
    while (await reader.ReadAsync(cancellationToken))
    {
      targets.Add(new TargetIdentity(
          ParseGuid(reader.GetString(0)),
          ParseGuid(reader.GetString(1)),
          reader.GetString(2)));
    }
    return targets;
  }

  private static async Task<int?> ReadNextPendingWaveAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      Guid campaignId,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        SELECT MIN(wave_number)
        FROM image_rollout_campaign_waves
        WHERE campaign_id = $campaignId
          AND status = 'pending'
          AND NOT EXISTS (
              SELECT 1
              FROM image_rollout_campaign_waves AS prior
              WHERE prior.campaign_id =
                  image_rollout_campaign_waves.campaign_id
                AND prior.wave_number <
                    image_rollout_campaign_waves.wave_number
                AND prior.status <> 'complete');
        """;
    command.Parameters.AddWithValue(
        "$campaignId",
        campaignId.ToString("D"));
    var value = await command.ExecuteScalarAsync(cancellationToken);
    return value is null or DBNull ? null : Convert.ToInt32(
        value,
        CultureInfo.InvariantCulture);
  }

  private static async Task<bool> HasLeasedTargetAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      Guid campaignId,
      DateTimeOffset activeAt,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        SELECT 1
        FROM image_rollout_campaign_targets
        WHERE campaign_id = $campaignId
          AND lease_owner IS NOT NULL
          AND lease_expires_at > $activeAt
        LIMIT 1;
        """;
    command.Parameters.AddWithValue(
        "$campaignId",
        campaignId.ToString("D"));
    command.Parameters.AddWithValue(
        "$activeAt",
        FormatTimestamp(activeAt));
    return await command.ExecuteScalarAsync(cancellationToken) is not null;
  }

  private static async Task<ImageRolloutCampaignState?> LoadCampaignOrNullAsync(
      SqliteConnection connection,
      SqliteTransaction? transaction,
      string tenantId,
      Guid campaignId,
      CancellationToken cancellationToken)
  {
    CampaignRow? row;
    await using (var command = connection.CreateCommand())
    {
      command.Transaction = transaction;
      command.CommandText =
          """
          SELECT
              campaign_id,
              tenant_id,
              kind,
              source_campaign_id,
              candidate_id,
              recipe_id,
              target_digest,
              target_platform,
              target_set_hash,
              status,
              revision,
              wave_size,
              requested_by_github_user_id,
              requested_at,
              configured_by_github_user_id,
              configured_at,
              paused_at,
              cancelled_at,
              completed_at
          FROM image_rollout_campaigns
          WHERE tenant_id = $tenantId
            AND campaign_id = $campaignId;
          """;
      command.Parameters.AddWithValue("$tenantId", tenantId);
      command.Parameters.AddWithValue(
          "$campaignId",
          campaignId.ToString("D"));
      await using var reader = await command.ExecuteReaderAsync(
          cancellationToken);
      if (!await reader.ReadAsync(cancellationToken))
      {
        return null;
      }
      row = new CampaignRow(
          ParseGuid(reader.GetString(0)),
          reader.GetString(1),
          ParseCampaignKind(reader.GetString(2)),
          await ReadGuidOrNullAsync(reader, 3, cancellationToken),
          await ReadCandidateOrNullAsync(reader, 4, cancellationToken),
          reader.GetString(8),
          ParseCampaignStatus(reader.GetString(9)),
          reader.GetInt32(10),
          await ReadIntOrNullAsync(reader, 11, cancellationToken),
          reader.GetString(12),
          ParseTimestamp(reader.GetString(13)),
          await ReadStringOrNullAsync(reader, 14, cancellationToken),
          await ReadTimestampOrNullAsync(reader, 15, cancellationToken),
          await ReadTimestampOrNullAsync(reader, 16, cancellationToken),
          await ReadTimestampOrNullAsync(reader, 17, cancellationToken),
          await ReadTimestampOrNullAsync(reader, 18, cancellationToken));
    }

    var targets = await LoadTargetsAsync(
        connection,
        transaction,
        campaignId,
        cancellationToken);
    var waves = await LoadWavesAsync(
        connection,
        transaction,
        campaignId,
        cancellationToken);
    return new ImageRolloutCampaignState(
        row.CampaignId,
        row.TenantId,
        row.Kind,
        row.SourceCampaignId,
        row.Candidate,
        row.TargetSetHash,
        row.Status,
        row.Revision,
        row.WaveSize,
        row.RequestedByGitHubUserId,
        row.RequestedAt,
        row.ConfiguredByGitHubUserId,
        row.ConfiguredAt,
        row.PausedAt,
        row.CancelledAt,
        row.CompletedAt,
        targets,
        waves);
  }

  private static async Task<IReadOnlyList<ImageRolloutCampaignTargetState>>
      LoadTargetsAsync(
          SqliteConnection connection,
          SqliteTransaction? transaction,
          Guid campaignId,
          CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        SELECT
            target_id,
            node_id,
            node_display_name,
            profile_id,
            candidate_id,
            recipe_id,
            target_digest,
            target_platform,
            expected_current_image_reference,
            expected_current_image_digest,
            expected_current_local_image_id,
            expected_current_worker_revision,
            expected_static_fingerprint,
            expected_preserved_configuration_fingerprint,
            expected_routing_fingerprint,
            expected_desired_generation,
            expected_desired_state_hash,
            exclusion_category,
            status,
            wave_number,
            is_canary,
            command_id,
            failure_category,
            result_message,
            target_worker_revision,
            manager_convergence_status,
            current_workers,
            stale_workers,
            claimed_at,
            started_at,
            completed_at,
            previous_candidate_id,
            previous_recipe_id,
            previous_image_reference,
            previous_image_digest,
            previous_worker_revision
        FROM image_rollout_campaign_targets
        WHERE campaign_id = $campaignId
        ORDER BY
            CASE WHEN exclusion_category IS NULL THEN 0 ELSE 1 END,
            wave_number,
            node_id,
            profile_id;
        """;
    command.Parameters.AddWithValue(
        "$campaignId",
        campaignId.ToString("D"));
    var targets = new List<ImageRolloutCampaignTargetState>();
    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
    while (await reader.ReadAsync(cancellationToken))
    {
      var candidate = await ReadCandidateOrNullAsync(
          reader,
          4,
          cancellationToken);
      var fences = await reader.IsDBNullAsync(12, cancellationToken)
          ? null
          : new ImageRolloutCommandFences(
              await ReadStringOrNullAsync(reader, 8, cancellationToken),
              await ReadStringOrNullAsync(reader, 9, cancellationToken),
              await ReadStringOrNullAsync(reader, 10, cancellationToken),
              await ReadStringOrNullAsync(reader, 11, cancellationToken),
              reader.GetString(12),
              reader.GetString(13),
              reader.GetString(14),
              reader.GetInt32(15),
              await ReadStringOrNullAsync(reader, 16, cancellationToken));
      targets.Add(new ImageRolloutCampaignTargetState(
          ParseGuid(reader.GetString(0)),
          ParseGuid(reader.GetString(1)),
          reader.GetString(2),
          reader.GetString(3),
          candidate,
          fences,
          await ReadStringOrNullAsync(reader, 17, cancellationToken),
          ParseTargetStatus(reader.GetString(18)),
          await ReadIntOrNullAsync(reader, 19, cancellationToken),
          reader.GetInt32(20) == 1,
          await ReadGuidOrNullAsync(reader, 21, cancellationToken),
          await ReadStringOrNullAsync(reader, 22, cancellationToken),
          await ReadStringOrNullAsync(reader, 23, cancellationToken),
          await ReadStringOrNullAsync(reader, 24, cancellationToken),
          await ReadStringOrNullAsync(reader, 25, cancellationToken),
          await ReadIntOrNullAsync(reader, 26, cancellationToken),
          await ReadIntOrNullAsync(reader, 27, cancellationToken),
          await ReadTimestampOrNullAsync(reader, 28, cancellationToken),
          await ReadTimestampOrNullAsync(reader, 29, cancellationToken),
          await ReadTimestampOrNullAsync(reader, 30, cancellationToken),
          await ReadGuidOrNullAsync(reader, 31, cancellationToken),
          await ReadStringOrNullAsync(reader, 32, cancellationToken),
          await ReadStringOrNullAsync(reader, 33, cancellationToken),
          await ReadStringOrNullAsync(reader, 34, cancellationToken),
          await ReadStringOrNullAsync(reader, 35, cancellationToken)));
    }
    return targets;
  }

  private static async Task<IReadOnlyList<ImageRolloutCampaignWaveState>>
      LoadWavesAsync(
          SqliteConnection connection,
          SqliteTransaction? transaction,
          Guid campaignId,
          CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        SELECT
            wave_number,
            status,
            target_count,
            approved_by_github_user_id,
            approved_at,
            completed_at
        FROM image_rollout_campaign_waves
        WHERE campaign_id = $campaignId
        ORDER BY wave_number;
        """;
    command.Parameters.AddWithValue(
        "$campaignId",
        campaignId.ToString("D"));
    var waves = new List<ImageRolloutCampaignWaveState>();
    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
    while (await reader.ReadAsync(cancellationToken))
    {
      waves.Add(new ImageRolloutCampaignWaveState(
          reader.GetInt32(0),
          ParseWaveStatus(reader.GetString(1)),
          reader.GetInt32(2),
          await ReadStringOrNullAsync(reader, 3, cancellationToken),
          await ReadTimestampOrNullAsync(reader, 4, cancellationToken),
          await ReadTimestampOrNullAsync(reader, 5, cancellationToken)));
    }
    return waves;
  }

  private static async Task<DispatchCandidate> ReadDispatchCandidateAsync(
      SqliteDataReader reader,
      CancellationToken cancellationToken)
  {
    var candidate = new ImageRolloutCandidateAuthority(
        ParseGuid(reader.GetString(6)),
        reader.GetString(7),
        reader.GetString(8),
        reader.GetString(9));
    var fences = new ImageRolloutCommandFences(
        await ReadStringOrNullAsync(reader, 10, cancellationToken),
        await ReadStringOrNullAsync(reader, 11, cancellationToken),
        await ReadStringOrNullAsync(reader, 12, cancellationToken),
        await ReadStringOrNullAsync(reader, 13, cancellationToken),
        reader.GetString(14),
        reader.GetString(15),
        reader.GetString(16),
        reader.GetInt32(17),
        await ReadStringOrNullAsync(reader, 18, cancellationToken));
    return new DispatchCandidate(
        ParseGuid(reader.GetString(0)),
        reader.GetString(1),
        ParseGuid(reader.GetString(2)),
        ParseGuid(reader.GetString(3)),
        reader.GetString(4),
        reader.GetInt32(5),
        candidate,
        fences,
        reader.GetString(19),
        reader.GetInt32(20),
        reader.GetInt32(21));
  }

  private static async Task<ImageRolloutCandidateAuthority?>
      ReadCandidateOrNullAsync(
      SqliteDataReader reader,
      int candidateOrdinal,
      CancellationToken cancellationToken) =>
      await reader.IsDBNullAsync(candidateOrdinal, cancellationToken)
          ? null
          : new ImageRolloutCandidateAuthority(
              ParseGuid(reader.GetString(candidateOrdinal)),
              reader.GetString(candidateOrdinal + 1),
              reader.GetString(candidateOrdinal + 2),
              reader.GetString(candidateOrdinal + 3));

  private static bool MatchesFence(
      CampaignHeader header,
      int expectedRevision,
      string expectedTargetSetHash) =>
      header.Revision == expectedRevision &&
      string.Equals(
          header.TargetSetHash,
          expectedTargetSetHash,
          StringComparison.Ordinal);

  private static ImageRolloutCampaignMutation Succeeded(
      ImageRolloutCampaignState? campaign) =>
      new(ImageRolloutCampaignMutationOutcome.Succeeded, campaign);

  private static ImageRolloutCampaignMutation NotFound() =>
      new(ImageRolloutCampaignMutationOutcome.NotFound, null);

  private static ImageRolloutCampaignMutation InvalidState() =>
      new(ImageRolloutCampaignMutationOutcome.InvalidState, null);

  private static ImageRolloutCampaignMutation StaleFence() =>
      new(ImageRolloutCampaignMutationOutcome.StaleFence, null);

  private static string MapQueueFailureCategory(
      ImageRolloutCommandQueueStatus status) =>
      status switch
      {
        ImageRolloutCommandQueueStatus.NodeNotFound => "node-not-found",
        ImageRolloutCommandQueueStatus.Unsupported => "unsupported",
        ImageRolloutCommandQueueStatus.NotAllowed => "not-allowed",
        ImageRolloutCommandQueueStatus.RecipeNotAllowed =>
            "recipe-not-allowed",
        ImageRolloutCommandQueueStatus.RegistryNotAllowed =>
            "registry-not-allowed",
        ImageRolloutCommandQueueStatus.UnsupportedTopology =>
            "unsupported-topology",
        ImageRolloutCommandQueueStatus.ArchitectureMismatch =>
            "unsupported-architecture",
        ImageRolloutCommandQueueStatus.StaleFence => "stale-fence",
        ImageRolloutCommandQueueStatus.Conflict => "operation-active",
        ImageRolloutCommandQueueStatus.RateLimited => "rate-limited",
        ImageRolloutCommandQueueStatus.IdempotencyKeyReuseConflict =>
            "idempotency-key-conflict",
        _ => "unknown",
      };

  private static string MapQueueFailureMessage(
      ImageRolloutCommandQueueStatus status) =>
      status switch
      {
        ImageRolloutCommandQueueStatus.NodeNotFound =>
            "The frozen node is no longer available.",
        ImageRolloutCommandQueueStatus.Unsupported =>
            "The frozen profile no longer advertises rollout support.",
        ImageRolloutCommandQueueStatus.NotAllowed =>
            "Local connector policy no longer allows this rollout.",
        ImageRolloutCommandQueueStatus.RecipeNotAllowed =>
            "The frozen recipe is no longer allowed for this profile.",
        ImageRolloutCommandQueueStatus.RegistryNotAllowed =>
            "Local registry policy no longer allows this recipe.",
        ImageRolloutCommandQueueStatus.UnsupportedTopology =>
            "The profile routing topology cannot be preserved.",
        ImageRolloutCommandQueueStatus.ArchitectureMismatch =>
            "The candidate architecture no longer matches the profile.",
        ImageRolloutCommandQueueStatus.StaleFence =>
            "Current profile evidence no longer matches the frozen fences.",
        ImageRolloutCommandQueueStatus.Conflict =>
            "Another profile operation is already active.",
        ImageRolloutCommandQueueStatus.RateLimited =>
            "The profile rollout cooldown has not elapsed.",
        ImageRolloutCommandQueueStatus.IdempotencyKeyReuseConflict =>
            "The stable target request key conflicts with different authority.",
        _ => "The frozen campaign target could not be queued.",
      };

  private static string FormatCampaignKind(ImageRolloutCampaignKind kind) =>
      kind switch
      {
        ImageRolloutCampaignKind.Forward => "forward",
        ImageRolloutCampaignKind.Rollback => "rollback",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
      };

  private static ImageRolloutCampaignKind ParseCampaignKind(string value) =>
      value switch
      {
        "forward" => ImageRolloutCampaignKind.Forward,
        "rollback" => ImageRolloutCampaignKind.Rollback,
        _ => throw new InvalidDataException(
            $"Unsupported image rollout campaign kind '{value}'."),
      };

  private static ImageRolloutCampaignStatus ParseCampaignStatus(string value) =>
      value switch
      {
        "draft" => ImageRolloutCampaignStatus.Draft,
        "awaiting-approval" => ImageRolloutCampaignStatus.AwaitingApproval,
        "running" => ImageRolloutCampaignStatus.Running,
        "paused" => ImageRolloutCampaignStatus.Paused,
        "complete" => ImageRolloutCampaignStatus.Complete,
        "partial" => ImageRolloutCampaignStatus.Partial,
        "blocked" => ImageRolloutCampaignStatus.Blocked,
        "cancelled" => ImageRolloutCampaignStatus.Cancelled,
        _ => throw new InvalidDataException(
            $"Unsupported image rollout campaign status '{value}'."),
      };

  private static ImageRolloutCampaignTargetStatus ParseTargetStatus(
      string value) =>
      value switch
      {
        "eligible" => ImageRolloutCampaignTargetStatus.Eligible,
        "excluded" => ImageRolloutCampaignTargetStatus.Excluded,
        "queued" => ImageRolloutCampaignTargetStatus.Queued,
        "claimed" => ImageRolloutCampaignTargetStatus.Claimed,
        "applying" => ImageRolloutCampaignTargetStatus.Applying,
        "rolling" => ImageRolloutCampaignTargetStatus.Rolling,
        "complete" => ImageRolloutCampaignTargetStatus.Complete,
        "failed" => ImageRolloutCampaignTargetStatus.Failed,
        "blocked" => ImageRolloutCampaignTargetStatus.Blocked,
        "indeterminate" => ImageRolloutCampaignTargetStatus.Indeterminate,
        "cancelled" => ImageRolloutCampaignTargetStatus.Cancelled,
        _ => throw new InvalidDataException(
            $"Unsupported image rollout campaign target status '{value}'."),
      };

  private static ImageRolloutCampaignWaveStatus ParseWaveStatus(string value) =>
      value switch
      {
        "pending" => ImageRolloutCampaignWaveStatus.Pending,
        "approved" => ImageRolloutCampaignWaveStatus.Approved,
        "running" => ImageRolloutCampaignWaveStatus.Running,
        "complete" => ImageRolloutCampaignWaveStatus.Complete,
        "blocked" => ImageRolloutCampaignWaveStatus.Blocked,
        "cancelled" => ImageRolloutCampaignWaveStatus.Cancelled,
        _ => throw new InvalidDataException(
            $"Unsupported image rollout campaign wave status '{value}'."),
      };

  private static string FormatTimestamp(DateTimeOffset value) =>
      value.ToString("O", CultureInfo.InvariantCulture);

  private static DateTimeOffset ParseTimestamp(string value) =>
      DateTimeOffset.Parse(
          value,
          CultureInfo.InvariantCulture,
          DateTimeStyles.RoundtripKind);

  private static Guid ParseGuid(string value) =>
      Guid.Parse(value, CultureInfo.InvariantCulture);

  private static object DbValue(Guid? value) =>
      value is null ? DBNull.Value : value.Value.ToString("D");

  private static object DbValue(string? value) =>
      value is null ? DBNull.Value : value;

  private static async Task<string?> ReadStringOrNullAsync(
      SqliteDataReader reader,
      int ordinal,
      CancellationToken cancellationToken) =>
      await reader.IsDBNullAsync(ordinal, cancellationToken)
          ? null
          : reader.GetString(ordinal);

  private static async Task<int?> ReadIntOrNullAsync(
      SqliteDataReader reader,
      int ordinal,
      CancellationToken cancellationToken) =>
      await reader.IsDBNullAsync(ordinal, cancellationToken)
          ? null
          : reader.GetInt32(ordinal);

  private static async Task<Guid?> ReadGuidOrNullAsync(
      SqliteDataReader reader,
      int ordinal,
      CancellationToken cancellationToken) =>
      await reader.IsDBNullAsync(ordinal, cancellationToken)
          ? null
          : ParseGuid(reader.GetString(ordinal));

  private static async Task<DateTimeOffset?> ReadTimestampOrNullAsync(
      SqliteDataReader reader,
      int ordinal,
      CancellationToken cancellationToken) =>
      await reader.IsDBNullAsync(ordinal, cancellationToken)
          ? null
          : ParseTimestamp(reader.GetString(ordinal));

  private sealed record CampaignHeader(
      ImageRolloutCampaignStatus Status,
      int Revision,
      string TargetSetHash);

  private sealed record CampaignRow(
      Guid CampaignId,
      string TenantId,
      ImageRolloutCampaignKind Kind,
      Guid? SourceCampaignId,
      ImageRolloutCandidateAuthority? Candidate,
      string TargetSetHash,
      ImageRolloutCampaignStatus Status,
      int Revision,
      int? WaveSize,
      string RequestedByGitHubUserId,
      DateTimeOffset RequestedAt,
      string? ConfiguredByGitHubUserId,
      DateTimeOffset? ConfiguredAt,
      DateTimeOffset? PausedAt,
      DateTimeOffset? CancelledAt,
      DateTimeOffset? CompletedAt);

  private sealed record TargetIdentity(
      Guid TargetId,
      Guid NodeId,
      string ProfileId);

  private sealed record WaveAssignment(
      Guid TargetId,
      int WaveNumber,
      bool IsCanary);

  private sealed record WaveInsert(
      int WaveNumber,
      int TargetCount);

  private sealed record DispatchCandidate(
      Guid CampaignId,
      string TenantId,
      Guid TargetId,
      Guid NodeId,
      string ProfileId,
      int WaveNumber,
      ImageRolloutCandidateAuthority Candidate,
      ImageRolloutCommandFences Fences,
      string ApprovedByGitHubUserId,
      int ActiveCampaignTargets,
      int ActiveNodeTargets);
}
