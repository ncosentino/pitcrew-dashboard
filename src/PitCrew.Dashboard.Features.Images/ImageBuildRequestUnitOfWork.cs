using System.Security.Claims;

using PitCrew.Dashboard.Features.Access.Abstractions;
using PitCrew.Dashboard.Features.Images.Abstractions;
using PitCrew.Dashboard.Kernel.Authentication;

namespace PitCrew.Dashboard.Features.Images;

internal sealed class ImageBuildRequestUnitOfWork(
    IImageCandidateStore _imageCandidateStore,
    IAccessStore _accessStore,
    IAuthenticatedDashboardUserAccessor _userAccessor,
    IGitHubImageWorkflowClient _gitHubImageWorkflowClient,
    TimeProvider _timeProvider) : IImageBuildRequestUnitOfWork
{
  public async Task<ImageBuildRequestCommandResult> CreateAsync(
      ClaimsPrincipal principal,
      string tenantId,
      RequestImageBuildInput input,
      CancellationToken cancellationToken)
  {
    var now = _timeProvider.GetUtcNow();
    var actor = await GetActorOrNullAsync(
        principal,
        now,
        cancellationToken);
    if (actor is null)
    {
      return Failure(
          ImageBuildRequestCommandStatus.Forbidden,
          "forbidden_image_build_request",
          "The image build request is not authorized.");
    }

    if (input.RequestId == Guid.Empty
        || input.RegistrationId == Guid.Empty
        || input.RegistrationVersion < 1)
    {
      return Invalid(
          "Request ID and registration ID must be non-empty GUIDs and registration version must be positive.");
    }

    var registration =
        await _imageCandidateStore.GetRecipeVersionOrNullAsync(
            tenantId,
            input.RegistrationId,
            input.RegistrationVersion,
            cancellationToken);
    if (registration is null)
    {
      return Failure(
          ImageBuildRequestCommandStatus.NotFound,
          "image_recipe_registration_not_found",
          "The exact image recipe registration version was not found.");
    }

    if (!ImageBuildRequestValidation.Canonicalize(
            input,
            registration,
            out var inputValuesJson,
            out var inputValuesSha256,
            out var error))
    {
      return Invalid(
          error ?? "The image build request is invalid.");
    }

    var existing = await _imageCandidateStore.GetBuildRequestOrNullAsync(
        tenantId,
        input.RequestId,
        cancellationToken);
    if (existing is not null)
    {
      return ImageBuildRequestValidation.MatchesReplay(
              existing,
              input,
              inputValuesJson!)
          ? new ImageBuildRequestCommandResult(
              ImageBuildRequestCommandStatus.Unchanged,
              null,
              null,
              existing,
              null)
          : Failure(
              ImageBuildRequestCommandStatus.Conflict,
              "image_build_request_conflict",
              "The request ID is already bound to different build authority.");
    }

    if (registration.DisabledAt is not null)
    {
      return Failure(
          ImageBuildRequestCommandStatus.Conflict,
          "image_recipe_registration_disabled",
          "The exact image recipe registration version is disabled.");
    }

    var repository = new GitHubRepositoryIdentity(
        registration.GitHubRepositoryId,
        registration.RepositoryOwner,
        registration.RepositoryName);
    var commitOutcome = await _gitHubImageWorkflowClient.ResolveCommitAsync(
        registration.GitHubInstallationId,
        repository,
        input.SourceCommit,
        cancellationToken);
    if (commitOutcome.Kind != GitHubClientOutcomeKind.Success)
    {
      return MapGitHubFailure(
          commitOutcome,
          "github_source_commit_not_found",
          "The exact source commit was not found.");
    }
    if (!string.Equals(
            commitOutcome.Value?.Sha,
            input.SourceCommit,
            StringComparison.Ordinal))
    {
      return Failure(
          ImageBuildRequestCommandStatus.Conflict,
          "github_source_commit_identity_mismatch",
          "GitHub returned a different source commit identity.");
    }

    var reachabilityOutcome =
        await _gitHubImageWorkflowClient.VerifyCommitReachableAsync(
            registration.GitHubInstallationId,
            repository,
            input.SourceCommit,
            input.SourceRef,
            cancellationToken);
    if (reachabilityOutcome.Kind != GitHubClientOutcomeKind.Success)
    {
      return MapGitHubFailure(
          reachabilityOutcome,
          "github_source_ref_not_found",
          "The exact allowed source ref was not found.");
    }
    if (reachabilityOutcome.Value?.IsReachable != true)
    {
      return Failure(
          ImageBuildRequestCommandStatus.Conflict,
          "github_source_commit_not_reachable",
          "The source commit is not reachable from the exact allowed source ref.");
    }

    var request = new ImageBuildRequest(
        tenantId,
        input.RequestId,
        registration.RegistrationId,
        registration.Version,
        registration.RecipeId,
        $"{registration.RepositoryOwner}/{registration.RepositoryName}",
        input.SourceCommit,
        inputValuesJson!,
        inputValuesSha256!,
        actor.GitHubUserId,
        now,
        ImageBuildRequestStatus.Requested,
        null,
        null,
        null,
        null,
        now,
        input.SourceRef,
        null);
    var result = await _imageCandidateStore.CreateBuildRequestAsync(
        request,
        cancellationToken);
    if (result == ImageCandidateMutationResult.Succeeded)
    {
      return new ImageBuildRequestCommandResult(
          ImageBuildRequestCommandStatus.Succeeded,
          null,
          null,
          request,
          null);
    }

    var durable = await _imageCandidateStore.GetBuildRequestOrNullAsync(
        tenantId,
        input.RequestId,
        cancellationToken);
    return durable is not null
        && ImageBuildRequestValidation.MatchesReplay(
            durable,
            input,
            inputValuesJson!)
        ? new ImageBuildRequestCommandResult(
            ImageBuildRequestCommandStatus.Unchanged,
            null,
            null,
            durable,
            null)
        : Failure(
            ImageBuildRequestCommandStatus.Conflict,
            "image_build_request_conflict",
            "The request ID is already bound to different build authority.");
  }

  public Task<IReadOnlyList<ImageBuildRequest>> ListAsync(
      string tenantId,
      int limit,
      CancellationToken cancellationToken) =>
      _imageCandidateStore.ListBuildRequestsAsync(
          tenantId,
          null,
          limit,
          cancellationToken);

  public Task<ImageBuildRequest?> GetOrNullAsync(
      string tenantId,
      Guid requestId,
      CancellationToken cancellationToken) =>
      _imageCandidateStore.GetBuildRequestOrNullAsync(
          tenantId,
          requestId,
          cancellationToken);

  private async Task<DashboardUser?> GetActorOrNullAsync(
      ClaimsPrincipal principal,
      DateTimeOffset observedAt,
      CancellationToken cancellationToken)
  {
    var authenticated = _userAccessor.GetOrNull(principal);
    if (authenticated is null)
    {
      return null;
    }

    var actor = new DashboardUser(
        authenticated.GitHubUserId,
        authenticated.GitHubLogin,
        authenticated.DisplayName,
        authenticated.AvatarUrl);
    await _accessStore.UpsertUserAsync(
        actor,
        observedAt,
        cancellationToken);
    return actor;
  }

  private static ImageBuildRequestCommandResult MapGitHubFailure<T>(
      GitHubClientOutcome<T> outcome,
      string notFoundCode,
      string notFoundError) =>
      outcome.Kind switch
      {
        GitHubClientOutcomeKind.InvalidRequest => Invalid(
            "The source commit or source ref is invalid."),
        GitHubClientOutcomeKind.NotFound => Failure(
            ImageBuildRequestCommandStatus.NotFound,
            notFoundCode,
            notFoundError),
        GitHubClientOutcomeKind.UnauthorizedOrForbidden => Failure(
            ImageBuildRequestCommandStatus.Forbidden,
            "github_source_validation_forbidden",
            "The GitHub installation could not validate the requested source."),
        GitHubClientOutcomeKind.NotConfigured => Failure(
            ImageBuildRequestCommandStatus.NotConfigured,
            "github_image_integration_not_configured",
            "Trusted GitHub image execution is not configured."),
        GitHubClientOutcomeKind.RateLimited => new ImageBuildRequestCommandResult(
            ImageBuildRequestCommandStatus.RateLimited,
            "github_image_integration_rate_limited",
            "GitHub source validation is temporarily rate-limited.",
            null,
            outcome.RetryAt),
        _ => Failure(
            ImageBuildRequestCommandStatus.Unavailable,
            "github_image_integration_unavailable",
            "GitHub source validation is temporarily unavailable."),
      };

  private static ImageBuildRequestCommandResult Invalid(string error) =>
      Failure(
          ImageBuildRequestCommandStatus.Invalid,
          "invalid_image_build_request",
          error);

  private static ImageBuildRequestCommandResult Failure(
      ImageBuildRequestCommandStatus status,
      string code,
      string error) =>
      new(
          status,
          code,
          error,
          null,
          null);
}
