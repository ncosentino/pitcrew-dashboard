using System.Security.Claims;

using PitCrew.Dashboard.Features.Access.Abstractions;
using PitCrew.Dashboard.Features.Images.Abstractions;
using PitCrew.Dashboard.Kernel.Authentication;

namespace PitCrew.Dashboard.Features.Images;

internal sealed class ImageRecipeRegistrationUnitOfWork(
    IImageCandidateStore _imageCandidateStore,
    IAccessStore _accessStore,
    IAuthenticatedDashboardUserAccessor _userAccessor,
    IGitHubImageWorkflowClient _gitHubImageWorkflowClient,
    TimeProvider _timeProvider) : IImageRecipeRegistrationUnitOfWork
{
  private const int MaximumCreateAttempts = 16;

  public async Task<RegisterImageRecipeCommandResult> CreateAsync(
      ClaimsPrincipal principal,
      string tenantId,
      RegisterImageRecipeInput input,
      CancellationToken cancellationToken)
  {
    if (!ImageRecipeRegistrationValidation.Canonicalize(
            input,
            out var canonical,
            out var error))
    {
      return Invalid(
          "invalid_image_recipe_registration",
          error ??
          "The image recipe registration request is invalid.");
    }

    var now = _timeProvider.GetUtcNow();
    var actor = await GetActorOrNullAsync(
        principal,
        now,
        cancellationToken);
    if (actor is null)
    {
      return Forbidden(
          "forbidden_image_recipe_registration",
          "The image recipe registration request is not authorized.");
    }

    var existing = await _imageCandidateStore.GetRecipeRegistrationOrNullAsync(
        tenantId,
        canonical!.RegistrationId,
        cancellationToken);
    if (existing is not null)
    {
      return ImageRecipeRegistrationValidation.MatchesDurableRegistrationRequest(
              existing,
              canonical)
          ? Unchanged(existing)
          : Conflict(
              "image_recipe_registration_conflict",
              "The image recipe registration ID is already bound to different request authority.");
    }

    var repositoryOutcome =
        await _gitHubImageWorkflowClient.LoadRepositoryAsync(
            canonical!.GitHubInstallationId,
            canonical.GitHubRepositoryId,
            cancellationToken);
    if (repositoryOutcome.Kind != GitHubClientOutcomeKind.Success)
    {
      return MapRepositoryFailure(repositoryOutcome);
    }

    var repository = repositoryOutcome.Value ??
        throw new InvalidOperationException(
            "A successful repository outcome omitted its value.");
    var workflowOutcome =
        await _gitHubImageWorkflowClient.LoadWorkflowAsync(
            canonical.GitHubInstallationId,
            repository,
            canonical.GitHubWorkflowId,
            cancellationToken);
    if (workflowOutcome.Kind != GitHubClientOutcomeKind.Success)
    {
      return MapWorkflowFailure(workflowOutcome);
    }

    var workflow = workflowOutcome.Value ??
        throw new InvalidOperationException(
            "A successful workflow outcome omitted its value.");
    var revisionOutcome =
        await _gitHubImageWorkflowClient.LoadWorkflowFileRevisionAsync(
            canonical.GitHubInstallationId,
            repository,
            workflow.Path,
            canonical.DispatchRef,
            cancellationToken);
    if (revisionOutcome.Kind != GitHubClientOutcomeKind.Success)
    {
      return MapRevisionFailure(revisionOutcome);
    }

    var revision = revisionOutcome.Value ??
        throw new InvalidOperationException(
            "A successful workflow revision outcome omitted its value.");
    var resolvedAuthority =
        ValidateResolvedAuthority(
            canonical,
            repository,
            workflow,
            revision);
    if (resolvedAuthority is not null)
    {
      return resolvedAuthority;
    }
    var workflowContentOutcome =
        await _gitHubImageWorkflowClient.LoadWorkflowFileContentAsync(
            canonical.GitHubInstallationId,
            repository,
            revision,
            cancellationToken);
    if (workflowContentOutcome.Kind != GitHubClientOutcomeKind.Success)
    {
      return MapWorkflowContentFailure(workflowContentOutcome);
    }

    var workflowContent = workflowContentOutcome.Value ??
        throw new InvalidOperationException(
            "A successful workflow content outcome omitted its value.");
    if (!ImageRecipeWorkflowDefinitionParser.Validate(
            canonical,
            workflowContent,
            out var workflowDefinitionCode,
            out var workflowDefinitionError))
    {
      return Conflict(
          workflowDefinitionCode ??
          "github_workflow_definition_invalid",
          workflowDefinitionError ??
          "The reviewed GitHub workflow file does not match the canonical image recipe schema.");
    }

    var version = await GetNextVersionAsync(
        tenantId,
        canonical.RecipeId,
        cancellationToken);
    for (var attempt = 0;
        attempt < MaximumCreateAttempts;
        attempt++)
    {
      var registration =
          ImageRecipeRegistrationValidation.CreateRegistration(
              tenantId,
              version,
              actor.GitHubUserId,
              now,
              canonical,
              repository,
              revision);
      var created = await _imageCandidateStore.CreateRecipeVersionAsync(
          registration,
          cancellationToken);
      if (created == ImageCandidateMutationResult.Succeeded)
      {
        return Succeeded(registration);
      }

      var durable =
          await _imageCandidateStore.GetRecipeRegistrationOrNullAsync(
              tenantId,
              canonical.RegistrationId,
              cancellationToken);
      if (durable is not null)
      {
        return ImageRecipeRegistrationValidation.MatchesDurableRegistrationRequest(
                durable,
                canonical)
            ? Unchanged(durable)
            : Conflict(
                "image_recipe_registration_conflict",
                "The image recipe registration ID is already bound to different request authority.");
      }

      var latestVersion = await GetLatestVersionOrZeroAsync(
          tenantId,
          canonical.RecipeId,
          cancellationToken);
      if (latestVersion >= version)
      {
        version = latestVersion + 1;
        continue;
      }

      return Conflict(
          "image_recipe_registration_conflict",
          "The image recipe registration could not be persisted.");
    }

    return Conflict(
        "image_recipe_registration_conflict",
        "The image recipe registration could not be persisted after retrying concurrent version allocation.");
  }

  public async Task<ImageRecipeRegistrationPage> ListAsync(
      string tenantId,
      bool includeDisabled,
      int limit,
      CancellationToken cancellationToken)
  {
    var registrations =
        await _imageCandidateStore.ListRecipeRegistrationsAsync(
            tenantId,
            includeDisabled,
            limit + 1,
            cancellationToken);
    return CreatePage(
        registrations,
        limit);
  }

  public Task<ImageRecipeRegistration?> GetOrNullAsync(
      string tenantId,
      Guid registrationId,
      CancellationToken cancellationToken) =>
      _imageCandidateStore.GetRecipeRegistrationOrNullAsync(
          tenantId,
          registrationId,
          cancellationToken);

  public async Task<DisableImageRecipeRegistrationStatus> DisableAsync(
      ClaimsPrincipal principal,
      string tenantId,
      Guid registrationId,
      CancellationToken cancellationToken)
  {
    var now = _timeProvider.GetUtcNow();
    var actor = await GetActorOrNullAsync(
        principal,
        now,
        cancellationToken);
    if (actor is null)
    {
      return DisableImageRecipeRegistrationStatus.Forbidden;
    }

    var disabled = await _imageCandidateStore.DisableRecipeRegistrationAsync(
        tenantId,
        registrationId,
        actor.GitHubUserId,
        now,
        cancellationToken);
    return disabled switch
    {
      ImageCandidateMutationResult.Succeeded or
          ImageCandidateMutationResult.Unchanged =>
              DisableImageRecipeRegistrationStatus.Succeeded,
      ImageCandidateMutationResult.NotFound =>
          DisableImageRecipeRegistrationStatus.NotFound,
      _ => DisableImageRecipeRegistrationStatus.Conflict,
    };
  }

  private async Task<int> GetNextVersionAsync(
      string tenantId,
      string recipeId,
      CancellationToken cancellationToken) =>
      await GetLatestVersionOrZeroAsync(
          tenantId,
          recipeId,
          cancellationToken) + 1;

  private async Task<int> GetLatestVersionOrZeroAsync(
      string tenantId,
      string recipeId,
      CancellationToken cancellationToken)
  {
    var versions = await _imageCandidateStore.ListRecipeVersionsAsync(
        tenantId,
        recipeId,
        1,
        cancellationToken);
    return versions.FirstOrDefault()?.Version ?? 0;
  }

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

  private static ImageRecipeRegistrationPage CreatePage(
      IReadOnlyList<ImageRecipeRegistration> registrations,
      int limit) =>
      registrations.Count > limit
          ? new ImageRecipeRegistrationPage(
              registrations.Take(limit).ToArray(),
              true)
          : new ImageRecipeRegistrationPage(
              registrations,
              false);

  private static RegisterImageRecipeCommandResult? ValidateResolvedAuthority(
      CanonicalImageRecipeRegistration canonical,
      GitHubRepositoryIdentity repository,
      GitHubWorkflowIdentity workflow,
      GitHubWorkflowFileRevision revision)
  {
    if (repository.Id != canonical.GitHubRepositoryId)
    {
      return Conflict(
          "github_repository_identity_mismatch",
          "The GitHub repository no longer matches the requested identity.");
    }
    if (workflow.Id != canonical.GitHubWorkflowId)
    {
      return Conflict(
          "github_workflow_identity_mismatch",
          "The GitHub workflow no longer matches the requested identity.");
    }
    if (!string.Equals(
            workflow.Path,
            canonical.WorkflowPath,
            StringComparison.Ordinal))
    {
      return Conflict(
          "github_workflow_path_mismatch",
          "The GitHub workflow no longer matches the requested workflow path.");
    }
    if (workflow.State != GitHubWorkflowState.Active)
    {
      return Conflict(
          "github_workflow_inactive",
          "The GitHub workflow must be active to register the image recipe.");
    }
    if (!string.Equals(
            revision.Path,
            canonical.WorkflowPath,
            StringComparison.Ordinal) ||
        !string.Equals(
            revision.Reference,
            canonical.DispatchRef,
            StringComparison.Ordinal))
    {
      return Conflict(
          "github_workflow_revision_mismatch",
          "The reviewed GitHub workflow file no longer matches the requested path or dispatch ref.");
    }

    return null;
  }

  private static RegisterImageRecipeCommandResult MapRepositoryFailure(
      GitHubClientOutcome<GitHubRepositoryIdentity> outcome) =>
      MapGitHubFailure(
          outcome,
          notFoundCode: "github_repository_not_found",
          notFoundError: "The GitHub installation could not find the requested repository.",
          forbiddenCode: "github_repository_forbidden",
          forbiddenError: "The GitHub installation could not access the requested repository.",
          invalidCode: "invalid_image_recipe_registration",
          invalidError: "GitHub repository identity is invalid.");

  private static RegisterImageRecipeCommandResult MapWorkflowFailure(
      GitHubClientOutcome<GitHubWorkflowIdentity> outcome) =>
      MapGitHubFailure(
          outcome,
          notFoundCode: "github_workflow_not_found",
          notFoundError: "The GitHub installation could not find the requested workflow.",
          forbiddenCode: "github_workflow_forbidden",
          forbiddenError: "The GitHub installation could not access the requested workflow.",
          invalidCode: "invalid_image_recipe_registration",
          invalidError: "GitHub workflow identity is invalid.");

  private static RegisterImageRecipeCommandResult MapRevisionFailure(
      GitHubClientOutcome<GitHubWorkflowFileRevision> outcome) =>
      MapGitHubFailure(
          outcome,
          notFoundCode: "github_workflow_revision_not_found",
          notFoundError: "The reviewed workflow file was not available at the dispatch ref.",
          forbiddenCode: "github_workflow_revision_forbidden",
          forbiddenError: "The GitHub installation could not access the reviewed workflow file at the dispatch ref.",
          invalidCode: "invalid_image_recipe_registration",
          invalidError: "The dispatch ref or workflow path is invalid.");

  private static RegisterImageRecipeCommandResult MapWorkflowContentFailure(
      GitHubClientOutcome<GitHubWorkflowFileContent> outcome) =>
      outcome.Kind switch
      {
        GitHubClientOutcomeKind.InvalidRequest => Invalid(
            "invalid_image_recipe_registration",
            "The reviewed GitHub workflow content request is invalid."),
        GitHubClientOutcomeKind.NotFound => NotFound(
            "github_workflow_content_not_found",
            "The reviewed GitHub workflow content was not available at the dispatch ref."),
        GitHubClientOutcomeKind.UnauthorizedOrForbidden => Forbidden(
            "github_workflow_content_forbidden",
            "The GitHub installation could not access the reviewed GitHub workflow content."),
        GitHubClientOutcomeKind.InvalidResponse => Conflict(
            "github_workflow_content_invalid",
            "The reviewed GitHub workflow content did not match the requested revision."),
        GitHubClientOutcomeKind.NotConfigured => new RegisterImageRecipeCommandResult(
            ImageRecipeRegistrationCommandStatus.NotConfigured,
            "github_image_integration_not_configured",
            "Trusted GitHub image registration is not configured for this deployment.",
            null,
            null),
        GitHubClientOutcomeKind.RateLimited => new RegisterImageRecipeCommandResult(
            ImageRecipeRegistrationCommandStatus.RateLimited,
            "github_image_integration_rate_limited",
            "GitHub image workflow validation is temporarily rate-limited.",
            null,
            outcome.RetryAt),
        _ => new RegisterImageRecipeCommandResult(
            ImageRecipeRegistrationCommandStatus.Unavailable,
            "github_image_integration_unavailable",
            "GitHub image workflow validation is temporarily unavailable.",
            null,
            null),
      };

  private static RegisterImageRecipeCommandResult MapGitHubFailure<T>(
      GitHubClientOutcome<T> outcome,
      string notFoundCode,
      string notFoundError,
      string forbiddenCode,
      string forbiddenError,
      string invalidCode,
      string invalidError) =>
      outcome.Kind switch
      {
        GitHubClientOutcomeKind.InvalidRequest => Invalid(
            invalidCode,
            invalidError),
        GitHubClientOutcomeKind.NotFound => NotFound(
            notFoundCode,
            notFoundError),
        GitHubClientOutcomeKind.UnauthorizedOrForbidden => Forbidden(
            forbiddenCode,
            forbiddenError),
        GitHubClientOutcomeKind.NotConfigured => new RegisterImageRecipeCommandResult(
            ImageRecipeRegistrationCommandStatus.NotConfigured,
            "github_image_integration_not_configured",
            "Trusted GitHub image registration is not configured for this deployment.",
            null,
            null),
        GitHubClientOutcomeKind.RateLimited => new RegisterImageRecipeCommandResult(
            ImageRecipeRegistrationCommandStatus.RateLimited,
            "github_image_integration_rate_limited",
            "GitHub image workflow validation is temporarily rate-limited.",
            null,
            outcome.RetryAt),
        _ => new RegisterImageRecipeCommandResult(
            ImageRecipeRegistrationCommandStatus.Unavailable,
            "github_image_integration_unavailable",
            "GitHub image workflow validation is temporarily unavailable.",
            null,
            null),
      };

  private static RegisterImageRecipeCommandResult Succeeded(
      ImageRecipeRegistration registration) =>
      new(
          ImageRecipeRegistrationCommandStatus.Succeeded,
          null,
          null,
          registration,
          null);

  private static RegisterImageRecipeCommandResult Unchanged(
      ImageRecipeRegistration registration) =>
      new(
          ImageRecipeRegistrationCommandStatus.Unchanged,
          null,
          null,
          registration,
          null);

  private static RegisterImageRecipeCommandResult Conflict(
      string code,
      string error) =>
      new(
          ImageRecipeRegistrationCommandStatus.Conflict,
          code,
          error,
          null,
          null);

  private static RegisterImageRecipeCommandResult Invalid(
      string code,
      string error) =>
      new(
          ImageRecipeRegistrationCommandStatus.Invalid,
          code,
          error,
          null,
          null);

  private static RegisterImageRecipeCommandResult NotFound(
      string code,
      string error) =>
      new(
          ImageRecipeRegistrationCommandStatus.NotFound,
          code,
          error,
          null,
          null);

  private static RegisterImageRecipeCommandResult Forbidden(
      string code,
      string error) =>
      new(
          ImageRecipeRegistrationCommandStatus.Forbidden,
          code,
          error,
          null,
          null);
}
