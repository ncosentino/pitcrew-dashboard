using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Microsoft.Data.Sqlite;

using NexusLabs.Needlr;

using PitCrew.Dashboard.Features.Images.Abstractions;

namespace PitCrew.Dashboard.Adapters.Sqlite;

[DoNotAutoRegister]
internal sealed class SqliteImageCandidateStore(
    SqliteConnectionFactory _connectionFactory) : IImageCandidateStore
{
  private const int MaximumListLimit = 200;

  public async Task<ImageCandidateMutationResult> CreateRecipeVersionAsync(
      ImageRecipeRegistration registration,
      CancellationToken cancellationToken)
  {
    var existing = await GetRecipeVersionOrNullAsync(
        registration.TenantId,
        registration.RegistrationId,
        registration.Version,
        cancellationToken);
    if (existing is not null)
    {
      return existing == registration
          ? ImageCandidateMutationResult.Unchanged
          : ImageCandidateMutationResult.Conflict;
    }

    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        INSERT INTO image_recipe_versions (
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
        VALUES (
            $tenantId,
            $registrationId,
            $version,
            $githubInstallationId,
            $githubRepositoryId,
            $githubWorkflowId,
            $repositoryOwner,
            $repositoryName,
            $workflowPath,
            $workflowBlobSha,
            $dispatchRef,
            $recipeId,
            $candidateSchemaVersion,
            $sourceRefPolicyJson,
            $inputSchemaJson,
            $createdByGitHubUserId,
            $createdAt,
            $disabledByGitHubUserId,
            $disabledAt);
        """;
    AddRecipeParameters(command, registration);
    try
    {
      await command.ExecuteNonQueryAsync(cancellationToken);
      return ImageCandidateMutationResult.Succeeded;
    }
    catch (SqliteException exception)
        when (exception.SqliteErrorCode == 19)
    {
      var durable = await GetRecipeVersionOrNullAsync(
          registration.TenantId,
          registration.RegistrationId,
          registration.Version,
          cancellationToken);
      return durable == registration
          ? ImageCandidateMutationResult.Unchanged
          : ImageCandidateMutationResult.Conflict;
    }
  }

  public async Task<IReadOnlyList<ImageRecipeRegistration>> ListRecipeVersionsAsync(
      string tenantId,
      string recipeId,
      int limit,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        $"""
        {RecipeSelectSql}
        WHERE tenant_id = $tenantId
          AND recipe_id = $recipeId
        ORDER BY version DESC
        LIMIT $limit;
        """;
    command.Parameters.AddWithValue("$tenantId", tenantId);
    command.Parameters.AddWithValue("$recipeId", recipeId);
    command.Parameters.AddWithValue(
        "$limit",
        Math.Clamp(limit, 1, MaximumListLimit));
    var registrations = new List<ImageRecipeRegistration>();
    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
    while (await reader.ReadAsync(cancellationToken))
    {
      registrations.Add(ReadRecipe(reader));
    }
    return registrations;
  }

  public async Task<ImageRecipeRegistration?> GetRecipeVersionOrNullAsync(
      string tenantId,
      Guid registrationId,
      int version,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        $"""
        {RecipeSelectSql}
        WHERE tenant_id = $tenantId
          AND registration_id = $registrationId
          AND version = $version;
        """;
    command.Parameters.AddWithValue("$tenantId", tenantId);
    command.Parameters.AddWithValue(
        "$registrationId",
        registrationId.ToString("D"));
    command.Parameters.AddWithValue("$version", version);
    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
    return await reader.ReadAsync(cancellationToken)
        ? ReadRecipe(reader)
        : null;
  }

  public async Task<ImageCandidateMutationResult> DisableRecipeVersionAsync(
      string tenantId,
      Guid registrationId,
      int version,
      string disabledByGitHubUserId,
      DateTimeOffset disabledAt,
      CancellationToken cancellationToken)
  {
    var existing = await GetRecipeVersionOrNullAsync(
        tenantId,
        registrationId,
        version,
        cancellationToken);
    if (existing is null)
    {
      return ImageCandidateMutationResult.NotFound;
    }
    if (existing.DisabledAt is not null)
    {
      return existing.DisabledAt == disabledAt
          && string.Equals(
              existing.DisabledByGitHubUserId,
              disabledByGitHubUserId,
              StringComparison.Ordinal)
          ? ImageCandidateMutationResult.Unchanged
          : ImageCandidateMutationResult.Conflict;
    }

    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        UPDATE image_recipe_versions
        SET disabled_by_github_user_id = $disabledByGitHubUserId,
            disabled_at = $disabledAt
        WHERE tenant_id = $tenantId
          AND registration_id = $registrationId
          AND version = $version
          AND disabled_at IS NULL;
        """;
    command.Parameters.AddWithValue("$tenantId", tenantId);
    command.Parameters.AddWithValue(
        "$registrationId",
        registrationId.ToString("D"));
    command.Parameters.AddWithValue("$version", version);
    command.Parameters.AddWithValue(
        "$disabledByGitHubUserId",
        disabledByGitHubUserId);
    command.Parameters.AddWithValue("$disabledAt", Format(disabledAt));
    try
    {
      if (await command.ExecuteNonQueryAsync(cancellationToken) == 1)
      {
        return ImageCandidateMutationResult.Succeeded;
      }
    }
    catch (SqliteException exception)
        when (exception.SqliteErrorCode == 19)
    {
    }
    var durable = await GetRecipeVersionOrNullAsync(
        tenantId,
        registrationId,
        version,
        cancellationToken);
    if (durable is null)
    {
      return ImageCandidateMutationResult.NotFound;
    }
    return durable.DisabledAt == disabledAt
        && string.Equals(
            durable.DisabledByGitHubUserId,
            disabledByGitHubUserId,
            StringComparison.Ordinal)
        ? ImageCandidateMutationResult.Unchanged
        : ImageCandidateMutationResult.Conflict;
  }

  public async Task<ImageCandidateMutationResult> CreateBuildRequestAsync(
      ImageBuildRequest request,
      CancellationToken cancellationToken)
  {
    var existing = await GetBuildRequestOrNullAsync(
        request.TenantId,
        request.RequestId,
        cancellationToken);
    if (existing is not null)
    {
      return existing == request
          ? ImageCandidateMutationResult.Unchanged
          : ImageCandidateMutationResult.Conflict;
    }
    if (request.Status != ImageBuildRequestStatus.Requested
        || request.GitHubRunId is not null
        || request.GitHubRunUrl is not null
        || request.TerminalCategory is not null
        || request.TerminalDetail is not null
        || !HasMatchingSha256(
            request.InputValuesJson,
            request.InputValuesSha256))
    {
      return ImageCandidateMutationResult.InvalidTransition;
    }
    var registration = await GetRecipeVersionOrNullAsync(
        request.TenantId,
        request.RegistrationId,
        request.RegistrationVersion,
        cancellationToken);
    if (registration is null
        || !string.Equals(
            registration.RecipeId,
            request.RecipeId,
            StringComparison.Ordinal)
        || !string.Equals(
            $"{registration.RepositoryOwner}/{registration.RepositoryName}",
            request.SourceRepository,
            StringComparison.Ordinal))
    {
      return ImageCandidateMutationResult.Conflict;
    }

    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        INSERT INTO image_build_requests (
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
        VALUES (
            $requestId,
            $tenantId,
            $registrationId,
            $registrationVersion,
            $recipeId,
            $sourceRepository,
            $sourceCommit,
            $inputValuesJson,
            $inputValuesSha256,
            $requestedByGitHubUserId,
            $requestedAt,
            'requested',
            NULL,
            NULL,
            NULL,
            NULL,
            $updatedAt);
        """;
    AddRequestParameters(command, request);
    try
    {
      await command.ExecuteNonQueryAsync(cancellationToken);
      return ImageCandidateMutationResult.Succeeded;
    }
    catch (SqliteException exception)
        when (exception.SqliteErrorCode == 19)
    {
      var durable = await GetBuildRequestOrNullAsync(
          request.TenantId,
          request.RequestId,
          cancellationToken);
      return durable == request
          ? ImageCandidateMutationResult.Unchanged
          : ImageCandidateMutationResult.Conflict;
    }
  }

  public async Task<IReadOnlyList<ImageBuildRequest>> ListBuildRequestsAsync(
      string tenantId,
      ImageBuildRequestStatus? status,
      int limit,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var command = connection.CreateCommand();
    if (status is null)
    {
      command.CommandText =
          $"""
          {RequestSelectSql}
          WHERE tenant_id = $tenantId
          ORDER BY requested_at DESC, request_id DESC
          LIMIT $limit;
          """;
    }
    else
    {
      command.CommandText =
          $"""
          {RequestSelectSql}
          WHERE tenant_id = $tenantId
            AND status = $status
          ORDER BY requested_at DESC, request_id DESC
          LIMIT $limit;
          """;
    }
    command.Parameters.AddWithValue("$tenantId", tenantId);
    command.Parameters.AddWithValue(
        "$limit",
        Math.Clamp(limit, 1, MaximumListLimit));
    if (status is not null)
    {
      command.Parameters.AddWithValue("$status", Format(status.Value));
    }
    var requests = new List<ImageBuildRequest>();
    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
    while (await reader.ReadAsync(cancellationToken))
    {
      requests.Add(ReadRequest(reader));
    }
    return requests;
  }

  public async Task<ImageBuildRequest?> GetBuildRequestOrNullAsync(
      string tenantId,
      Guid requestId,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        $"""
        {RequestSelectSql}
        WHERE tenant_id = $tenantId
          AND request_id = $requestId;
        """;
    command.Parameters.AddWithValue("$tenantId", tenantId);
    command.Parameters.AddWithValue("$requestId", requestId.ToString("D"));
    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
    return await reader.ReadAsync(cancellationToken)
        ? ReadRequest(reader)
        : null;
  }

  public async Task<ImageCandidateMutationResult> ApplyBuildRequestTransitionAsync(
      string tenantId,
      Guid requestId,
      ImageBuildRequestTransition transition,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var transaction = (SqliteTransaction)
        await connection.BeginTransactionAsync(cancellationToken);
    var existing = await GetRequestOrNullAsync(
        connection,
        transaction,
        tenantId,
        requestId,
        cancellationToken);
    if (existing is null)
    {
      await transaction.RollbackAsync(cancellationToken);
      return ImageCandidateMutationResult.NotFound;
    }

    var runId = transition.GitHubRunId ?? existing.GitHubRunId;
    var runUrl = transition.GitHubRunUrl ?? existing.GitHubRunUrl;
    if (IsExactTransitionReplay(existing, transition, runId, runUrl))
    {
      await transaction.RollbackAsync(cancellationToken);
      return ImageCandidateMutationResult.Unchanged;
    }
    if (existing.Status != transition.ExpectedCurrentStatus)
    {
      await transaction.RollbackAsync(cancellationToken);
      return ImageCandidateMutationResult.Conflict;
    }
    if (!IsAllowedTransition(
            existing.Status,
            transition.NewStatus)
        || transition.NewStatus == ImageBuildRequestStatus.Ready
        || (existing.GitHubRunId is not null
            && (existing.GitHubRunId != runId
                || !string.Equals(
                    existing.GitHubRunUrl,
                    runUrl,
                    StringComparison.Ordinal)))
        || ((runId is null) != (runUrl is null))
        || (transition.NewStatus is ImageBuildRequestStatus.Building
            or ImageBuildRequestStatus.Qualifying
            && runId is null)
        || !HasValidTerminalEvidence(
            transition.NewStatus,
            transition.TerminalCategory,
            transition.TerminalDetail))
    {
      await transaction.RollbackAsync(cancellationToken);
      return ImageCandidateMutationResult.InvalidTransition;
    }

    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        UPDATE image_build_requests
        SET status = $newStatus,
            github_run_id = $githubRunId,
            github_run_url = $githubRunUrl,
            terminal_category = $terminalCategory,
            terminal_detail = $terminalDetail,
            updated_at = $updatedAt
        WHERE tenant_id = $tenantId
          AND request_id = $requestId
          AND status = $expectedStatus;
        """;
    command.Parameters.AddWithValue("$newStatus", Format(transition.NewStatus));
    command.Parameters.AddWithValue(
        "$githubRunId",
        runId is null ? DBNull.Value : runId.Value);
    command.Parameters.AddWithValue(
        "$githubRunUrl",
        runUrl is null ? DBNull.Value : runUrl);
    command.Parameters.AddWithValue(
        "$terminalCategory",
        transition.TerminalCategory is null
            ? DBNull.Value
            : transition.TerminalCategory);
    command.Parameters.AddWithValue(
        "$terminalDetail",
        transition.TerminalDetail is null
            ? DBNull.Value
            : transition.TerminalDetail);
    command.Parameters.AddWithValue("$updatedAt", Format(transition.UpdatedAt));
    command.Parameters.AddWithValue("$tenantId", tenantId);
    command.Parameters.AddWithValue("$requestId", requestId.ToString("D"));
    command.Parameters.AddWithValue(
        "$expectedStatus",
        Format(transition.ExpectedCurrentStatus));
    try
    {
      if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
      {
        await transaction.RollbackAsync(cancellationToken);
        return ImageCandidateMutationResult.Conflict;
      }
      await transaction.CommitAsync(cancellationToken);
      return ImageCandidateMutationResult.Succeeded;
    }
    catch (SqliteException exception)
        when (exception.SqliteErrorCode == 19)
    {
      await transaction.RollbackAsync(cancellationToken);
      return ImageCandidateMutationResult.InvalidTransition;
    }
  }

  public async Task<ImageCandidateMutationResult> StoreCandidateAsync(
      string tenantId,
      ImageCandidate candidate,
      IReadOnlyList<ImageCandidateQualification> qualifications,
      CancellationToken cancellationToken)
  {
    if (!string.Equals(tenantId, candidate.TenantId, StringComparison.Ordinal)
        || !HasMatchingSha256(candidate.ReportJson, candidate.ReportHash)
        || !IsValidQualificationSet(candidate, qualifications))
    {
      return ImageCandidateMutationResult.Conflict;
    }

    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var transaction = (SqliteTransaction)
        await connection.BeginTransactionAsync(cancellationToken);
    var request = await GetRequestOrNullAsync(
        connection,
        transaction,
        tenantId,
        candidate.RequestId,
        cancellationToken);
    if (request is null)
    {
      await transaction.RollbackAsync(cancellationToken);
      return ImageCandidateMutationResult.NotFound;
    }

    var existingCandidate = await GetCandidateOrNullAsync(
        connection,
        transaction,
        tenantId,
        candidate.RequestId,
        cancellationToken);
    if (existingCandidate is not null)
    {
      var existingQualifications = await GetQualificationsAsync(
          connection,
          transaction,
          existingCandidate.CandidateId,
          cancellationToken);
      await transaction.RollbackAsync(cancellationToken);
      return existingCandidate == candidate
          && existingQualifications.SequenceEqual(
              OrderQualifications(qualifications))
          ? ImageCandidateMutationResult.Unchanged
          : ImageCandidateMutationResult.Conflict;
    }

    if (request.Status != ImageBuildRequestStatus.Qualifying
        || request.GitHubRunId is null
        || request.GitHubRunId != candidate.GitHubRunId
        || !string.Equals(request.RecipeId, candidate.RecipeId, StringComparison.Ordinal)
        || !string.Equals(
            request.SourceRepository,
            candidate.SourceRepository,
            StringComparison.Ordinal)
        || !string.Equals(
            request.SourceCommit,
            candidate.SourceCommit,
            StringComparison.Ordinal))
    {
      await transaction.RollbackAsync(cancellationToken);
      return ImageCandidateMutationResult.InvalidTransition;
    }

    try
    {
      await InsertCandidateAsync(
          connection,
          transaction,
          candidate,
          cancellationToken);
      foreach (var qualification in qualifications)
      {
        await InsertQualificationAsync(
            connection,
            transaction,
            qualification,
            cancellationToken);
      }
      if (!await TerminalizeRequestAsync(
          connection,
          transaction,
          request,
          candidate,
          cancellationToken))
      {
        await transaction.RollbackAsync(cancellationToken);
        return ImageCandidateMutationResult.Conflict;
      }
      await transaction.CommitAsync(cancellationToken);
      return ImageCandidateMutationResult.Succeeded;
    }
    catch (SqliteException exception)
        when (exception.SqliteErrorCode == 19)
    {
      await transaction.RollbackAsync(cancellationToken);
      return ImageCandidateMutationResult.Conflict;
    }
  }

  public async Task<int> PurgeTerminalBuildRequestsAsync(
      string tenantId,
      DateTimeOffset olderThan,
      int limit,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var transaction = (SqliteTransaction)
        await connection.BeginTransactionAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        DELETE FROM image_build_requests
        WHERE tenant_id = $tenantId
          AND request_id IN (
            SELECT request_id
            FROM image_build_requests
            WHERE tenant_id = $tenantId
              AND status IN ('ready', 'blocked', 'failed')
              AND updated_at < $olderThan
            ORDER BY updated_at, request_id
            LIMIT $limit);
        """;
    command.Parameters.AddWithValue("$tenantId", tenantId);
    command.Parameters.AddWithValue("$olderThan", Format(olderThan));
    command.Parameters.AddWithValue(
        "$limit",
        Math.Clamp(limit, 1, MaximumListLimit));
    var deleted = await command.ExecuteNonQueryAsync(cancellationToken);
    await transaction.CommitAsync(cancellationToken);
    return deleted;
  }

  private static async Task InsertCandidateAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      ImageCandidate candidate,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        INSERT INTO image_candidates (
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
        VALUES (
            $candidateId,
            $tenantId,
            $requestId,
            $outcome,
            $recipeId,
            $sourceRepository,
            $sourceCommit,
            $githubRunId,
            $artifactId,
            $artifactName,
            $artifactDigest,
            $reportHash,
            $reportJson,
            $imageReference,
            $digest,
            $immutableReference,
            $platform,
            $outputMode,
            $failureCategory,
            $failureDetail,
            $createdAt,
            $storedAt);
        """;
    var ready = candidate as ReadyImageCandidate;
    var failed = candidate as FailedImageCandidate;
    command.Parameters.AddWithValue(
        "$candidateId",
        candidate.CandidateId.ToString("D"));
    command.Parameters.AddWithValue("$tenantId", candidate.TenantId);
    command.Parameters.AddWithValue(
        "$requestId",
        candidate.RequestId.ToString("D"));
    command.Parameters.AddWithValue(
        "$outcome",
        ready is not null ? "ready" : "failed");
    command.Parameters.AddWithValue("$recipeId", candidate.RecipeId);
    command.Parameters.AddWithValue(
        "$sourceRepository",
        candidate.SourceRepository);
    command.Parameters.AddWithValue("$sourceCommit", candidate.SourceCommit);
    command.Parameters.AddWithValue("$githubRunId", candidate.GitHubRunId);
    command.Parameters.AddWithValue("$artifactId", candidate.ArtifactId);
    command.Parameters.AddWithValue("$artifactName", candidate.ArtifactName);
    command.Parameters.AddWithValue(
        "$artifactDigest",
        candidate.ArtifactDigest);
    command.Parameters.AddWithValue("$reportHash", candidate.ReportHash);
    command.Parameters.AddWithValue("$reportJson", candidate.ReportJson);
    command.Parameters.AddWithValue(
        "$imageReference",
        candidate.ImageReference);
    command.Parameters.AddWithValue(
        "$digest",
        ready?.Digest ?? failed?.Digest ?? (object)DBNull.Value);
    command.Parameters.AddWithValue(
        "$immutableReference",
        ready?.ImmutableReference
            ?? failed?.ImmutableReference
            ?? (object)DBNull.Value);
    command.Parameters.AddWithValue("$platform", Format(candidate.Platform));
    command.Parameters.AddWithValue("$outputMode", Format(candidate.OutputMode));
    command.Parameters.AddWithValue(
        "$failureCategory",
        failed?.FailureCategory ?? (object)DBNull.Value);
    command.Parameters.AddWithValue(
        "$failureDetail",
        failed?.FailureDetail ?? (object)DBNull.Value);
    command.Parameters.AddWithValue("$createdAt", Format(candidate.CreatedAt));
    command.Parameters.AddWithValue("$storedAt", Format(candidate.StoredAt));
    await command.ExecuteNonQueryAsync(cancellationToken);
  }

  private static async Task InsertQualificationAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      ImageCandidateQualification qualification,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        INSERT INTO image_candidate_qualifications (
            candidate_id,
            name,
            status)
        VALUES (
            $candidateId,
            $name,
            $status);
        """;
    command.Parameters.AddWithValue(
        "$candidateId",
        qualification.CandidateId.ToString("D"));
    command.Parameters.AddWithValue("$name", Format(qualification.Name));
    command.Parameters.AddWithValue("$status", Format(qualification.Status));
    await command.ExecuteNonQueryAsync(cancellationToken);
  }

  private static async Task<bool> TerminalizeRequestAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      ImageBuildRequest request,
      ImageCandidate candidate,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        UPDATE image_build_requests
        SET status = $status,
            terminal_category = $terminalCategory,
            terminal_detail = $terminalDetail,
            updated_at = $updatedAt
        WHERE tenant_id = $tenantId
          AND request_id = $requestId
          AND status = 'qualifying';
        """;
    var failed = candidate as FailedImageCandidate;
    command.Parameters.AddWithValue(
        "$status",
        failed is null ? "ready" : "failed");
    command.Parameters.AddWithValue(
        "$terminalCategory",
        failed?.FailureCategory ?? (object)DBNull.Value);
    command.Parameters.AddWithValue(
        "$terminalDetail",
        failed?.FailureDetail ?? (object)DBNull.Value);
    command.Parameters.AddWithValue("$updatedAt", Format(candidate.StoredAt));
    command.Parameters.AddWithValue("$tenantId", candidate.TenantId);
    command.Parameters.AddWithValue(
        "$requestId",
        candidate.RequestId.ToString("D"));
    return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
  }

  private static bool IsValidQualificationSet(
      ImageCandidate candidate,
      IReadOnlyList<ImageCandidateQualification> qualifications)
  {
    if (qualifications.Count != 4
        || qualifications.Any(qualification =>
            qualification.CandidateId != candidate.CandidateId)
        || qualifications.Select(qualification => qualification.Name)
            .Distinct()
            .Count() != qualifications.Count)
    {
      return false;
    }

    var expected = candidate.OutputMode == ImageCandidateOutputMode.Registry
        ? new[]
        {
          ImageCandidateQualificationName.ImageBuild,
          ImageCandidateQualificationName.BuildKitDigest,
          ImageCandidateQualificationName.RegistryDigest,
          ImageCandidateQualificationName.BuilderCleanup,
        }
        : new[]
        {
          ImageCandidateQualificationName.ImageBuild,
          ImageCandidateQualificationName.BuildKitDigest,
          ImageCandidateQualificationName.OciManifest,
          ImageCandidateQualificationName.BuilderCleanup,
        };
    if (!qualifications.Select(qualification => qualification.Name)
        .Order()
        .SequenceEqual(expected.Order()))
    {
      return false;
    }

    return candidate switch
    {
      ReadyImageCandidate ready =>
          qualifications.All(qualification =>
              qualification.Status
                  == ImageCandidateQualificationStatus.Passed)
          && !string.IsNullOrEmpty(ready.Digest)
          && ((ready.OutputMode == ImageCandidateOutputMode.Registry
                  && !string.IsNullOrEmpty(ready.ImmutableReference))
              || (ready.OutputMode == ImageCandidateOutputMode.Oci
                  && ready.ImmutableReference is null)),
      FailedImageCandidate failed =>
          !string.IsNullOrEmpty(failed.FailureCategory)
          && !string.IsNullOrEmpty(failed.FailureDetail),
      _ => false,
    };
  }

  private static bool IsAllowedTransition(
      ImageBuildRequestStatus current,
      ImageBuildRequestStatus next) =>
      (current, next) switch
      {
        (ImageBuildRequestStatus.Requested,
            ImageBuildRequestStatus.Dispatching) => true,
        (ImageBuildRequestStatus.Dispatching,
            ImageBuildRequestStatus.Building) => true,
        (ImageBuildRequestStatus.Building,
            ImageBuildRequestStatus.Qualifying) => true,
        (ImageBuildRequestStatus.Qualifying,
            ImageBuildRequestStatus.Ready
                or ImageBuildRequestStatus.Blocked
                or ImageBuildRequestStatus.Failed) => true,
        _ => false,
      };

  private static bool HasValidTerminalEvidence(
      ImageBuildRequestStatus status,
      string? category,
      string? detail) =>
      status is ImageBuildRequestStatus.Blocked
          or ImageBuildRequestStatus.Failed
          ? !string.IsNullOrEmpty(category) && !string.IsNullOrEmpty(detail)
          : category is null && detail is null;

  private static bool IsExactTransitionReplay(
      ImageBuildRequest existing,
      ImageBuildRequestTransition transition,
      long? runId,
      string? runUrl) =>
      existing.Status == transition.NewStatus
      && existing.GitHubRunId == runId
      && string.Equals(existing.GitHubRunUrl, runUrl, StringComparison.Ordinal)
      && string.Equals(
          existing.TerminalCategory,
          transition.TerminalCategory,
          StringComparison.Ordinal)
      && string.Equals(
          existing.TerminalDetail,
          transition.TerminalDetail,
          StringComparison.Ordinal)
      && existing.UpdatedAt == transition.UpdatedAt;

  private static bool HasMatchingSha256(string value, string hash)
  {
    var actual = Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    return string.Equals(actual, hash, StringComparison.OrdinalIgnoreCase)
        && string.Equals(hash, hash.ToLowerInvariant(), StringComparison.Ordinal);
  }

  private static async Task<ImageBuildRequest?> GetRequestOrNullAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      string tenantId,
      Guid requestId,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        $"""
        {RequestSelectSql}
        WHERE tenant_id = $tenantId
          AND request_id = $requestId;
        """;
    command.Parameters.AddWithValue("$tenantId", tenantId);
    command.Parameters.AddWithValue("$requestId", requestId.ToString("D"));
    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
    return await reader.ReadAsync(cancellationToken)
        ? ReadRequest(reader)
        : null;
  }

  private static async Task<ImageCandidate?> GetCandidateOrNullAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      string tenantId,
      Guid requestId,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
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
        FROM image_candidates
        WHERE tenant_id = $tenantId
          AND request_id = $requestId;
        """;
    command.Parameters.AddWithValue("$tenantId", tenantId);
    command.Parameters.AddWithValue("$requestId", requestId.ToString("D"));
    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
    return await reader.ReadAsync(cancellationToken)
        ? ReadCandidate(reader)
        : null;
  }

  private static async Task<IReadOnlyList<ImageCandidateQualification>> GetQualificationsAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      Guid candidateId,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        SELECT name, status
        FROM image_candidate_qualifications
        WHERE candidate_id = $candidateId
        ORDER BY name;
        """;
    command.Parameters.AddWithValue(
        "$candidateId",
        candidateId.ToString("D"));
    var qualifications = new List<ImageCandidateQualification>();
    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
    while (await reader.ReadAsync(cancellationToken))
    {
      qualifications.Add(new ImageCandidateQualification(
          candidateId,
          ParseQualificationName(reader.GetString(0)),
          ParseQualificationStatus(reader.GetString(1))));
    }
    return qualifications;
  }

  private static IEnumerable<ImageCandidateQualification> OrderQualifications(
      IEnumerable<ImageCandidateQualification> qualifications) =>
      qualifications.OrderBy(qualification => Format(qualification.Name));

  private static void AddRecipeParameters(
      SqliteCommand command,
      ImageRecipeRegistration registration)
  {
    command.Parameters.AddWithValue("$tenantId", registration.TenantId);
    command.Parameters.AddWithValue(
        "$registrationId",
        registration.RegistrationId.ToString("D"));
    command.Parameters.AddWithValue("$version", registration.Version);
    command.Parameters.AddWithValue(
        "$githubInstallationId",
        registration.GitHubInstallationId);
    command.Parameters.AddWithValue(
        "$githubRepositoryId",
        registration.GitHubRepositoryId);
    command.Parameters.AddWithValue(
        "$githubWorkflowId",
        registration.GitHubWorkflowId);
    command.Parameters.AddWithValue(
        "$repositoryOwner",
        registration.RepositoryOwner);
    command.Parameters.AddWithValue(
        "$repositoryName",
        registration.RepositoryName);
    command.Parameters.AddWithValue("$workflowPath", registration.WorkflowPath);
    command.Parameters.AddWithValue(
        "$workflowBlobSha",
        registration.WorkflowBlobSha);
    command.Parameters.AddWithValue("$dispatchRef", registration.DispatchRef);
    command.Parameters.AddWithValue("$recipeId", registration.RecipeId);
    command.Parameters.AddWithValue(
        "$candidateSchemaVersion",
        registration.CandidateSchemaVersion);
    command.Parameters.AddWithValue(
        "$sourceRefPolicyJson",
        registration.SourceRefPolicyJson);
    command.Parameters.AddWithValue(
        "$inputSchemaJson",
        registration.InputSchemaJson);
    command.Parameters.AddWithValue(
        "$createdByGitHubUserId",
        registration.CreatedByGitHubUserId);
    command.Parameters.AddWithValue("$createdAt", Format(registration.CreatedAt));
    command.Parameters.AddWithValue(
        "$disabledByGitHubUserId",
        registration.DisabledByGitHubUserId is null
            ? DBNull.Value
            : registration.DisabledByGitHubUserId);
    command.Parameters.AddWithValue(
        "$disabledAt",
        registration.DisabledAt is null
            ? DBNull.Value
            : Format(registration.DisabledAt.Value));
  }

  private static void AddRequestParameters(
      SqliteCommand command,
      ImageBuildRequest request)
  {
    command.Parameters.AddWithValue(
        "$requestId",
        request.RequestId.ToString("D"));
    command.Parameters.AddWithValue("$tenantId", request.TenantId);
    command.Parameters.AddWithValue(
        "$registrationId",
        request.RegistrationId.ToString("D"));
    command.Parameters.AddWithValue(
        "$registrationVersion",
        request.RegistrationVersion);
    command.Parameters.AddWithValue("$recipeId", request.RecipeId);
    command.Parameters.AddWithValue(
        "$sourceRepository",
        request.SourceRepository);
    command.Parameters.AddWithValue("$sourceCommit", request.SourceCommit);
    command.Parameters.AddWithValue("$inputValuesJson", request.InputValuesJson);
    command.Parameters.AddWithValue(
        "$inputValuesSha256",
        request.InputValuesSha256);
    command.Parameters.AddWithValue(
        "$requestedByGitHubUserId",
        request.RequestedByGitHubUserId);
    command.Parameters.AddWithValue("$requestedAt", Format(request.RequestedAt));
    command.Parameters.AddWithValue("$updatedAt", Format(request.UpdatedAt));
  }

  private static ImageRecipeRegistration ReadRecipe(SqliteDataReader reader) =>
      new(
          reader.GetString(0),
          Guid.Parse(reader.GetString(1), CultureInfo.InvariantCulture),
          reader.GetInt32(2),
          reader.GetInt64(3),
          reader.GetInt64(4),
          reader.GetInt64(5),
          reader.GetString(6),
          reader.GetString(7),
          reader.GetString(8),
          reader.GetString(9),
          reader.GetString(10),
          reader.GetString(11),
          reader.GetInt32(12),
          reader.GetString(13),
          reader.GetString(14),
          reader.GetString(15),
          ParseDate(reader.GetString(16)),
          reader.IsDBNull(17) ? null : reader.GetString(17),
          reader.IsDBNull(18) ? null : ParseDate(reader.GetString(18)));

  private static ImageBuildRequest ReadRequest(SqliteDataReader reader) =>
      new(
          reader.GetString(1),
          Guid.Parse(reader.GetString(0), CultureInfo.InvariantCulture),
          Guid.Parse(reader.GetString(2), CultureInfo.InvariantCulture),
          reader.GetInt32(3),
          reader.GetString(4),
          reader.GetString(5),
          reader.GetString(6),
          reader.GetString(7),
          reader.GetString(8),
          reader.GetString(9),
          ParseDate(reader.GetString(10)),
          ParseRequestStatus(reader.GetString(11)),
          reader.IsDBNull(12) ? null : reader.GetInt64(12),
          reader.IsDBNull(13) ? null : reader.GetString(13),
          reader.IsDBNull(14) ? null : reader.GetString(14),
          reader.IsDBNull(15) ? null : reader.GetString(15),
          ParseDate(reader.GetString(16)));

  private static ImageCandidate ReadCandidate(SqliteDataReader reader)
  {
    var candidateId = Guid.Parse(
        reader.GetString(0),
        CultureInfo.InvariantCulture);
    var tenantId = reader.GetString(1);
    var requestId = Guid.Parse(
        reader.GetString(2),
        CultureInfo.InvariantCulture);
    var recipeId = reader.GetString(4);
    var sourceRepository = reader.GetString(5);
    var sourceCommit = reader.GetString(6);
    var githubRunId = reader.GetInt64(7);
    var artifactId = reader.GetInt64(8);
    var artifactName = reader.GetString(9);
    var artifactDigest = reader.GetString(10);
    var reportHash = reader.GetString(11);
    var reportJson = reader.GetString(12);
    var imageReference = reader.GetString(13);
    var digest = reader.IsDBNull(14) ? null : reader.GetString(14);
    var immutableReference = reader.IsDBNull(15)
        ? null
        : reader.GetString(15);
    var platform = ParsePlatform(reader.GetString(16));
    var outputMode = ParseOutputMode(reader.GetString(17));
    var createdAt = ParseDate(reader.GetString(20));
    var storedAt = ParseDate(reader.GetString(21));
    return string.Equals(
        reader.GetString(3),
        "ready",
        StringComparison.Ordinal)
        ? new ReadyImageCandidate(
            candidateId,
            tenantId,
            requestId,
            recipeId,
            sourceRepository,
            sourceCommit,
            githubRunId,
            artifactId,
            artifactName,
            artifactDigest,
            reportHash,
            reportJson,
            imageReference,
            platform,
            outputMode,
            createdAt,
            storedAt,
            digest!,
            immutableReference)
        : new FailedImageCandidate(
            candidateId,
            tenantId,
            requestId,
            recipeId,
            sourceRepository,
            sourceCommit,
            githubRunId,
            artifactId,
            artifactName,
            artifactDigest,
            reportHash,
            reportJson,
            imageReference,
            platform,
            outputMode,
            createdAt,
            storedAt,
            digest,
            immutableReference,
            reader.GetString(18),
            reader.GetString(19));
  }

  private static string Format(DateTimeOffset value) =>
      value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

  private static DateTimeOffset ParseDate(string value) =>
      DateTimeOffset.Parse(
          value,
          CultureInfo.InvariantCulture,
          DateTimeStyles.RoundtripKind);

  private static string Format(ImageBuildRequestStatus value) =>
      value switch
      {
        ImageBuildRequestStatus.Requested => "requested",
        ImageBuildRequestStatus.Dispatching => "dispatching",
        ImageBuildRequestStatus.Building => "building",
        ImageBuildRequestStatus.Qualifying => "qualifying",
        ImageBuildRequestStatus.Ready => "ready",
        ImageBuildRequestStatus.Blocked => "blocked",
        ImageBuildRequestStatus.Failed => "failed",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
      };

  private static ImageBuildRequestStatus ParseRequestStatus(string value) =>
      value switch
      {
        "requested" => ImageBuildRequestStatus.Requested,
        "dispatching" => ImageBuildRequestStatus.Dispatching,
        "building" => ImageBuildRequestStatus.Building,
        "qualifying" => ImageBuildRequestStatus.Qualifying,
        "ready" => ImageBuildRequestStatus.Ready,
        "blocked" => ImageBuildRequestStatus.Blocked,
        "failed" => ImageBuildRequestStatus.Failed,
        _ => throw new InvalidOperationException(
            $"SQLite returned unknown image request status '{value}'."),
      };

  private static string Format(ImageCandidateQualificationName value) =>
      value switch
      {
        ImageCandidateQualificationName.ImageBuild => "image-build",
        ImageCandidateQualificationName.BuildKitDigest => "buildkit-digest",
        ImageCandidateQualificationName.RegistryDigest => "registry-digest",
        ImageCandidateQualificationName.OciManifest => "oci-manifest",
        ImageCandidateQualificationName.BuilderCleanup => "builder-cleanup",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
      };

  private static ImageCandidateQualificationName ParseQualificationName(
      string value) =>
      value switch
      {
        "image-build" => ImageCandidateQualificationName.ImageBuild,
        "buildkit-digest" => ImageCandidateQualificationName.BuildKitDigest,
        "registry-digest" => ImageCandidateQualificationName.RegistryDigest,
        "oci-manifest" => ImageCandidateQualificationName.OciManifest,
        "builder-cleanup" => ImageCandidateQualificationName.BuilderCleanup,
        _ => throw new InvalidOperationException(
            $"SQLite returned unknown image qualification name '{value}'."),
      };

  private static string Format(ImageCandidateQualificationStatus value) =>
      value switch
      {
        ImageCandidateQualificationStatus.Passed => "passed",
        ImageCandidateQualificationStatus.Failed => "failed",
        ImageCandidateQualificationStatus.Unavailable => "unavailable",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
      };

  private static ImageCandidateQualificationStatus ParseQualificationStatus(
      string value) =>
      value switch
      {
        "passed" => ImageCandidateQualificationStatus.Passed,
        "failed" => ImageCandidateQualificationStatus.Failed,
        "unavailable" => ImageCandidateQualificationStatus.Unavailable,
        _ => throw new InvalidOperationException(
            $"SQLite returned unknown image qualification status '{value}'."),
      };

  private static string Format(ImageCandidatePlatform value) =>
      value switch
      {
        ImageCandidatePlatform.LinuxAmd64 => "linux/amd64",
        ImageCandidatePlatform.LinuxArm64 => "linux/arm64",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
      };

  private static ImageCandidatePlatform ParsePlatform(string value) =>
      value switch
      {
        "linux/amd64" => ImageCandidatePlatform.LinuxAmd64,
        "linux/arm64" => ImageCandidatePlatform.LinuxArm64,
        _ => throw new InvalidOperationException(
            $"SQLite returned unknown image platform '{value}'."),
      };

  private static string Format(ImageCandidateOutputMode value) =>
      value switch
      {
        ImageCandidateOutputMode.Registry => "registry",
        ImageCandidateOutputMode.Oci => "oci",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
      };

  private static ImageCandidateOutputMode ParseOutputMode(string value) =>
      value switch
      {
        "registry" => ImageCandidateOutputMode.Registry,
        "oci" => ImageCandidateOutputMode.Oci,
        _ => throw new InvalidOperationException(
            $"SQLite returned unknown image output mode '{value}'."),
      };

  private const string RecipeSelectSql =
      """
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
      FROM image_recipe_versions
      """;

  private const string RequestSelectSql =
      """
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
      FROM image_build_requests
      """;
}
