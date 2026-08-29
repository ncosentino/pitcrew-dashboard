using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NexusLabs.Needlr;

using PitCrew.Dashboard.Features.Images.Abstractions;

namespace PitCrew.Dashboard.Features.Images;

[DoNotAutoRegister]
internal sealed partial class ImageBuildExecutionWorker(
    IImageCandidateStore _store,
    IGitHubImageWorkflowClient _gitHubClient,
    IOptions<ImageBuildExecutionOptions> _options,
    TimeProvider _timeProvider,
    ILogger<ImageBuildExecutionWorker> _logger) : BackgroundService
{
  private const int MaximumArtifactListSize = 100;
  private readonly string _leaseOwner = $"images-{Guid.NewGuid():N}";

  protected override async Task ExecuteAsync(
      CancellationToken stoppingToken)
  {
    using var timer = new PeriodicTimer(
        TimeSpan.FromSeconds(_options.Value.PollIntervalSeconds),
        _timeProvider);
    try
    {
      do
      {
        await ProcessOnceAsync(stoppingToken);
      }
      while (await timer.WaitForNextTickAsync(stoppingToken));
    }
    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
    {
    }
  }

  internal async Task<int> ProcessOnceAsync(
      CancellationToken cancellationToken)
  {
    var now = _timeProvider.GetUtcNow();
    var claims = await _store.ClaimDueBuildRequestsAsync(
        _leaseOwner,
        now,
        now.AddSeconds(_options.Value.ClaimLeaseSeconds),
        _options.Value.BatchSize,
        cancellationToken);
    foreach (var claim in claims)
    {
      await ProcessClaimAsync(claim, cancellationToken);
    }
    if (claims.Count > 0)
    {
      LogProcessedBatch(claims.Count);
    }
    return claims.Count;
  }

  private Task ProcessClaimAsync(
      ImageBuildExecutionClaim claim,
      CancellationToken cancellationToken) =>
      claim.Request.Status switch
      {
        ImageBuildRequestStatus.Requested =>
            DispatchAsync(claim, cancellationToken),
        ImageBuildRequestStatus.Dispatching
            when claim.DispatchSafeToRetry =>
                DispatchAsync(claim, cancellationToken),
        ImageBuildRequestStatus.Dispatching =>
            BlockAsync(
                claim,
                "dispatch-indeterminate",
                "Workflow dispatch may have been accepted; automatic redispatch is prohibited.",
                "dispatch-indeterminate",
                cancellationToken),
        ImageBuildRequestStatus.Building =>
            PollAsync(claim, cancellationToken),
        ImageBuildRequestStatus.Qualifying =>
            QualifyAsync(claim, cancellationToken),
        _ => Task.CompletedTask,
      };

  private async Task DispatchAsync(
      ImageBuildExecutionClaim claim,
      CancellationToken cancellationToken)
  {
    if (claim.Registration.DisabledAt is not null)
    {
      await BlockAsync(
          claim,
          "registration-disabled",
          "The frozen image recipe registration version is disabled.",
          "registration-disabled",
          cancellationToken);
      return;
    }

    var repository = new GitHubRepositoryIdentity(
        claim.Registration.GitHubRepositoryId,
        claim.Registration.RepositoryOwner,
        claim.Registration.RepositoryName);
    var authority = await ValidateDispatchAuthorityAsync(
        claim,
        repository,
        cancellationToken);
    if (!authority.Valid)
    {
      if (authority.Kind is GitHubClientOutcomeKind.RateLimited
          or GitHubClientOutcomeKind.TransientFailure
          or GitHubClientOutcomeKind.TimedOut)
      {
        var authorityNow = _timeProvider.GetUtcNow();
        var deferredAt = authority.RetryAt is { } retryAt
            ? Max(authorityNow, retryAt)!.Value
            : authorityNow.Add(CalculateBackoff(
                claim.DispatchAttempts + 1));
        await _store.DeferDispatchAuthorityAsync(
            claim.Request.TenantId,
            claim.Request.RequestId,
            claim.LeaseOwner,
            deferredAt,
            authority.ExternalStatus,
            authorityNow,
            cancellationToken);
      }
      else if (authority.Kind == GitHubClientOutcomeKind.Cancelled)
      {
      }
      else
      {
        await BlockAsync(
            claim,
            authority.Category,
            authority.Detail,
            authority.ExternalStatus,
            cancellationToken);
      }
      return;
    }

    var startedAt = _timeProvider.GetUtcNow();
    var started = await _store.MarkDispatchStartedAsync(
        claim.Request.TenantId,
        claim.Request.RequestId,
        claim.LeaseOwner,
        startedAt,
        cancellationToken);
    if (started is not ImageCandidateMutationResult.Succeeded)
    {
      return;
    }

    var inputs = new Dictionary<string, string>(
        ImageBuildRequestValidation.ReadDispatchInputs(
            claim.Request.InputValuesJson),
        StringComparer.Ordinal)
    {
      ["pitcrew_request_id"] = claim.Request.RequestId.ToString("D"),
      ["pitcrew_source_commit"] = claim.Request.SourceCommit,
      ["pitcrew_recipe_id"] = claim.Request.RecipeId,
    };
    var outcome = await _gitHubClient.DispatchWorkflowAsync(
        claim.Registration.GitHubInstallationId,
        repository,
        claim.Registration.GitHubWorkflowId,
        claim.Registration.DispatchRef,
        inputs,
        cancellationToken);
    var now = _timeProvider.GetUtcNow();
    switch (outcome.Kind)
    {
      case GitHubClientOutcomeKind.Success when outcome.Value is not null:
        await _store.RecordDispatchSucceededAsync(
            claim.Request.TenantId,
            claim.Request.RequestId,
            claim.LeaseOwner,
            outcome.Value.RunId,
            outcome.Value.RunApiUrl.AbsoluteUri,
            outcome.Value.RunHtmlUrl.AbsoluteUri,
            now.AddSeconds(_options.Value.PollIntervalSeconds),
            now,
            cancellationToken);
        break;
      case GitHubClientOutcomeKind.RateLimited:
        await _store.DeferRateLimitedDispatchAsync(
            claim.Request.TenantId,
            claim.Request.RequestId,
            claim.LeaseOwner,
            Max(now, outcome.RetryAt) ??
                now.AddSeconds(_options.Value.RetryBackoffSeconds),
            "dispatch-rate-limited",
            now,
            cancellationToken);
        break;
      case GitHubClientOutcomeKind.TransientFailure
          or GitHubClientOutcomeKind.TimedOut:
        await BlockAsync(
            claim,
            "dispatch-indeterminate",
            "Workflow dispatch may have been accepted; automatic redispatch is prohibited.",
            "dispatch-indeterminate",
            cancellationToken);
        break;
      case GitHubClientOutcomeKind.Cancelled
          when cancellationToken.IsCancellationRequested:
        break;
      default:
        await BlockAsync(
            claim,
            DispatchCategory(outcome.Kind),
            DispatchDetail(outcome.Kind),
            $"dispatch-{Format(outcome.Kind)}",
            cancellationToken);
        break;
    }
  }

  private async Task<DispatchAuthorityValidation>
      ValidateDispatchAuthorityAsync(
          ImageBuildExecutionClaim claim,
          GitHubRepositoryIdentity expectedRepository,
          CancellationToken cancellationToken)
  {
    var repositoryOutcome = await _gitHubClient.LoadRepositoryAsync(
        claim.Registration.GitHubInstallationId,
        claim.Registration.GitHubRepositoryId,
        cancellationToken);
    if (repositoryOutcome.Kind != GitHubClientOutcomeKind.Success)
    {
      return DispatchAuthorityValidation.Failed(
          repositoryOutcome.Kind,
          repositoryOutcome.RetryAt,
          "registration-authority-unavailable",
          "The frozen GitHub repository authority could not be revalidated.",
          $"dispatch-repository-{Format(repositoryOutcome.Kind)}");
    }
    if (repositoryOutcome.Value != expectedRepository)
    {
      return DispatchAuthorityValidation.Changed(
          "The frozen GitHub repository identity changed.");
    }

    var workflowOutcome = await _gitHubClient.LoadWorkflowAsync(
        claim.Registration.GitHubInstallationId,
        expectedRepository,
        claim.Registration.GitHubWorkflowId,
        cancellationToken);
    if (workflowOutcome.Kind != GitHubClientOutcomeKind.Success)
    {
      return DispatchAuthorityValidation.Failed(
          workflowOutcome.Kind,
          workflowOutcome.RetryAt,
          "registration-authority-unavailable",
          "The frozen GitHub workflow authority could not be revalidated.",
          $"dispatch-workflow-{Format(workflowOutcome.Kind)}");
    }
    if (workflowOutcome.Value is not { } workflow
        || workflow.Id != claim.Registration.GitHubWorkflowId
        || workflow.State != GitHubWorkflowState.Active
        || !string.Equals(
            workflow.Path,
            claim.Registration.WorkflowPath,
            StringComparison.Ordinal))
    {
      return DispatchAuthorityValidation.Changed(
          "The frozen GitHub workflow identity or activation state changed.");
    }

    var revisionOutcome =
        await _gitHubClient.LoadWorkflowFileRevisionAsync(
            claim.Registration.GitHubInstallationId,
            expectedRepository,
            claim.Registration.WorkflowPath,
            claim.Registration.DispatchRef,
            cancellationToken);
    if (revisionOutcome.Kind != GitHubClientOutcomeKind.Success)
    {
      return DispatchAuthorityValidation.Failed(
          revisionOutcome.Kind,
          revisionOutcome.RetryAt,
          "registration-authority-unavailable",
          "The frozen GitHub workflow revision could not be revalidated.",
          $"dispatch-revision-{Format(revisionOutcome.Kind)}");
    }
    if (revisionOutcome.Value is not { } revision
        || !string.Equals(
            revision.Path,
            claim.Registration.WorkflowPath,
            StringComparison.Ordinal)
        || !string.Equals(
            revision.Reference,
            claim.Registration.DispatchRef,
            StringComparison.Ordinal)
        || !string.Equals(
            revision.BlobSha,
            claim.Registration.WorkflowBlobSha,
            StringComparison.Ordinal))
    {
      return DispatchAuthorityValidation.Changed(
          "The frozen GitHub workflow revision changed.");
    }

    return DispatchAuthorityValidation.Succeeded;
  }

  private async Task PollAsync(
      ImageBuildExecutionClaim claim,
      CancellationToken cancellationToken)
  {
    if (claim.Request.GitHubRunId is null
        || claim.Request.GitHubRunApiUrl is null
        || claim.Request.GitHubRunUrl is null)
    {
      await BlockAsync(
          claim,
          "run-identity-missing",
          "The exact GitHub workflow run identity is incomplete.",
          "run-identity-missing",
          cancellationToken);
      return;
    }

    var repository = new GitHubRepositoryIdentity(
        claim.Registration.GitHubRepositoryId,
        claim.Registration.RepositoryOwner,
        claim.Registration.RepositoryName);
    var outcome = await _gitHubClient.LoadWorkflowRunAsync(
        claim.Registration.GitHubInstallationId,
        repository,
        claim.Request.GitHubRunId.Value,
        cancellationToken);
    var now = _timeProvider.GetUtcNow();
    if (outcome.Kind == GitHubClientOutcomeKind.Success
        && outcome.Value is { } run)
    {
      if (run.Id != claim.Request.GitHubRunId
          || run.WorkflowId != claim.Registration.GitHubWorkflowId
          || !string.Equals(
              run.Event,
              "workflow_dispatch",
              StringComparison.Ordinal)
          || !string.Equals(
              run.RunApiUrl.AbsoluteUri,
              claim.Request.GitHubRunApiUrl,
              StringComparison.Ordinal)
          || !string.Equals(
              run.RunHtmlUrl.AbsoluteUri,
              claim.Request.GitHubRunUrl,
              StringComparison.Ordinal))
      {
        await BlockAsync(
            claim,
            "run-identity-mismatch",
            "GitHub returned workflow run identity that does not match durable authority.",
            "run-identity-mismatch",
            cancellationToken);
        return;
      }

      if (run.Status is "queued" or "pending" or "in_progress" or "waiting")
      {
        await DeferRunPollAsync(
            claim,
            now.AddSeconds(_options.Value.PollIntervalSeconds),
            $"run-{run.Status}",
            ImageBuildNotFoundCounterAction.Reset,
            cancellationToken);
        return;
      }
      if (!string.Equals(
          run.Status,
          "completed",
          StringComparison.Ordinal))
      {
        await BlockAsync(
            claim,
            "run-status-invalid",
            "GitHub returned an unsupported workflow run status or conclusion.",
            "run-status-invalid",
            cancellationToken);
        return;
      }

      var observed = await _store.MarkBuildRunObservedAsync(
          claim.Request.TenantId,
          claim.Request.RequestId,
          claim.LeaseOwner,
          now,
          cancellationToken);
      if (observed is not ImageCandidateMutationResult.Succeeded)
      {
        return;
      }

      if (!await VerifyRunWorkflowRevisionAsync(
          claim,
          repository,
          run,
          now,
          cancellationToken))
      {
        return;
      }

      if (string.Equals(
          run.Conclusion,
          "success",
          StringComparison.Ordinal))
      {
        await _store.MarkBuildQualifyingAsync(
            claim.Request.TenantId,
            claim.Request.RequestId,
            claim.LeaseOwner,
            "run-completed-success",
            now,
            cancellationToken);
        return;
      }
      if (IsFailedConclusion(run.Conclusion))
      {
        await _store.TerminalizeBuildRequestAsync(
            claim.Request.TenantId,
            claim.Request.RequestId,
            claim.LeaseOwner,
            ImageBuildRequestStatus.Failed,
            $"workflow-{run.Conclusion}",
            FailureDetail(run.Conclusion),
            $"run-completed-{run.Conclusion}",
            now,
            cancellationToken);
        return;
      }

      await BlockAsync(
          claim,
          "run-status-invalid",
          "GitHub returned an unsupported workflow run status or conclusion.",
          "run-status-invalid",
          cancellationToken);
      return;
    }

    if (outcome.Kind == GitHubClientOutcomeKind.NotFound)
    {
      var exhausted =
          claim.RunNotFoundAttempts + 1 >=
              _options.Value.NotFoundMaximumAttempts
          && now - (claim.DispatchStartedAt
              ?? claim.Request.RequestedAt) >=
              TimeSpan.FromSeconds(_options.Value.NotFoundGraceSeconds);
      if (exhausted)
      {
        await BlockAsync(
            claim,
            "run-not-found",
            "The exact GitHub workflow run remained unavailable after the bounded grace period.",
            "run-not-found-exhausted",
            cancellationToken);
      }
      else
      {
        await DeferRunPollAsync(
            claim,
            now.Add(CalculateBackoff(
                claim.RunNotFoundAttempts + 1)),
            "run-not-found",
            ImageBuildNotFoundCounterAction.Increment,
            cancellationToken);
      }
      return;
    }

    if (outcome.Kind is GitHubClientOutcomeKind.RateLimited
        or GitHubClientOutcomeKind.TransientFailure
        or GitHubClientOutcomeKind.TimedOut)
    {
      await DeferRunPollAsync(
          claim,
          Max(now, outcome.RetryAt)
              ?? now.Add(CalculateBackoff(claim.PollAttempts + 1)),
          $"run-{Format(outcome.Kind)}",
          ImageBuildNotFoundCounterAction.Preserve,
          cancellationToken);
      return;
    }
    if (outcome.Kind == GitHubClientOutcomeKind.Cancelled
        && cancellationToken.IsCancellationRequested)
    {
      return;
    }

    await BlockAsync(
        claim,
        PollCategory(outcome.Kind),
        PollDetail(outcome.Kind),
        $"run-{Format(outcome.Kind)}",
        cancellationToken);
  }

  private async Task<bool> VerifyRunWorkflowRevisionAsync(
      ImageBuildExecutionClaim claim,
      GitHubRepositoryIdentity repository,
      GitHubWorkflowRun run,
      DateTimeOffset now,
      CancellationToken cancellationToken)
  {
    if (!IsCanonicalSha1(run.HeadSha))
    {
      await BlockAsync(
          claim,
          "run-revision-invalid",
          "GitHub returned an invalid workflow execution revision.",
          "run-revision-invalid",
          cancellationToken);
      return false;
    }

    var outcome =
        await _gitHubClient.LoadWorkflowFileRevisionAtCommitAsync(
            claim.Registration.GitHubInstallationId,
            repository,
            claim.Registration.WorkflowPath,
            run.HeadSha,
            cancellationToken);
    if (outcome.Kind == GitHubClientOutcomeKind.Success
        && outcome.Value is { } revision)
    {
      if (string.Equals(
              revision.Path,
              claim.Registration.WorkflowPath,
              StringComparison.Ordinal)
          && string.Equals(
              revision.Reference,
              run.HeadSha,
              StringComparison.Ordinal)
          && string.Equals(
              revision.BlobSha,
              claim.Registration.WorkflowBlobSha,
              StringComparison.Ordinal))
      {
        var observed =
            await _store.MarkBuildRevisionObservedAsync(
                claim.Request.TenantId,
                claim.Request.RequestId,
                claim.LeaseOwner,
                now,
                cancellationToken);
        return observed is ImageCandidateMutationResult.Succeeded;
      }

      await BlockAsync(
          claim,
          "run-revision-mismatch",
          "The workflow executed by GitHub does not match the frozen reviewed revision.",
          "run-revision-mismatch",
          cancellationToken);
      return false;
    }

    if (outcome.Kind == GitHubClientOutcomeKind.NotFound)
    {
      var exhausted =
          claim.RevisionNotFoundAttempts + 1 >=
              _options.Value.NotFoundMaximumAttempts
          && now - (claim.DispatchStartedAt
              ?? claim.Request.RequestedAt) >=
              TimeSpan.FromSeconds(_options.Value.NotFoundGraceSeconds);
      if (exhausted)
      {
        await BlockAsync(
            claim,
            "run-revision-not-found",
            "The exact workflow execution revision remained unavailable after the bounded grace period.",
            "run-revision-not-found-exhausted",
            cancellationToken);
      }
      else
      {
        await DeferRevisionPollAsync(
            claim,
            now.Add(CalculateBackoff(
                claim.RevisionNotFoundAttempts + 1)),
            "run-revision-not-found",
            ImageBuildNotFoundCounterAction.Increment,
            cancellationToken);
      }
      return false;
    }

    if (outcome.Kind is GitHubClientOutcomeKind.RateLimited
        or GitHubClientOutcomeKind.TransientFailure
        or GitHubClientOutcomeKind.TimedOut)
    {
      await DeferRevisionPollAsync(
          claim,
          Max(now, outcome.RetryAt)
              ?? now.Add(CalculateBackoff(claim.PollAttempts + 1)),
          $"run-revision-{Format(outcome.Kind)}",
          ImageBuildNotFoundCounterAction.Preserve,
          cancellationToken);
      return false;
    }
    if (outcome.Kind == GitHubClientOutcomeKind.Cancelled)
    {
      return false;
    }

    await BlockAsync(
        claim,
        "run-revision-invalid",
        "The exact workflow execution revision could not be verified.",
        $"run-revision-{Format(outcome.Kind)}",
        cancellationToken);
    return false;
  }

  private async Task QualifyAsync(
      ImageBuildExecutionClaim claim,
      CancellationToken cancellationToken)
  {
    if (claim.Request.GitHubRunId is null)
    {
      await BlockAsync(
          claim,
          "candidate-run-identity-missing",
          "The exact GitHub workflow run identity is unavailable for qualification.",
          "candidate-run-identity-missing",
          cancellationToken);
      return;
    }

    var repository = new GitHubRepositoryIdentity(
        claim.Registration.GitHubRepositoryId,
        claim.Registration.RepositoryOwner,
        claim.Registration.RepositoryName);
    var artifactsOutcome =
        await _gitHubClient.ListWorkflowRunArtifactsAsync(
            claim.Registration.GitHubInstallationId,
            repository,
            claim.Request.GitHubRunId.Value,
            MaximumArtifactListSize,
            cancellationToken);
    if (await HandleRetryableQualificationOutcomeAsync(
            claim,
            artifactsOutcome.Kind,
            artifactsOutcome.RetryAt,
            "candidate-artifacts",
            cancellationToken))
    {
      return;
    }
    if (artifactsOutcome.Kind == GitHubClientOutcomeKind.Cancelled &&
        cancellationToken.IsCancellationRequested)
    {
      return;
    }
    if (artifactsOutcome.Kind != GitHubClientOutcomeKind.Success ||
        artifactsOutcome.Value is null)
    {
      await BlockAsync(
          claim,
          QualificationCategory(
              "candidate-artifact-list",
              artifactsOutcome.Kind),
          QualificationDetail(
              "candidate artifact metadata",
              artifactsOutcome.Kind),
          $"candidate-artifacts-{Format(artifactsOutcome.Kind)}",
          cancellationToken);
      return;
    }

    var artifacts = artifactsOutcome.Value;
    if (artifacts.TotalCount != artifacts.Artifacts.Count)
    {
      await BlockAsync(
          claim,
          "candidate-artifact-set-unbounded",
          "The exact workflow run has more artifacts than Dashboard can inspect safely.",
          "candidate-artifact-set-unbounded",
          cancellationToken);
      return;
    }

    var candidates = artifacts.Artifacts
        .Where(artifact => string.Equals(
            artifact.Name,
            ImageCandidateArchiveParser.ArtifactName,
            StringComparison.Ordinal))
        .ToArray();
    if (candidates.Length == 0)
    {
      await _store.TerminalizeBuildRequestAsync(
          claim.Request.TenantId,
          claim.Request.RequestId,
          claim.LeaseOwner,
          ImageBuildRequestStatus.Failed,
          "candidate-artifact-missing",
          "The required candidate artifact was not published.",
          "candidate-artifact-missing",
          _timeProvider.GetUtcNow(),
          cancellationToken);
      return;
    }
    if (candidates.Length != 1)
    {
      await BlockAsync(
          claim,
          "candidate-artifact-ambiguous",
          "The exact workflow run published duplicate candidate artifacts.",
          "candidate-artifact-ambiguous",
          cancellationToken);
      return;
    }

    var artifact = candidates[0];
    var now = _timeProvider.GetUtcNow();
    if (artifact.Expired || artifact.ExpiresAt <= now)
    {
      await BlockAsync(
          claim,
          "candidate-artifact-expired",
          "The exact candidate artifact expired before it could be validated.",
          "candidate-artifact-expired",
          cancellationToken);
      return;
    }

    var archiveOutcome =
        await _gitHubClient.DownloadWorkflowArtifactArchiveAsync(
            claim.Registration.GitHubInstallationId,
            repository,
            artifact,
            _options.Value.MaximumArtifactArchiveBytes,
            cancellationToken);
    if (await HandleRetryableQualificationOutcomeAsync(
            claim,
            archiveOutcome.Kind,
            archiveOutcome.RetryAt,
            "candidate-archive",
            cancellationToken))
    {
      return;
    }
    if (archiveOutcome.Kind == GitHubClientOutcomeKind.Cancelled &&
        cancellationToken.IsCancellationRequested)
    {
      return;
    }
    if (archiveOutcome.Kind != GitHubClientOutcomeKind.Success ||
        archiveOutcome.Value is null)
    {
      await BlockAsync(
          claim,
          QualificationCategory(
              "candidate-artifact-download",
              archiveOutcome.Kind),
          QualificationDetail(
              "candidate artifact archive",
              archiveOutcome.Kind),
          $"candidate-archive-{Format(archiveOutcome.Kind)}",
          cancellationToken);
      return;
    }

    var parsed = ImageCandidateArchiveParser.Parse(
        claim,
        artifact,
        archiveOutcome.Value,
        _options.Value.MaximumArtifactArchiveBytes,
        _options.Value.MaximumCandidateReportBytes);
    if (!parsed.Succeeded ||
        parsed.Candidate is null)
    {
      await BlockAsync(
          claim,
          parsed.ErrorCode ?? "candidate-report-invalid",
          parsed.ErrorDetail ??
              "The candidate report does not satisfy the trusted contract.",
          parsed.ErrorCode ?? "candidate-report-invalid",
          cancellationToken);
      return;
    }

    var stored = await _store.StoreCandidateAsync(
        claim.Request.TenantId,
        parsed.Candidate,
        parsed.Qualifications,
        cancellationToken);
    if (stored is ImageCandidateMutationResult.Succeeded
        or ImageCandidateMutationResult.Unchanged
        or ImageCandidateMutationResult.NotFound)
    {
      return;
    }

    await BlockAsync(
        claim,
        "candidate-persistence-conflict",
        "The validated candidate could not be committed to durable state.",
        "candidate-persistence-conflict",
        cancellationToken);
  }

  private async Task<bool> HandleRetryableQualificationOutcomeAsync(
      ImageBuildExecutionClaim claim,
      GitHubClientOutcomeKind kind,
      DateTimeOffset? retryAt,
      string externalStatusPrefix,
      CancellationToken cancellationToken)
  {
    if (kind is not (
        GitHubClientOutcomeKind.RateLimited or
        GitHubClientOutcomeKind.TransientFailure or
        GitHubClientOutcomeKind.TimedOut))
    {
      return false;
    }

    var now = _timeProvider.GetUtcNow();
    await _store.DeferCandidateQualificationAsync(
        claim.Request.TenantId,
        claim.Request.RequestId,
        claim.LeaseOwner,
        Max(now, retryAt)
            ?? now.Add(CalculateBackoff(claim.PollAttempts + 1)),
        $"{externalStatusPrefix}-{Format(kind)}",
        now,
        cancellationToken);
    return true;
  }

  private Task DeferRunPollAsync(
      ImageBuildExecutionClaim claim,
      DateTimeOffset nextPollAt,
      string externalStatus,
      ImageBuildNotFoundCounterAction notFoundCounterAction,
      CancellationToken cancellationToken) =>
      _store.DeferBuildRunPollAsync(
          claim.Request.TenantId,
          claim.Request.RequestId,
          claim.LeaseOwner,
          nextPollAt,
          externalStatus,
          notFoundCounterAction,
          _timeProvider.GetUtcNow(),
          cancellationToken);

  private Task DeferRevisionPollAsync(
      ImageBuildExecutionClaim claim,
      DateTimeOffset nextPollAt,
      string externalStatus,
      ImageBuildNotFoundCounterAction notFoundCounterAction,
      CancellationToken cancellationToken) =>
      _store.DeferBuildRevisionPollAsync(
          claim.Request.TenantId,
          claim.Request.RequestId,
          claim.LeaseOwner,
          nextPollAt,
          externalStatus,
          notFoundCounterAction,
          _timeProvider.GetUtcNow(),
          cancellationToken);

  private Task BlockAsync(
      ImageBuildExecutionClaim claim,
      string category,
      string detail,
      string externalStatus,
      CancellationToken cancellationToken) =>
      _store.TerminalizeBuildRequestAsync(
          claim.Request.TenantId,
          claim.Request.RequestId,
          claim.LeaseOwner,
          ImageBuildRequestStatus.Blocked,
          category,
          detail,
          externalStatus,
          _timeProvider.GetUtcNow(),
          cancellationToken);

  private TimeSpan CalculateBackoff(int attempts)
  {
    var exponent = Math.Clamp(attempts - 1, 0, 10);
    var seconds = _options.Value.RetryBackoffSeconds * (1 << exponent);
    return TimeSpan.FromSeconds(Math.Min(
        seconds,
        _options.Value.MaximumRetryBackoffSeconds));
  }

  private static DateTimeOffset? Max(
      DateTimeOffset now,
      DateTimeOffset? retryAt) =>
      retryAt is null ? null : retryAt > now ? retryAt : now;

  private static bool IsFailedConclusion(string? conclusion) =>
      conclusion is "failure"
          or "cancelled"
          or "timed_out"
          or "action_required"
          or "stale"
          or "startup_failure"
          or "neutral"
          or "skipped";

  private static bool IsCanonicalSha1(string? value) =>
      value is { Length: 40 } &&
      value.All(static character =>
          character is >= '0' and <= '9' or >= 'a' and <= 'f');

  private static string FailureDetail(string? conclusion) =>
      conclusion switch
      {
        "failure" => "The trusted workflow reported failure.",
        "cancelled" => "The trusted workflow was cancelled.",
        "timed_out" => "The trusted workflow timed out.",
        "action_required" => "The trusted workflow requires action.",
        "stale" => "The trusted workflow became stale.",
        "startup_failure" => "The trusted workflow failed during startup.",
        "neutral" => "The trusted workflow completed neutrally.",
        "skipped" => "The trusted workflow was skipped.",
        _ => "The trusted workflow ended unsuccessfully.",
      };

  private static string DispatchCategory(GitHubClientOutcomeKind kind) =>
      kind switch
      {
        GitHubClientOutcomeKind.NotConfigured =>
            "integration-not-configured",
        GitHubClientOutcomeKind.UnauthorizedOrForbidden =>
            "dispatch-forbidden",
        GitHubClientOutcomeKind.NotFound => "dispatch-authority-not-found",
        GitHubClientOutcomeKind.InvalidRequest => "dispatch-request-invalid",
        GitHubClientOutcomeKind.InvalidResponse => "dispatch-response-invalid",
        _ => "dispatch-failed",
      };

  private static string DispatchDetail(GitHubClientOutcomeKind kind) =>
      kind switch
      {
        GitHubClientOutcomeKind.NotConfigured =>
            "Trusted GitHub image execution is not configured.",
        GitHubClientOutcomeKind.UnauthorizedOrForbidden =>
            "The GitHub installation cannot dispatch the frozen workflow.",
        GitHubClientOutcomeKind.NotFound =>
            "The frozen GitHub workflow authority was not found.",
        GitHubClientOutcomeKind.InvalidRequest =>
            "The frozen workflow dispatch request is invalid.",
        GitHubClientOutcomeKind.InvalidResponse =>
            "GitHub omitted the exact accepted workflow run identity.",
        _ => "The frozen workflow dispatch could not be completed.",
      };

  private static string PollCategory(GitHubClientOutcomeKind kind) =>
      kind switch
      {
        GitHubClientOutcomeKind.NotConfigured =>
            "integration-not-configured",
        GitHubClientOutcomeKind.UnauthorizedOrForbidden =>
            "run-poll-forbidden",
        GitHubClientOutcomeKind.InvalidRequest => "run-poll-invalid",
        GitHubClientOutcomeKind.InvalidResponse => "run-response-invalid",
        _ => "run-poll-blocked",
      };

  private static string PollDetail(GitHubClientOutcomeKind kind) =>
      kind switch
      {
        GitHubClientOutcomeKind.NotConfigured =>
            "Trusted GitHub image execution is not configured.",
        GitHubClientOutcomeKind.UnauthorizedOrForbidden =>
            "The GitHub installation cannot read the exact workflow run.",
        GitHubClientOutcomeKind.InvalidRequest =>
            "The exact workflow run polling request is invalid.",
        GitHubClientOutcomeKind.InvalidResponse =>
            "GitHub returned an invalid exact workflow run response.",
        _ => "The exact workflow run cannot be polled safely.",
      };

  private static string QualificationCategory(
      string prefix,
      GitHubClientOutcomeKind kind) =>
      kind switch
      {
        GitHubClientOutcomeKind.NotConfigured =>
            "integration-not-configured",
        GitHubClientOutcomeKind.UnauthorizedOrForbidden =>
            $"{prefix}-forbidden",
        GitHubClientOutcomeKind.NotFound =>
            $"{prefix}-not-found",
        GitHubClientOutcomeKind.InvalidRequest =>
            $"{prefix}-request-invalid",
        GitHubClientOutcomeKind.InvalidResponse =>
            $"{prefix}-response-invalid",
        _ => $"{prefix}-blocked",
      };

  private static string QualificationDetail(
      string evidenceName,
      GitHubClientOutcomeKind kind) =>
      kind switch
      {
        GitHubClientOutcomeKind.NotConfigured =>
            "Trusted GitHub image execution is not configured.",
        GitHubClientOutcomeKind.UnauthorizedOrForbidden =>
            $"The GitHub installation cannot read the exact {evidenceName}.",
        GitHubClientOutcomeKind.NotFound =>
            $"The exact {evidenceName} was not found.",
        GitHubClientOutcomeKind.InvalidRequest =>
            $"The exact {evidenceName} request is invalid.",
        GitHubClientOutcomeKind.InvalidResponse =>
            $"GitHub returned invalid {evidenceName}.",
        _ => $"The exact {evidenceName} cannot be processed safely.",
      };

  private static string Format(GitHubClientOutcomeKind kind) =>
      kind.ToString().ToLowerInvariant();

  [LoggerMessage(
      EventId = 1,
      Level = LogLevel.Debug,
      Message = "Processed {RequestCount} trusted image build requests.")]
  private partial void LogProcessedBatch(int requestCount);

  private sealed record DispatchAuthorityValidation(
      bool Valid,
      GitHubClientOutcomeKind Kind,
      DateTimeOffset? RetryAt,
      string Category,
      string Detail,
      string ExternalStatus)
  {
    public static DispatchAuthorityValidation Succeeded { get; } =
        new(
            true,
            GitHubClientOutcomeKind.Success,
            null,
            string.Empty,
            string.Empty,
            "dispatch-authority-valid");

    public static DispatchAuthorityValidation Failed(
        GitHubClientOutcomeKind kind,
        DateTimeOffset? retryAt,
        string category,
        string detail,
        string externalStatus) =>
        new(
            false,
            kind,
            retryAt,
            category,
            detail,
            externalStatus);

    public static DispatchAuthorityValidation Changed(string detail) =>
        new(
            false,
            GitHubClientOutcomeKind.InvalidResponse,
            null,
            "registration-changed",
            detail,
            "dispatch-registration-changed");
  }
}
