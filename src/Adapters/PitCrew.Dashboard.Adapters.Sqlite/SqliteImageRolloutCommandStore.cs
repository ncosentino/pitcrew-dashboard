using System.Globalization;
using System.Text.Json;

using Microsoft.Data.Sqlite;

using PitCrew.Dashboard.Features.Fleet.Abstractions;
using PitCrew.Dashboard.Kernel.ImageRollouts;
using PitCrew.Protocol;

namespace PitCrew.Dashboard.Adapters.Sqlite;

internal sealed class SqliteImageRolloutCommandStore(
    SqliteConnectionFactory _connectionFactory) : IImageRolloutCommandStore
{
  private const int MaximumHistoryPerProfile = 500;

  public async Task<ImageRolloutCommandQueueResult> QueueAsync(
      string tenantId,
      Guid nodeId,
      string profileId,
      ImageRolloutCandidateAuthority candidate,
      ImageRolloutCommandFences fences,
      string requestedByGitHubUserId,
      string idempotencyKey,
      string idempotencySignature,
      DateTimeOffset requestedAt,
      DateTimeOffset expiresAt,
      DateTimeOffset capabilityObservedAfter,
      DateTimeOffset repeatAllowedAfter,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var transaction = (SqliteTransaction)
        await connection.BeginTransactionAsync(cancellationToken);

    // Repeat the replay lookup inside the transaction to close the race
    // between the pre-candidate probe and command insertion.
    var replayLookup = await LookupIdempotentReplayCoreAsync(
        connection,
        transaction,
        tenantId,
        nodeId,
        requestedByGitHubUserId,
        idempotencyKey,
        idempotencySignature,
        cancellationToken);
    if (replayLookup.Outcome ==
        ImageRolloutIdempotencyLookupOutcome.IdempotentReplay)
    {
      return new ImageRolloutCommandQueueResult(
          ImageRolloutCommandQueueStatus.IdempotentReplay,
          replayLookup.CommandId);
    }
    if (replayLookup.Outcome ==
        ImageRolloutIdempotencyLookupOutcome.IdempotencyKeyReuseConflict)
    {
      return new ImageRolloutCommandQueueResult(
          ImageRolloutCommandQueueStatus.IdempotencyKeyReuseConflict,
          null);
    }

    ImageRolloutOperatorCapability? capability;
    DateTimeOffset? capabilityAt;
    await using (var capabilityCommand = connection.CreateCommand())
    {
      capabilityCommand.Transaction = transaction;
      capabilityCommand.CommandText =
          """
          SELECT image_rollout_capability_json, image_rollout_capability_at
          FROM nodes
          WHERE tenant_id = $tenantId
            AND node_id = $nodeId
            AND revoked_at IS NULL;
          """;
      capabilityCommand.Parameters.AddWithValue("$tenantId", tenantId);
      capabilityCommand.Parameters.AddWithValue(
          "$nodeId",
          nodeId.ToString("D"));
      await using var reader = await capabilityCommand.ExecuteReaderAsync(
          cancellationToken);
      if (!await reader.ReadAsync(cancellationToken))
      {
        return new ImageRolloutCommandQueueResult(
            ImageRolloutCommandQueueStatus.NodeNotFound,
            null);
      }
      capability = await reader.IsDBNullAsync(0, cancellationToken)
          ? null
          : JsonSerializer.Deserialize(
              reader.GetString(0),
              PitCrewProtocolJsonContext.Default.ImageRolloutOperatorCapability);
      capabilityAt = await reader.IsDBNullAsync(1, cancellationToken)
          ? null
          : DateTimeOffset.Parse(
              reader.GetString(1),
              CultureInfo.InvariantCulture,
              DateTimeStyles.RoundtripKind);
    }

    var profile = capability?.Profiles.FirstOrDefault(candidateProfile =>
        string.Equals(
            candidateProfile.ProfileId,
            profileId,
            StringComparison.OrdinalIgnoreCase));
    if (profile is null)
    {
      return new ImageRolloutCommandQueueResult(
          ImageRolloutCommandQueueStatus.Unsupported,
          null);
    }

    var maximumObservedAgeSeconds =
        (requestedAt - capabilityObservedAfter).TotalSeconds;
    if (capabilityAt is null ||
        capabilityAt < capabilityObservedAfter ||
        profile.ObservedStateAgeSeconds > maximumObservedAgeSeconds)
    {
      return new ImageRolloutCommandQueueResult(
          ImageRolloutCommandQueueStatus.StaleFence,
          null);
    }
    if (string.Equals(
            profile.LocalFailureCategory,
            "stale-observed-state",
            StringComparison.Ordinal))
    {
      return new ImageRolloutCommandQueueResult(
          ImageRolloutCommandQueueStatus.StaleFence,
          null);
    }
    if (string.Equals(
            profile.LocalFailureCategory,
            "unsupported-topology",
            StringComparison.Ordinal))
    {
      return new ImageRolloutCommandQueueResult(
          ImageRolloutCommandQueueStatus.UnsupportedTopology,
          null);
    }
    if (string.Equals(
            profile.LocalFailureCategory,
            "unsupported-architecture",
            StringComparison.Ordinal))
    {
      return new ImageRolloutCommandQueueResult(
          ImageRolloutCommandQueueStatus.ArchitectureMismatch,
          null);
    }
    if (string.Equals(
            profile.LocalFailureCategory,
            "unsupported-schema",
            StringComparison.Ordinal) ||
        string.Equals(
            profile.LocalFailureCategory,
            "unsupported-manager",
            StringComparison.Ordinal))
    {
      return new ImageRolloutCommandQueueResult(
          ImageRolloutCommandQueueStatus.Unsupported,
          null);
    }
    if (string.Equals(
            profile.LocalFailureCategory,
            "registry-not-allowed",
            StringComparison.Ordinal))
    {
      return new ImageRolloutCommandQueueResult(
          ImageRolloutCommandQueueStatus.RegistryNotAllowed,
          null);
    }
    if (!profile.AllowedRecipeIds.Any(recipeId =>
            string.Equals(
                recipeId,
                candidate.RecipeId,
                StringComparison.OrdinalIgnoreCase)))
    {
      return new ImageRolloutCommandQueueResult(
          ImageRolloutCommandQueueStatus.RecipeNotAllowed,
          null);
    }
    if (!profile.LocalSchemaSupported ||
        !profile.RolloutAllowed ||
        profile.LocalFailureCategory is not null)
    {
      return new ImageRolloutCommandQueueResult(
          ImageRolloutCommandQueueStatus.NotAllowed,
          null);
    }
    if (!string.Equals(
            profile.Architecture,
            candidate.TargetPlatform,
            StringComparison.Ordinal))
    {
      return new ImageRolloutCommandQueueResult(
          ImageRolloutCommandQueueStatus.ArchitectureMismatch,
          null);
    }
    if (profile.OperationActive ||
        await SqliteProfileOperationSlot.IsHeldAsync(
            connection,
            transaction,
            nodeId,
            profile.ProfileId,
            cancellationToken))
    {
      return new ImageRolloutCommandQueueResult(
          ImageRolloutCommandQueueStatus.Conflict,
          null);
    }
    if (!string.Equals(
            profile.StaticFingerprint,
            fences.ExpectedStaticFingerprint,
            StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(
            profile.PreservedConfigurationFingerprint,
            fences.ExpectedPreservedConfigurationFingerprint,
            StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(
            profile.RoutingFingerprint,
            fences.ExpectedRoutingFingerprint,
            StringComparison.OrdinalIgnoreCase) ||
        profile.DesiredGeneration != fences.ExpectedDesiredGeneration ||
        !string.Equals(
            profile.DesiredStateHash,
            fences.ExpectedDesiredStateHash,
            StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(
            profile.CurrentImageReference,
            fences.ExpectedCurrentImageReference,
            StringComparison.Ordinal) ||
        !string.Equals(
            profile.CurrentImageDigest,
            fences.ExpectedCurrentImageDigest,
            StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(
            profile.CurrentLocalImageId,
            fences.ExpectedCurrentLocalImageId,
            StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(
            profile.CurrentWorkerRevision,
            fences.ExpectedCurrentWorkerRevision,
            StringComparison.OrdinalIgnoreCase))
    {
      return new ImageRolloutCommandQueueResult(
          ImageRolloutCommandQueueStatus.StaleFence,
          null);
    }

    await using (var recentCommand = connection.CreateCommand())
    {
      recentCommand.Transaction = transaction;
      recentCommand.CommandText =
          """
          SELECT 1
          FROM image_rollout_commands
          WHERE node_id = $nodeId
            AND profile_id = $profileId
            AND requested_at > $repeatAllowedAfter
          LIMIT 1;
          """;
      recentCommand.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
      recentCommand.Parameters.AddWithValue("$profileId", profile.ProfileId);
      recentCommand.Parameters.AddWithValue(
          "$repeatAllowedAfter",
          repeatAllowedAfter.ToString("O", CultureInfo.InvariantCulture));
      if (await recentCommand.ExecuteScalarAsync(cancellationToken) is not null)
      {
        return new ImageRolloutCommandQueueResult(
            ImageRolloutCommandQueueStatus.RateLimited,
            null);
      }
    }

    var localExpiry = requestedAt.AddSeconds(profile.MaximumExpirySeconds);
    var effectiveExpiry = expiresAt < localExpiry
        ? expiresAt
        : localExpiry;
    var commandId = Guid.NewGuid();

    Guid? previousCandidateId = null;
    string? previousRecipeId = null;
    string? previousImageDigest = null;
    string? previousImageReference = null;
    string? previousWorkerRevision = null;
    // Prior authority requires both the applied digest and worker revision;
    // either value alone can survive an unrelated profile change.
    if (fences.ExpectedCurrentImageDigest is not null &&
        fences.ExpectedCurrentWorkerRevision is not null)
    {
      await using var priorCommand = connection.CreateCommand();
      priorCommand.Transaction = transaction;
      priorCommand.CommandText =
          """
          SELECT candidate_id, recipe_id, target_digest, target_worker_revision
          FROM image_rollout_commands
          WHERE node_id = $nodeId
            AND profile_id = $profileId
            AND status = 'succeeded'
            AND target_digest = $currentDigest
            AND target_worker_revision = $currentRevision
          ORDER BY completed_at DESC
          LIMIT 1;
          """;
      priorCommand.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
      priorCommand.Parameters.AddWithValue("$profileId", profile.ProfileId);
      priorCommand.Parameters.AddWithValue(
          "$currentDigest",
          fences.ExpectedCurrentImageDigest);
      priorCommand.Parameters.AddWithValue(
          "$currentRevision",
          fences.ExpectedCurrentWorkerRevision);
      await using var priorReader = await priorCommand.ExecuteReaderAsync(
          cancellationToken);
      if (await priorReader.ReadAsync(cancellationToken))
      {
        previousCandidateId = Guid.Parse(
            priorReader.GetString(0),
            CultureInfo.InvariantCulture);
        previousRecipeId = priorReader.GetString(1);
        previousImageDigest = priorReader.GetString(2);
        previousWorkerRevision = await priorReader.IsDBNullAsync(
                3,
                cancellationToken)
            ? null
            : priorReader.GetString(3);
        previousImageReference = fences.ExpectedCurrentImageReference;
      }
    }

    if (!await SqliteProfileOperationSlot.AcquireAsync(
        connection,
        transaction,
        nodeId,
        profile.ProfileId,
        SqliteProfileOperationSlot.ImageRolloutKind,
        commandId,
        requestedAt,
        cancellationToken))
    {
      return new ImageRolloutCommandQueueResult(
          ImageRolloutCommandQueueStatus.Conflict,
          null);
    }

    await using (var insert = connection.CreateCommand())
    {
      insert.Transaction = transaction;
      insert.CommandText =
          """
          INSERT INTO image_rollout_commands (
              command_id,
              node_id,
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
              previous_image_reference,
              previous_image_digest,
              previous_worker_revision,
              previous_candidate_id,
              previous_recipe_id,
              status,
              requested_by_github_user_id,
              requested_at,
              expires_at,
              idempotency_key,
              idempotency_signature)
          VALUES (
              $commandId,
              $nodeId,
              $profileId,
              $candidateId,
              $recipeId,
              $targetDigest,
              $targetPlatform,
              $expectedCurrentImageReference,
              $expectedCurrentImageDigest,
              $expectedCurrentLocalImageId,
              $expectedCurrentWorkerRevision,
              $expectedStaticFingerprint,
              $expectedPreservedConfigurationFingerprint,
              $expectedRoutingFingerprint,
              $expectedDesiredGeneration,
              $expectedDesiredStateHash,
              $previousImageReference,
              $previousImageDigest,
              $previousWorkerRevision,
              $previousCandidateId,
              $previousRecipeId,
              'queued',
              $requestedBy,
              $requestedAt,
              $expiresAt,
              $idempotencyKey,
              $idempotencySignature);
          """;
      insert.Parameters.AddWithValue("$commandId", commandId.ToString("D"));
      insert.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
      insert.Parameters.AddWithValue("$profileId", profile.ProfileId);
      insert.Parameters.AddWithValue(
          "$candidateId",
          candidate.CandidateId.ToString("D"));
      insert.Parameters.AddWithValue("$recipeId", candidate.RecipeId);
      insert.Parameters.AddWithValue("$targetDigest", candidate.TargetDigest);
      insert.Parameters.AddWithValue(
          "$targetPlatform",
          candidate.TargetPlatform);
      insert.Parameters.AddWithValue(
          "$expectedCurrentImageReference",
          fences.ExpectedCurrentImageReference is null
              ? DBNull.Value
              : fences.ExpectedCurrentImageReference);
      insert.Parameters.AddWithValue(
          "$expectedCurrentImageDigest",
          fences.ExpectedCurrentImageDigest is null
              ? DBNull.Value
              : fences.ExpectedCurrentImageDigest);
      insert.Parameters.AddWithValue(
          "$expectedCurrentLocalImageId",
          fences.ExpectedCurrentLocalImageId is null
              ? DBNull.Value
              : fences.ExpectedCurrentLocalImageId);
      insert.Parameters.AddWithValue(
          "$expectedCurrentWorkerRevision",
          fences.ExpectedCurrentWorkerRevision is null
              ? DBNull.Value
              : fences.ExpectedCurrentWorkerRevision);
      insert.Parameters.AddWithValue(
          "$expectedStaticFingerprint",
          fences.ExpectedStaticFingerprint);
      insert.Parameters.AddWithValue(
          "$expectedPreservedConfigurationFingerprint",
          fences.ExpectedPreservedConfigurationFingerprint);
      insert.Parameters.AddWithValue(
          "$expectedRoutingFingerprint",
          fences.ExpectedRoutingFingerprint);
      insert.Parameters.AddWithValue(
          "$expectedDesiredGeneration",
          fences.ExpectedDesiredGeneration);
      insert.Parameters.AddWithValue(
          "$expectedDesiredStateHash",
          fences.ExpectedDesiredStateHash is null
              ? DBNull.Value
              : fences.ExpectedDesiredStateHash);
      insert.Parameters.AddWithValue(
          "$previousImageReference",
          previousImageReference is null
              ? DBNull.Value
              : previousImageReference);
      insert.Parameters.AddWithValue(
          "$previousImageDigest",
          previousImageDigest is null
              ? DBNull.Value
              : previousImageDigest);
      insert.Parameters.AddWithValue(
          "$previousWorkerRevision",
          previousWorkerRevision is null
              ? DBNull.Value
              : previousWorkerRevision);
      insert.Parameters.AddWithValue(
          "$previousCandidateId",
          previousCandidateId is null
              ? DBNull.Value
              : previousCandidateId.Value.ToString("D"));
      insert.Parameters.AddWithValue(
          "$previousRecipeId",
          previousRecipeId is null
              ? DBNull.Value
              : previousRecipeId);
      insert.Parameters.AddWithValue(
          "$requestedBy",
          requestedByGitHubUserId);
      insert.Parameters.AddWithValue(
          "$requestedAt",
          requestedAt.ToString("O", CultureInfo.InvariantCulture));
      insert.Parameters.AddWithValue(
          "$expiresAt",
          effectiveExpiry.ToString("O", CultureInfo.InvariantCulture));
      insert.Parameters.AddWithValue(
          "$idempotencyKey",
          idempotencyKey);
      insert.Parameters.AddWithValue(
          "$idempotencySignature",
          idempotencySignature);
      await insert.ExecuteNonQueryAsync(cancellationToken);
    }

    await transaction.CommitAsync(cancellationToken);
    return new ImageRolloutCommandQueueResult(
        ImageRolloutCommandQueueStatus.Queued,
        commandId);
  }

  public async Task<ImageRolloutIdempotencyLookup> LookupIdempotentReplayAsync(
      string tenantId,
      Guid nodeId,
      string requestedByGitHubUserId,
      string idempotencyKey,
      string idempotencySignature,
      CancellationToken cancellationToken)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
    ArgumentException.ThrowIfNullOrWhiteSpace(requestedByGitHubUserId);
    ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
    ArgumentException.ThrowIfNullOrWhiteSpace(idempotencySignature);

    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    return await LookupIdempotentReplayCoreAsync(
        connection,
        transaction: null,
        tenantId,
        nodeId,
        requestedByGitHubUserId,
        idempotencyKey,
        idempotencySignature,
        cancellationToken);
  }

  private static async Task<ImageRolloutIdempotencyLookup>
      LookupIdempotentReplayCoreAsync(
          SqliteConnection connection,
          SqliteTransaction? transaction,
          string tenantId,
          Guid nodeId,
          string requestedByGitHubUserId,
          string idempotencyKey,
          string idempotencySignature,
          CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        SELECT irc.command_id, irc.idempotency_signature
        FROM image_rollout_commands irc
        INNER JOIN nodes n ON n.node_id = irc.node_id
        WHERE irc.node_id = $nodeId
          AND irc.requested_by_github_user_id = $requestedBy
          AND irc.idempotency_key = $idempotencyKey
          AND n.tenant_id = $tenantId
          AND n.revoked_at IS NULL
        LIMIT 1;
        """;
    command.Parameters.AddWithValue("$tenantId", tenantId);
    command.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
    command.Parameters.AddWithValue("$requestedBy", requestedByGitHubUserId);
    command.Parameters.AddWithValue("$idempotencyKey", idempotencyKey);
    await using var reader = await command.ExecuteReaderAsync(
        cancellationToken);
    if (await reader.ReadAsync(cancellationToken))
    {
      var existingCommandId = Guid.Parse(
          reader.GetString(0),
          CultureInfo.InvariantCulture);
      var existingSignature = reader.GetString(1);
      if (string.Equals(
              existingSignature,
              idempotencySignature,
              StringComparison.Ordinal))
      {
        return new ImageRolloutIdempotencyLookup(
            ImageRolloutIdempotencyLookupOutcome.IdempotentReplay,
            existingCommandId);
      }
      return new ImageRolloutIdempotencyLookup(
          ImageRolloutIdempotencyLookupOutcome.IdempotencyKeyReuseConflict,
          null);
    }
    return new ImageRolloutIdempotencyLookup(
        ImageRolloutIdempotencyLookupOutcome.NoExistingCommand,
        null);
  }

  public async Task<RollOutProfileImageCommand?> ApplyConnectorSyncAsync(
      Guid nodeId,
      ImageRolloutOperatorCapability? capability,
      ImageRolloutCommandProgress? progress,
      ImageRolloutCommandOutcome? outcome,
      DateTimeOffset receivedAt,
      DateTimeOffset redeliverBefore,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var transaction = (SqliteTransaction)
        await connection.BeginTransactionAsync(cancellationToken);

    await using (var capabilityCommand = connection.CreateCommand())
    {
      capabilityCommand.Transaction = transaction;
      capabilityCommand.CommandText =
          """
          UPDATE nodes
          SET image_rollout_capability_json = $capability,
              image_rollout_capability_at = $receivedAt
          WHERE node_id = $nodeId
            AND revoked_at IS NULL;
          """;
      capabilityCommand.Parameters.AddWithValue(
          "$capability",
          capability is null
              ? DBNull.Value
              : JsonSerializer.Serialize(
                  capability,
                  PitCrewProtocolJsonContext.Default.ImageRolloutOperatorCapability));
      capabilityCommand.Parameters.AddWithValue(
          "$receivedAt",
          receivedAt.ToString("O", CultureInfo.InvariantCulture));
      capabilityCommand.Parameters.AddWithValue(
          "$nodeId",
          nodeId.ToString("D"));
      if (await capabilityCommand.ExecuteNonQueryAsync(cancellationToken) != 1)
      {
        throw new InvalidOperationException(
            $"Node '{nodeId}' was not available for image rollout synchronization.");
      }
    }

    if (progress is not null)
    {
      await ApplyProgressAsync(
          connection,
          transaction,
          nodeId,
          progress,
          cancellationToken);
    }
    if (outcome is not null)
    {
      await ApplyOutcomeAsync(
          connection,
          transaction,
          nodeId,
          outcome,
          cancellationToken);
    }
    await ApplyExpiryAsync(
        connection,
        transaction,
        nodeId,
        receivedAt,
        cancellationToken);

    var offered = await OfferAsync(
        connection,
        transaction,
        nodeId,
        capability,
        receivedAt,
        redeliverBefore,
        cancellationToken);
    await SqliteProfileOperationSlot.ReleaseCompletedAsync(
        connection,
        transaction,
        nodeId,
        cancellationToken);
    await transaction.CommitAsync(cancellationToken);
    return offered;
  }

  public async Task<IReadOnlyList<NodeImageRolloutControls>> GetControlsAsync(
      string tenantId,
      int observedStateMaximumAgeSeconds,
      CancellationToken cancellationToken,
      int historyPerProfile = 20)
  {
    ValidateHistoryLimit(historyPerProfile);
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    var capabilities = new Dictionary<
        Guid,
        (ImageRolloutOperatorCapability Capability, DateTimeOffset ObservedAt)>();
    await using (var capabilityCommand = connection.CreateCommand())
    {
      capabilityCommand.CommandText =
          """
          SELECT
              node_id,
              image_rollout_capability_json,
              image_rollout_capability_at
          FROM nodes
          WHERE tenant_id = $tenantId
            AND revoked_at IS NULL
            AND image_rollout_capability_json IS NOT NULL
            AND image_rollout_capability_at IS NOT NULL;
          """;
      capabilityCommand.Parameters.AddWithValue("$tenantId", tenantId);
      await using var reader = await capabilityCommand.ExecuteReaderAsync(
          cancellationToken);
      while (await reader.ReadAsync(cancellationToken))
      {
        var capability = JsonSerializer.Deserialize(
            reader.GetString(1),
            PitCrewProtocolJsonContext.Default.ImageRolloutOperatorCapability);
        if (capability is not null)
        {
          capabilities[Guid.Parse(
              reader.GetString(0),
              CultureInfo.InvariantCulture)] = (
                  capability,
                  ReadTimestamp(reader.GetString(2)));
        }
      }
    }

    var commands = await LoadCommandStatesAsync(
        connection,
        tenantId,
        capabilities.Keys,
        null,
        historyPerProfile,
        cancellationToken);

    return capabilities
        .OrderBy(pair => pair.Key)
        .Select(pair => new NodeImageRolloutControls(
            pair.Key,
            pair.Value.Capability.Profiles
                .OrderBy(profile => profile.ProfileId, StringComparer.Ordinal)
                .Select(profile => BuildControlState(
                    profile,
                    pair.Value.ObservedAt,
                    observedStateMaximumAgeSeconds,
                    commands.TryGetValue(
                        (pair.Key, profile.ProfileId),
                        out var history)
                        ? history
                        : []))
                .ToArray()))
        .ToArray();
  }

  public async Task<ImageRolloutControlState?> GetProfileControlOrNullAsync(
      string tenantId,
      Guid nodeId,
      string profileId,
      int observedStateMaximumAgeSeconds,
      CancellationToken cancellationToken,
      int historyPerProfile = 20)
  {
    ValidateHistoryLimit(historyPerProfile);
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    ImageRolloutOperatorCapability? capability = null;
    DateTimeOffset? capabilityObservedAt = null;
    await using (var capabilityCommand = connection.CreateCommand())
    {
      capabilityCommand.CommandText =
          """
          SELECT
              image_rollout_capability_json,
              image_rollout_capability_at
          FROM nodes
          WHERE tenant_id = $tenantId
            AND node_id = $nodeId
            AND revoked_at IS NULL
            AND image_rollout_capability_json IS NOT NULL
            AND image_rollout_capability_at IS NOT NULL;
          """;
      capabilityCommand.Parameters.AddWithValue("$tenantId", tenantId);
      capabilityCommand.Parameters.AddWithValue(
          "$nodeId",
          nodeId.ToString("D"));
      await using var reader = await capabilityCommand.ExecuteReaderAsync(
          cancellationToken);
      if (await reader.ReadAsync(cancellationToken))
      {
        capability = JsonSerializer.Deserialize(
            reader.GetString(0),
            PitCrewProtocolJsonContext.Default.ImageRolloutOperatorCapability);
        capabilityObservedAt = ReadTimestamp(reader.GetString(1));
      }
    }
    if (capability is null || capabilityObservedAt is null)
    {
      return null;
    }
    var profile = capability.Profiles.FirstOrDefault(candidate =>
        string.Equals(
            candidate.ProfileId,
            profileId,
            StringComparison.OrdinalIgnoreCase));
    if (profile is null)
    {
      return null;
    }
    var commands = await LoadCommandStatesAsync(
        connection,
        tenantId,
        [nodeId],
        profile.ProfileId,
        historyPerProfile,
        cancellationToken);
    return BuildControlState(
        profile,
        capabilityObservedAt.Value,
        observedStateMaximumAgeSeconds,
        commands.TryGetValue((nodeId, profile.ProfileId), out var history)
            ? history
            : []);
  }

  private static ImageRolloutControlState BuildControlState(
      ImageRolloutOperatorProfile profile,
      DateTimeOffset capabilityObservedAt,
      int observedStateMaximumAgeSeconds,
      IReadOnlyList<ImageRolloutCommandState> history) =>
      new(
          profile.ProfileId,
          profile.Architecture,
          profile.CurrentImageReference,
          profile.CurrentImageDigest,
          profile.CurrentLocalImageId,
          profile.CurrentWorkerRevision,
          profile.StaticFingerprint,
          profile.PreservedConfigurationFingerprint,
          profile.RoutingFingerprint,
          profile.DesiredGeneration,
          profile.DesiredStateHash,
          profile.AllowedRecipeIds,
          profile.RolloutAllowed,
          profile.LocalSchemaSupported,
          profile.LocalFailureCategory,
          profile.OperationActive,
          profile.ObservedStateAgeSeconds,
          capabilityObservedAt,
          observedStateMaximumAgeSeconds,
          profile.ManagerConvergenceStatus,
          profile.CurrentWorkers,
          profile.StaleWorkers,
          history.Count > 0 ? history[0] : null,
          history);

  private static async Task<Dictionary<(Guid NodeId, string ProfileId), List<ImageRolloutCommandState>>>
      LoadCommandStatesAsync(
          SqliteConnection connection,
          string tenantId,
          IEnumerable<Guid> nodeIds,
          string? profileFilter,
          int historyPerProfile,
          CancellationToken cancellationToken)
  {
    var commands =
        new Dictionary<(Guid NodeId, string ProfileId), List<ImageRolloutCommandState>>();
    var nodeIdList = nodeIds.Distinct().ToArray();
    if (nodeIdList.Length == 0)
    {
      return commands;
    }
    await using var commandQuery = connection.CreateCommand();
    var nodeParameterNames = nodeIdList
        .Select((_, index) => "$node" + index.ToString(CultureInfo.InvariantCulture))
        .ToArray();
    var nodeFilter = string.Join(", ", nodeParameterNames);
    var profileClause = profileFilter is null
        ? string.Empty
        : " AND c.profile_id = $profileFilter";
    commandQuery.CommandText =
        $$"""
        SELECT
            c.node_id,
            c.profile_id,
            c.command_id,
            c.candidate_id,
            c.recipe_id,
            c.target_digest,
            c.target_platform,
            c.previous_image_reference,
            c.previous_image_digest,
            c.previous_worker_revision,
            c.status,
            c.failure_category,
            c.requested_by_github_user_id,
            c.requested_at,
            c.expires_at,
            c.delivered_at,
            c.claimed_at,
            c.started_at,
            c.completed_at,
            c.target_worker_revision,
            c.manager_convergence_status,
            c.current_workers,
            c.stale_workers,
            c.last_error,
            c.result_message,
            c.previous_candidate_id,
            c.previous_recipe_id
        FROM image_rollout_commands AS c
        INNER JOIN nodes AS n ON n.node_id = c.node_id
        WHERE n.tenant_id = $tenantId
          AND n.revoked_at IS NULL
          AND c.node_id IN ({{nodeFilter}}){{profileClause}}
        ORDER BY c.requested_at DESC;
        """;
    commandQuery.Parameters.AddWithValue("$tenantId", tenantId);
    for (var index = 0; index < nodeIdList.Length; index++)
    {
      commandQuery.Parameters.AddWithValue(
          nodeParameterNames[index],
          nodeIdList[index].ToString("D"));
    }
    if (profileFilter is not null)
    {
      commandQuery.Parameters.AddWithValue("$profileFilter", profileFilter);
    }
    await using var reader = await commandQuery.ExecuteReaderAsync(
        cancellationToken);
    while (await reader.ReadAsync(cancellationToken))
    {
      var nodeId = Guid.Parse(
          reader.GetString(0),
          CultureInfo.InvariantCulture);
      var key = (nodeId, reader.GetString(1));
      if (!commands.TryGetValue(key, out var history))
      {
        history = [];
        commands[key] = history;
      }
      if (history.Count >= historyPerProfile)
      {
        continue;
      }
      history.Add(new ImageRolloutCommandState(
          Guid.Parse(
              reader.GetString(2),
              CultureInfo.InvariantCulture),
          Guid.Parse(
              reader.GetString(3),
              CultureInfo.InvariantCulture),
          reader.GetString(4),
          reader.GetString(5),
          reader.GetString(6),
          await ReadStringOrNullAsync(reader, 7, cancellationToken),
          await ReadStringOrNullAsync(reader, 8, cancellationToken),
          await ReadStringOrNullAsync(reader, 9, cancellationToken),
          reader.GetString(10),
          await ReadStringOrNullAsync(reader, 11, cancellationToken),
          reader.GetString(12),
          ReadTimestamp(reader.GetString(13)),
          ReadTimestamp(reader.GetString(14)),
          await ReadTimestampOrNullAsync(reader, 15, cancellationToken),
          await ReadTimestampOrNullAsync(reader, 16, cancellationToken),
          await ReadTimestampOrNullAsync(reader, 17, cancellationToken),
          await ReadTimestampOrNullAsync(reader, 18, cancellationToken),
          await ReadStringOrNullAsync(reader, 19, cancellationToken),
          await ReadStringOrNullAsync(reader, 20, cancellationToken),
          await ReadIntOrNullAsync(reader, 21, cancellationToken),
          await ReadIntOrNullAsync(reader, 22, cancellationToken),
          await ReadStringOrNullAsync(reader, 23, cancellationToken),
          await ReadStringOrNullAsync(reader, 24, cancellationToken),
          await ReadGuidOrNullAsync(reader, 25, cancellationToken),
          await ReadStringOrNullAsync(reader, 26, cancellationToken)));
    }
    return commands;
  }

  private static void ValidateHistoryLimit(int historyPerProfile)
  {
    if (historyPerProfile is < 1 or > MaximumHistoryPerProfile)
    {
      throw new ArgumentOutOfRangeException(
          nameof(historyPerProfile),
          historyPerProfile,
          $"History per profile must be between 1 and {MaximumHistoryPerProfile}.");
    }
  }

  private static async Task ApplyProgressAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      Guid nodeId,
      ImageRolloutCommandProgress progress,
      CancellationToken cancellationToken)
  {
    const string claimSql =
        """
        UPDATE image_rollout_commands
        SET status = 'claimed',
            claimed_at = $reportedAt
        WHERE command_id = $commandId
          AND node_id = $nodeId
          AND status = 'queued';
        """;
    const string startSql =
        """
        UPDATE image_rollout_commands
        SET status = 'started',
            claimed_at = COALESCE(claimed_at, $reportedAt),
            started_at = $reportedAt
        WHERE command_id = $commandId
          AND node_id = $nodeId
          AND status IN ('queued', 'claimed');
        """;

    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = string.Equals(
            progress.Phase,
            "claimed",
            StringComparison.Ordinal)
        ? claimSql
        : startSql;
    command.Parameters.AddWithValue(
        "$reportedAt",
        progress.ReportedAt.ToString("O", CultureInfo.InvariantCulture));
    command.Parameters.AddWithValue(
        "$commandId",
        progress.CommandId.ToString("D"));
    command.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
    await command.ExecuteNonQueryAsync(cancellationToken);
  }

  private static async Task ApplyOutcomeAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      Guid nodeId,
      ImageRolloutCommandOutcome outcome,
      CancellationToken cancellationToken)
  {
    var effectiveStatus = outcome.Status;
    var effectiveCategory = outcome.FailureCategory;
    var effectiveMessage = outcome.Message;
    var effectiveWorkerRevision = outcome.TargetWorkerRevision;
    string? effectiveConvergence = outcome.ManagerConvergenceStatus;
    var effectiveCurrentWorkers = outcome.CurrentWorkers;
    var effectiveStaleWorkers = outcome.StaleWorkers;
    var effectiveLastError = outcome.LastError;

    if (string.Equals(outcome.Status, "succeeded", StringComparison.Ordinal))
    {
      var storedTargetDigest = await ReadActiveRowTargetDigestAsync(
          connection,
          transaction,
          nodeId,
          outcome.CommandId,
          cancellationToken);
      var authorityMatches = storedTargetDigest is not null
          && string.Equals(
              outcome.TargetDigest,
              storedTargetDigest,
              StringComparison.Ordinal)
          && !string.IsNullOrEmpty(outcome.TargetWorkerRevision);
      if (!authorityMatches)
      {
        effectiveStatus = "indeterminate";
        effectiveCategory = "unknown";
        effectiveMessage =
            "Success outcome authority did not match the queued command target.";
        effectiveWorkerRevision = null;
        effectiveConvergence = null;
        effectiveCurrentWorkers = null;
        effectiveStaleWorkers = null;
        effectiveLastError = null;
      }
    }

    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        UPDATE image_rollout_commands
        SET status = $status,
            failure_category = $failureCategory,
            completed_at = $completedAt,
            claimed_at = COALESCE(claimed_at, $completedAt),
            started_at = COALESCE(
                started_at,
                CASE WHEN $status = 'succeeded' THEN $completedAt END),
            target_worker_revision = $targetWorkerRevision,
            manager_convergence_status = $managerConvergence,
            current_workers = $currentWorkers,
            stale_workers = $staleWorkers,
            last_error = $lastError,
            result_message = $message
        WHERE command_id = $commandId
          AND node_id = $nodeId
          AND status IN ('queued', 'claimed', 'started');
        """;
    command.Parameters.AddWithValue("$status", effectiveStatus);
    command.Parameters.AddWithValue(
        "$failureCategory",
        string.Equals(effectiveStatus, "succeeded", StringComparison.Ordinal)
            ? DBNull.Value
            : effectiveCategory ?? "unknown");
    command.Parameters.AddWithValue(
        "$completedAt",
        outcome.CompletedAt.ToString("O", CultureInfo.InvariantCulture));
    command.Parameters.AddWithValue(
        "$targetWorkerRevision",
        effectiveWorkerRevision is null
            ? DBNull.Value
            : effectiveWorkerRevision);
    command.Parameters.AddWithValue(
        "$managerConvergence",
        effectiveConvergence is null
            ? DBNull.Value
            : effectiveConvergence);
    command.Parameters.AddWithValue(
        "$currentWorkers",
        effectiveCurrentWorkers is null
            ? DBNull.Value
            : effectiveCurrentWorkers);
    command.Parameters.AddWithValue(
        "$staleWorkers",
        effectiveStaleWorkers is null
            ? DBNull.Value
            : effectiveStaleWorkers);
    command.Parameters.AddWithValue(
        "$lastError",
        effectiveLastError is null
            ? DBNull.Value
            : effectiveLastError);
    command.Parameters.AddWithValue(
        "$message",
        effectiveMessage is null
            ? DBNull.Value
            : effectiveMessage);
    command.Parameters.AddWithValue(
        "$commandId",
        outcome.CommandId.ToString("D"));
    command.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
    await command.ExecuteNonQueryAsync(cancellationToken);
  }

  private static async Task<string?> ReadActiveRowTargetDigestAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      Guid nodeId,
      Guid commandId,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        SELECT target_digest
        FROM image_rollout_commands
        WHERE command_id = $commandId
          AND node_id = $nodeId
          AND status IN ('queued', 'claimed', 'started');
        """;
    command.Parameters.AddWithValue(
        "$commandId",
        commandId.ToString("D"));
    command.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
    var value = await command.ExecuteScalarAsync(cancellationToken);
    return value is string digest ? digest : null;
  }

  private static async Task ApplyExpiryAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      Guid nodeId,
      DateTimeOffset receivedAt,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        UPDATE image_rollout_commands
        SET status = 'expired',
            failure_category = 'expired',
            completed_at = $receivedAt,
            result_message = 'Command expired before execution started.'
        WHERE node_id = $nodeId
          AND status IN ('queued', 'claimed')
          AND expires_at <= $receivedAt;

        UPDATE image_rollout_commands
        SET status = 'indeterminate',
            failure_category = 'timeout',
            completed_at = $receivedAt,
            result_message = 'Execution started but no terminal outcome was reported.'
        WHERE node_id = $nodeId
          AND status = 'started'
          AND expires_at <= $receivedAt;
        """;
    command.Parameters.AddWithValue(
        "$receivedAt",
        receivedAt.ToString("O", CultureInfo.InvariantCulture));
    command.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
    await command.ExecuteNonQueryAsync(cancellationToken);
  }

  private static async Task<RollOutProfileImageCommand?> OfferAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      Guid nodeId,
      ImageRolloutOperatorCapability? capability,
      DateTimeOffset receivedAt,
      DateTimeOffset redeliverBefore,
      CancellationToken cancellationToken)
  {
    QueuedCommand? queued = null;
    await using (var select = connection.CreateCommand())
    {
      select.Transaction = transaction;
      select.CommandText =
          """
          SELECT
              command_id,
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
              requested_at,
              expires_at
          FROM image_rollout_commands
          WHERE node_id = $nodeId
            AND status = 'queued'
            AND expires_at > $receivedAt
            AND (delivered_at IS NULL OR delivered_at <= $redeliverBefore)
          ORDER BY requested_at
          LIMIT 1;
          """;
      select.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
      select.Parameters.AddWithValue(
          "$receivedAt",
          receivedAt.ToString("O", CultureInfo.InvariantCulture));
      select.Parameters.AddWithValue(
          "$redeliverBefore",
          redeliverBefore.ToString("O", CultureInfo.InvariantCulture));
      await using var reader = await select.ExecuteReaderAsync(
          cancellationToken);
      if (await reader.ReadAsync(cancellationToken))
      {
        queued = new QueuedCommand(
            Guid.Parse(
                reader.GetString(0),
                CultureInfo.InvariantCulture),
            reader.GetString(1),
            Guid.Parse(
                reader.GetString(2),
                CultureInfo.InvariantCulture),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            await ReadStringOrNullAsync(reader, 6, cancellationToken),
            await ReadStringOrNullAsync(reader, 7, cancellationToken),
            await ReadStringOrNullAsync(reader, 8, cancellationToken),
            await ReadStringOrNullAsync(reader, 9, cancellationToken),
            reader.GetString(10),
            reader.GetString(11),
            reader.GetString(12),
            reader.GetInt32(13),
            await ReadStringOrNullAsync(reader, 14, cancellationToken),
            ReadTimestamp(reader.GetString(15)),
            ReadTimestamp(reader.GetString(16)));
      }
    }
    if (queued is null)
    {
      return null;
    }

    var profile = capability?.Profiles.FirstOrDefault(candidate =>
        string.Equals(
            candidate.ProfileId,
            queued.ProfileId,
            StringComparison.OrdinalIgnoreCase));
    var rejection = profile switch
    {
      null => ("not-allowed",
          "Connector no longer advertises image rollout for this profile."),
      _ when profile.LocalFailureCategory is "stale-observed-state" =>
          ("stale-fence",
              "Connector observed profile state is stale; freshness fence is not satisfied."),
      _ when profile.LocalFailureCategory is "unsupported-topology" =>
          ("unsupported-topology",
              "Connector routing state cannot be preserved for this profile."),
      _ when profile.LocalFailureCategory is
              ("unsupported-schema" or "unsupported-manager") =>
          ("unsupported",
              "Connector reports the local worker schema is no longer supported."),
      _ when profile.LocalFailureCategory is
              ("not-allowed" or "policy-disabled") =>
          ("not-allowed",
              "Local policy no longer allows image rollout for this profile."),
      _ when profile.LocalFailureCategory is "unsupported-architecture" =>
          ("unsupported-architecture",
              "Connector architecture no longer matches the candidate platform."),
      _ when profile.LocalFailureCategory is "registry-not-allowed" =>
          ("registry-not-allowed",
              "Local registry policy no longer allows this recipe."),
      _ when !profile.RolloutAllowed || !profile.LocalSchemaSupported =>
          ("not-allowed",
              "Local policy no longer allows image rollout for this profile."),
      _ when !string.Equals(
              profile.Architecture,
              queued.TargetPlatform,
              StringComparison.Ordinal) =>
          ("unsupported-architecture",
              "Connector architecture no longer matches the candidate platform."),
      _ when !profile.AllowedRecipeIds.Any(recipeId =>
              string.Equals(
                  recipeId,
                  queued.RecipeId,
                  StringComparison.OrdinalIgnoreCase)) =>
          ("recipe-not-allowed",
              "Local policy no longer allows this recipe on this profile."),
      _ when !string.Equals(
              profile.StaticFingerprint,
              queued.ExpectedStaticFingerprint,
              StringComparison.OrdinalIgnoreCase) ||
          !string.Equals(
              profile.PreservedConfigurationFingerprint,
              queued.ExpectedPreservedConfigurationFingerprint,
              StringComparison.OrdinalIgnoreCase) ||
          !string.Equals(
              profile.RoutingFingerprint,
              queued.ExpectedRoutingFingerprint,
              StringComparison.OrdinalIgnoreCase) ||
          profile.DesiredGeneration != queued.ExpectedDesiredGeneration ||
          !string.Equals(
              profile.DesiredStateHash,
              queued.ExpectedDesiredStateHash,
              StringComparison.OrdinalIgnoreCase) ||
          !string.Equals(
              profile.CurrentImageReference,
              queued.ExpectedCurrentImageReference,
              StringComparison.Ordinal) ||
          !string.Equals(
              profile.CurrentImageDigest,
              queued.ExpectedCurrentImageDigest,
              StringComparison.OrdinalIgnoreCase) ||
          !string.Equals(
              profile.CurrentLocalImageId,
              queued.ExpectedCurrentLocalImageId,
              StringComparison.OrdinalIgnoreCase) ||
          !string.Equals(
              profile.CurrentWorkerRevision,
              queued.ExpectedCurrentWorkerRevision,
              StringComparison.OrdinalIgnoreCase) =>
          ("stale-fence",
              "Profile fences changed before command delivery."),
      _ => default((string, string)?),
    };
    if (rejection is not null)
    {
      await using var reject = connection.CreateCommand();
      reject.Transaction = transaction;
      reject.CommandText =
          """
          UPDATE image_rollout_commands
          SET status = 'rejected',
              failure_category = $failureCategory,
              completed_at = $receivedAt,
              result_message = $message
          WHERE command_id = $commandId
            AND status = 'queued';
          """;
      reject.Parameters.AddWithValue(
          "$failureCategory",
          rejection.Value.Item1);
      reject.Parameters.AddWithValue(
          "$receivedAt",
          receivedAt.ToString("O", CultureInfo.InvariantCulture));
      reject.Parameters.AddWithValue("$message", rejection.Value.Item2);
      reject.Parameters.AddWithValue(
          "$commandId",
          queued.CommandId.ToString("D"));
      await reject.ExecuteNonQueryAsync(cancellationToken);
      return null;
    }

    await using (var deliver = connection.CreateCommand())
    {
      deliver.Transaction = transaction;
      deliver.CommandText =
          """
          UPDATE image_rollout_commands
          SET delivered_at = $receivedAt,
              delivery_attempts = delivery_attempts + 1
          WHERE command_id = $commandId
            AND status = 'queued';
          """;
      deliver.Parameters.AddWithValue(
          "$receivedAt",
          receivedAt.ToString("O", CultureInfo.InvariantCulture));
      deliver.Parameters.AddWithValue(
          "$commandId",
          queued.CommandId.ToString("D"));
      await deliver.ExecuteNonQueryAsync(cancellationToken);
    }

    return new RollOutProfileImageCommand(
        queued.CommandId,
        queued.CandidateId,
        queued.RecipeId,
        queued.ProfileId,
        queued.TargetDigest,
        queued.TargetPlatform,
        queued.ExpectedCurrentImageReference,
        queued.ExpectedCurrentImageDigest,
        queued.ExpectedCurrentLocalImageId,
        queued.ExpectedCurrentWorkerRevision,
        queued.ExpectedStaticFingerprint,
        queued.ExpectedPreservedConfigurationFingerprint,
        queued.ExpectedRoutingFingerprint,
        queued.ExpectedDesiredGeneration,
        queued.ExpectedDesiredStateHash,
        queued.RequestedAt,
        queued.ExpiresAt);
  }

  private static DateTimeOffset ReadTimestamp(string value) =>
      DateTimeOffset.Parse(
          value,
          CultureInfo.InvariantCulture,
          DateTimeStyles.RoundtripKind);

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

  private static async Task<DateTimeOffset?> ReadTimestampOrNullAsync(
      SqliteDataReader reader,
      int ordinal,
      CancellationToken cancellationToken) =>
      await reader.IsDBNullAsync(ordinal, cancellationToken)
          ? null
          : ReadTimestamp(reader.GetString(ordinal));

  private static async Task<Guid?> ReadGuidOrNullAsync(
      SqliteDataReader reader,
      int ordinal,
      CancellationToken cancellationToken) =>
      await reader.IsDBNullAsync(ordinal, cancellationToken)
          ? null
          : Guid.Parse(
              reader.GetString(ordinal),
              CultureInfo.InvariantCulture);

  private sealed record QueuedCommand(
      Guid CommandId,
      string ProfileId,
      Guid CandidateId,
      string RecipeId,
      string TargetDigest,
      string TargetPlatform,
      string? ExpectedCurrentImageReference,
      string? ExpectedCurrentImageDigest,
      string? ExpectedCurrentLocalImageId,
      string? ExpectedCurrentWorkerRevision,
      string ExpectedStaticFingerprint,
      string ExpectedPreservedConfigurationFingerprint,
      string ExpectedRoutingFingerprint,
      int ExpectedDesiredGeneration,
      string? ExpectedDesiredStateHash,
      DateTimeOffset RequestedAt,
      DateTimeOffset ExpiresAt);
}
