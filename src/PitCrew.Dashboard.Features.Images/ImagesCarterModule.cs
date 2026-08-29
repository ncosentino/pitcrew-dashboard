using Carter;

using System.Globalization;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using PitCrew.Dashboard.Features.Access;
using PitCrew.Dashboard.Features.Images.Abstractions;
using PitCrew.Dashboard.Kernel.Authentication;

namespace PitCrew.Dashboard.Features.Images;

/// <summary>
/// Maps tenant-scoped trusted image recipe registration endpoints.
/// </summary>
public sealed class ImagesCarterModule : ICarterModule
{
  private const int DefaultListLimit = 20;
  private const int MaximumListLimit = 100;

  /// <summary>
  /// Adds trusted image recipe registration routes.
  /// </summary>
  /// <param name="app">Endpoint route builder.</param>
  public void AddRoutes(IEndpointRouteBuilder app)
  {
    var readers = app.MapGroup("/api/tenants/{tenantId}/images/v1/recipes")
        .RequireAuthorization(AccessPolicies.TenantViewer);
    readers.MapGet("/registrations", GetRegistrationsAsync);
    readers.MapGet(
        "/registrations/{registrationId:guid}",
        GetRegistrationAsync);

    var administrators = app.MapGroup(
            "/api/tenants/{tenantId}/images/v1/recipes")
        .RequireAuthorization(AccessPolicies.TenantAdministrator);
    administrators.MapPost("/registrations", CreateRegistrationAsync)
        .AddEndpointFilter<DashboardAntiforgeryEndpointFilter>()
        .AddImageRecipeMutationRateLimit();
    administrators.MapPost(
            "/registrations/{registrationId:guid}/disable",
            DisableRegistrationAsync)
        .AddEndpointFilter<DashboardAntiforgeryEndpointFilter>()
        .AddImageRecipeMutationRateLimit();

    var requestReaders = app.MapGroup(
            "/api/tenants/{tenantId}/images/requests")
        .RequireAuthorization(AccessPolicies.TenantViewer);
    requestReaders.MapGet("", GetBuildRequestsAsync);
    requestReaders.MapGet("/{requestId:guid}", GetBuildRequestAsync);

    var candidateReaders = app.MapGroup(
            "/api/tenants/{tenantId}/images/candidates")
        .RequireAuthorization(AccessPolicies.TenantViewer);
    candidateReaders.MapGet("", GetCandidatesAsync);
    candidateReaders.MapGet("/{candidateId:guid}", GetCandidateAsync);

    var requestAdministrators = app.MapGroup(
            "/api/tenants/{tenantId}/images/requests")
        .RequireAuthorization(AccessPolicies.TenantAdministrator);
    requestAdministrators.MapPost("", CreateBuildRequestAsync)
        .AddEndpointFilter<DashboardAntiforgeryEndpointFilter>()
        .AddImageRecipeMutationRateLimit();

    var rolloutReaders = app.MapGroup(
            "/api/tenants/{tenantId}/images/profile-rollouts")
        .RequireAuthorization(AccessPolicies.TenantViewer);
    rolloutReaders.MapGet(
        "/{nodeId:guid}/{profileId}",
        GetProfileRolloutAsync);

    var rolloutAdministrators = app.MapGroup(
            "/api/tenants/{tenantId}/images/profile-rollouts")
        .RequireAuthorization(AccessPolicies.TenantAdministrator);
    rolloutAdministrators.MapPost("", CreateProfileRolloutAsync)
        .AddEndpointFilter<DashboardAntiforgeryEndpointFilter>()
        .AddImageRecipeMutationRateLimit();
  }

  private static async Task<IResult> GetBuildRequestsAsync(
      HttpContext context,
      string tenantId,
      IImageBuildRequestUnitOfWork unitOfWork,
      CancellationToken cancellationToken)
  {
    if (!ReadLimit(context, out var limit, out var error))
    {
      return error!;
    }

    var requests = await unitOfWork.ListAsync(
        tenantId,
        limit + 1,
        cancellationToken);
    return Results.Ok(new ImageBuildRequestListResponse(
        requests.Take(limit)
            .Select(ImageBuildRequestValidation.ToResponse)
            .ToArray(),
        requests.Count > limit));
  }

  private static async Task<IResult> GetBuildRequestAsync(
      string tenantId,
      Guid requestId,
      IImageBuildRequestUnitOfWork unitOfWork,
      CancellationToken cancellationToken)
  {
    var request = await unitOfWork.GetOrNullAsync(
        tenantId,
        requestId,
        cancellationToken);
    return request is null
        ? Results.NotFound(Error(
            "image_build_request_not_found",
            "The image build request was not found."))
        : Results.Ok(ImageBuildRequestValidation.ToResponse(request));
  }

  private static async Task<IResult> GetCandidatesAsync(
      HttpContext context,
      string tenantId,
      IImageBuildRequestUnitOfWork unitOfWork,
      CancellationToken cancellationToken)
  {
    if (!ReadLimit(
            context,
            out var limit,
            out var error,
            "invalid_image_candidate_query"))
    {
      return error!;
    }

    var candidates = await unitOfWork.ListCandidatesAsync(
        tenantId,
        limit + 1,
        cancellationToken);
    return Results.Ok(new ImageCandidateListResponse(
        candidates.Take(limit)
            .Select(ToCandidateResponse)
            .ToArray(),
        candidates.Count > limit));
  }

  private static async Task<IResult> GetCandidateAsync(
      string tenantId,
      Guid candidateId,
      IImageBuildRequestUnitOfWork unitOfWork,
      CancellationToken cancellationToken)
  {
    var candidate = await unitOfWork.GetCandidateOrNullAsync(
        tenantId,
        candidateId,
        cancellationToken);
    return candidate is null
        ? Results.NotFound(Error(
            "image_candidate_not_found",
            "The image candidate was not found."))
        : Results.Ok(ToCandidateResponse(candidate));
  }

  private static async Task<IResult> CreateBuildRequestAsync(
      HttpContext context,
      string tenantId,
      RequestImageBuildRequest request,
      IImageBuildRequestUnitOfWork unitOfWork,
      TimeProvider timeProvider,
      CancellationToken cancellationToken)
  {
    var result = await unitOfWork.CreateAsync(
        context.User,
        tenantId,
        new RequestImageBuildInput(
            request.RequestId,
            request.RegistrationId,
            request.RegistrationVersion,
            request.SourceRef ?? string.Empty,
            request.SourceCommit ?? string.Empty,
            request.Inputs ??
                new Dictionary<string, System.Text.Json.JsonElement>(
                    StringComparer.Ordinal)),
        cancellationToken);
    if (result.Status == ImageBuildRequestCommandStatus.RateLimited)
    {
      WriteRetryAfterHeader(
          context,
          timeProvider,
          result.RetryAt);
    }

    return result.Status switch
    {
      ImageBuildRequestCommandStatus.Succeeded
          when result.Request is not null => Results.Accepted(
              $"/api/tenants/{tenantId}/images/requests/{result.Request.RequestId:D}",
              ImageBuildRequestValidation.ToResponse(result.Request)),
      ImageBuildRequestCommandStatus.Unchanged
          when result.Request is not null => Results.Ok(
              ImageBuildRequestValidation.ToResponse(result.Request)),
      ImageBuildRequestCommandStatus.Invalid => Results.BadRequest(
          Error(
              result.Code ?? "invalid_image_build_request",
              result.Error ?? "The image build request is invalid.")),
      ImageBuildRequestCommandStatus.Conflict => Results.Conflict(
          Error(
              result.Code ?? "image_build_request_conflict",
              result.Error ??
              "The image build request conflicts with durable state.")),
      ImageBuildRequestCommandStatus.NotFound => Results.NotFound(
          Error(
              result.Code ?? "image_build_request_not_found",
              result.Error ?? "The image build authority was not found.")),
      ImageBuildRequestCommandStatus.Forbidden => Results.Json(
          Error(
              result.Code ?? "forbidden_image_build_request",
              result.Error ?? "The image build request is not authorized."),
          statusCode: StatusCodes.Status403Forbidden),
      ImageBuildRequestCommandStatus.RateLimited => Results.Json(
          Error(
              result.Code ?? "github_image_integration_rate_limited",
              result.Error ?? "GitHub source validation is rate-limited."),
          statusCode: StatusCodes.Status429TooManyRequests),
      ImageBuildRequestCommandStatus.NotConfigured
          or ImageBuildRequestCommandStatus.Unavailable => Results.Json(
              Error(
                  result.Code ?? "github_image_integration_unavailable",
                  result.Error ??
                  "GitHub source validation is temporarily unavailable."),
              statusCode: StatusCodes.Status503ServiceUnavailable),
      _ => Results.Problem(
          statusCode: StatusCodes.Status500InternalServerError,
          title: "Unsupported image build request result."),
    };
  }

  private static async Task<IResult> GetRegistrationsAsync(
      HttpContext context,
      string tenantId,
      IImageRecipeRegistrationUnitOfWork unitOfWork,
      CancellationToken cancellationToken)
  {
    if (!ReadListOptions(
            context,
            out var limit,
            out var includeDisabled,
            out var error))
    {
      return error!;
    }

    var page = await unitOfWork.ListAsync(
        tenantId,
        includeDisabled,
        limit,
        cancellationToken);
    return Results.Ok(new ImageRecipeRegistrationListResponse(
        page.Registrations.Select(
            ImageRecipeRegistrationValidation.ToResponse).ToArray(),
        page.Truncated));
  }

  private static async Task<IResult> GetRegistrationAsync(
      string tenantId,
      Guid registrationId,
      IImageRecipeRegistrationUnitOfWork unitOfWork,
      CancellationToken cancellationToken)
  {
    var registration = await unitOfWork.GetOrNullAsync(
        tenantId,
        registrationId,
        cancellationToken);
    return registration is null
        ? Results.NotFound(Error(
            "image_recipe_registration_not_found",
            "The image recipe registration was not found."))
        : Results.Ok(
            ImageRecipeRegistrationValidation.ToResponse(
                registration));
  }

  private static async Task<IResult> CreateRegistrationAsync(
      HttpContext context,
      string tenantId,
      RegisterImageRecipeRequest request,
      IImageRecipeRegistrationUnitOfWork unitOfWork,
      TimeProvider timeProvider,
      CancellationToken cancellationToken)
  {
    var result = await unitOfWork.CreateAsync(
        context.User,
        tenantId,
        new RegisterImageRecipeInput(
            request.RegistrationId,
            request.GitHubInstallationId,
            request.GitHubRepositoryId,
            request.GitHubWorkflowId,
            request.WorkflowPath,
            request.DispatchRef,
            request.RecipeId,
            request.CandidateSchemaVersion,
            request.AllowedSourceRefs ?? [],
            request.Inputs ?? []),
        cancellationToken);
    if (result.Status == ImageRecipeRegistrationCommandStatus.RateLimited)
    {
      WriteRetryAfterHeader(
          context,
          timeProvider,
          result.RetryAt);
    }

    return result.Status switch
    {
      ImageRecipeRegistrationCommandStatus.Succeeded
          when result.Registration is not null => Results.Created(
              $"/api/tenants/{tenantId}/images/v1/recipes/registrations/{result.Registration.RegistrationId:D}",
              ImageRecipeRegistrationValidation.ToResponse(
                  result.Registration)),
      ImageRecipeRegistrationCommandStatus.Unchanged
          when result.Registration is not null => Results.Ok(
              ImageRecipeRegistrationValidation.ToResponse(
                  result.Registration)),
      ImageRecipeRegistrationCommandStatus.Invalid => Results.BadRequest(
          Error(
              result.Code ??
              "invalid_image_recipe_registration",
              result.Error ??
              "The image recipe registration request is invalid.")),
      ImageRecipeRegistrationCommandStatus.Conflict => Results.Conflict(
          Error(
              result.Code ??
              "image_recipe_registration_conflict",
              result.Error ??
              "The image recipe registration conflicts with existing durable state.")),
      ImageRecipeRegistrationCommandStatus.NotFound => Results.Json(
          Error(
              result.Code ??
              "github_image_registration_not_found",
              result.Error ??
              "The requested GitHub workflow authority was not found."),
          statusCode: StatusCodes.Status404NotFound),
      ImageRecipeRegistrationCommandStatus.Forbidden => Results.Json(
          Error(
              result.Code ??
              "forbidden_image_recipe_registration",
              result.Error ??
              "The image recipe registration request is not authorized."),
          statusCode: StatusCodes.Status403Forbidden),
      ImageRecipeRegistrationCommandStatus.NotConfigured => Results.Json(
          Error(
              result.Code ??
              "github_image_integration_not_configured",
              result.Error ??
              "Trusted GitHub image registration is not configured for this deployment."),
          statusCode: StatusCodes.Status503ServiceUnavailable),
      ImageRecipeRegistrationCommandStatus.RateLimited => Results.Json(
          Error(
              result.Code ??
              "github_image_integration_rate_limited",
              result.Error ??
              "GitHub image workflow validation is temporarily rate-limited."),
          statusCode: StatusCodes.Status429TooManyRequests),
      ImageRecipeRegistrationCommandStatus.Unavailable => Results.Json(
          Error(
              result.Code ??
              "github_image_integration_unavailable",
              result.Error ??
              "GitHub image workflow validation is temporarily unavailable."),
          statusCode: StatusCodes.Status503ServiceUnavailable),
      _ => Results.Problem(
          statusCode: StatusCodes.Status500InternalServerError,
          title: "Unsupported image recipe registration result."),
    };
  }

  private static async Task<IResult> DisableRegistrationAsync(
      HttpContext context,
      string tenantId,
      Guid registrationId,
      IImageRecipeRegistrationUnitOfWork unitOfWork,
      CancellationToken cancellationToken)
  {
    var status = await unitOfWork.DisableAsync(
        context.User,
        tenantId,
        registrationId,
        cancellationToken);
    return status switch
    {
      DisableImageRecipeRegistrationStatus.Succeeded =>
          Results.NoContent(),
      DisableImageRecipeRegistrationStatus.NotFound => Results.NotFound(
          Error(
              "image_recipe_registration_not_found",
              "The image recipe registration was not found.")),
      DisableImageRecipeRegistrationStatus.Forbidden => Results.Json(
          Error(
              "forbidden_image_recipe_registration",
              "The image recipe registration request is not authorized."),
          statusCode: StatusCodes.Status403Forbidden),
      _ => Results.Conflict(Error(
          "image_recipe_registration_disable_conflict",
          "The image recipe registration could not be disabled.")),
    };
  }

  private static bool ReadListOptions(
      HttpContext context,
      out int limit,
      out bool includeDisabled,
      out IResult? error)
  {
    error = null;
    includeDisabled = false;
    if (!ReadLimit(
            context,
            out limit,
            out error))
    {
      return false;
    }

    var rawIncludeDisabled = context.Request.Query["includeDisabled"].ToString();
    if (string.IsNullOrWhiteSpace(rawIncludeDisabled))
    {
      return true;
    }

    if (!bool.TryParse(
            rawIncludeDisabled,
            out includeDisabled))
    {
      error = Results.BadRequest(Error(
          "invalid_image_recipe_query",
          "The includeDisabled query parameter must be true or false."));
      return false;
    }

    return true;
  }

  private static ImageCandidateResponse ToCandidateResponse(
      ImageCandidateDetails details)
  {
    var candidate = details.Candidate;
    var ready = candidate as ReadyImageCandidate;
    var failed = candidate as FailedImageCandidate;
    return new ImageCandidateResponse(
        candidate.CandidateId,
        candidate.RequestId,
        details.RegistrationId,
        details.RegistrationVersion,
        ready is not null ? "ready" : "failed",
        candidate.RecipeId,
        candidate.SourceRepository,
        candidate.SourceCommit,
        Convert.ToString(
            candidate.GitHubRunId,
            CultureInfo.InvariantCulture),
        details.GitHubRunApiUrl,
        details.GitHubRunUrl,
        Convert.ToString(
            candidate.ArtifactId,
            CultureInfo.InvariantCulture),
        candidate.ArtifactName,
        candidate.ArtifactDigest,
        candidate.ReportHash,
        candidate.ImageReference,
        ready?.Digest ?? failed?.Digest,
        ready?.ImmutableReference ?? failed?.ImmutableReference,
        Format(candidate.Platform),
        Format(candidate.OutputMode),
        failed?.FailureCategory,
        failed?.FailureDetail,
        candidate.CreatedAt,
        candidate.StoredAt,
        details.Qualifications.Select(qualification =>
            new ImageCandidateQualificationResponse(
                Format(qualification.Name),
                Format(qualification.Status)))
            .ToArray());
  }

  private static string Format(
      ImageCandidatePlatform value) =>
      value switch
      {
        ImageCandidatePlatform.LinuxAmd64 => "linux/amd64",
        ImageCandidatePlatform.LinuxArm64 => "linux/arm64",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
      };

  private static string Format(
      ImageCandidateOutputMode value) =>
      value switch
      {
        ImageCandidateOutputMode.Registry => "registry",
        ImageCandidateOutputMode.Oci => "oci",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
      };

  private static string Format(
      ImageCandidateQualificationName value) =>
      value switch
      {
        ImageCandidateQualificationName.ImageBuild => "image-build",
        ImageCandidateQualificationName.BuildKitDigest =>
            "buildkit-digest",
        ImageCandidateQualificationName.RegistryDigest =>
            "registry-digest",
        ImageCandidateQualificationName.OciManifest => "oci-manifest",
        ImageCandidateQualificationName.BuilderCleanup =>
            "builder-cleanup",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
      };

  private static string Format(
      ImageCandidateQualificationStatus value) =>
      value switch
      {
        ImageCandidateQualificationStatus.Passed => "passed",
        ImageCandidateQualificationStatus.Failed => "failed",
        ImageCandidateQualificationStatus.Unavailable => "unavailable",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
      };

  private static bool ReadLimit(
      HttpContext context,
      out int limit,
      out IResult? error,
      string errorCode = "invalid_image_recipe_query")
  {
    error = null;
    var rawLimit = context.Request.Query["limit"].ToString();
    if (string.IsNullOrWhiteSpace(rawLimit))
    {
      limit = DefaultListLimit;
      return true;
    }

    if (!int.TryParse(
            rawLimit,
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out limit) ||
        limit is < 1 or > MaximumListLimit)
    {
      error = Results.BadRequest(Error(
          errorCode,
          $"The limit query parameter must be between 1 and {MaximumListLimit}."));
      return false;
    }

    return true;
  }

  private static void WriteRetryAfterHeader(
      HttpContext context,
      TimeProvider timeProvider,
      DateTimeOffset? retryAt)
  {
    if (retryAt is null)
    {
      return;
    }

    var delay = retryAt.Value - timeProvider.GetUtcNow();
    var seconds = Math.Max(
        0,
        (int)Math.Ceiling(delay.TotalSeconds));
    context.Response.Headers["Retry-After"] =
        seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
  }

  private static object Error(
      string code,
      string message) => new
      {
        error = new
        {
          code,
          message,
        },
      };

  private static async Task<IResult> GetProfileRolloutAsync(
      string tenantId,
      Guid nodeId,
      string profileId,
      IProfileImageRolloutReader reader,
      CancellationToken cancellationToken)
  {
    if (!PitCrew.Protocol.PitCrewProfileId.IsValid(profileId))
    {
      return Results.BadRequest(Error(
          "invalid_image_rollout_query",
          "The profile identifier is invalid."));
    }
    var response = await reader.GetProfileControlAsync(
        tenantId,
        nodeId,
        profileId,
        cancellationToken);
    if (response is null)
    {
      return Results.NotFound(Error(
          "image_rollout_profile_not_found",
          "The profile has not advertised image rollout capability."));
    }
    return Results.Ok(response);
  }

  private const string IdempotencyKeyHeader = "Idempotency-Key";

  private static async Task<IResult> CreateProfileRolloutAsync(
      HttpContext context,
      string tenantId,
      RollOutProfileImageRequestBody request,
      IRollOutProfileImageOrchestrator orchestrator,
      CancellationToken cancellationToken)
  {
    if (request is null)
    {
      return Results.BadRequest(Error(
          "invalid_image_rollout_request",
          "A profile-image rollout body is required."));
    }
    if (!context.Request.Headers.TryGetValue(
            IdempotencyKeyHeader,
            out var idempotencyKeyValues) ||
        idempotencyKeyValues.Count != 1)
    {
      return Results.BadRequest(Error(
          "invalid_image_rollout_idempotency_key",
          "Exactly one Idempotency-Key header is required."));
    }
    var idempotencyKey = idempotencyKeyValues[0];
    if (!RollOutProfileImageOrchestrator.IsValidIdempotencyKey(
            idempotencyKey))
    {
      return Results.BadRequest(Error(
          "invalid_image_rollout_idempotency_key",
          "Idempotency-Key must be 8 to 200 characters of "
          + "ASCII letters, digits, '-', '_', '.', or ':'."));
    }
    var statusLocation =
        $"/api/tenants/{tenantId}/images/profile-rollouts/"
        + $"{request.NodeId:D}/{request.ProfileId}";
    var outcome = await orchestrator.QueueAsync(
        context.User,
        tenantId,
        new RollOutProfileImageInput(
            request.NodeId,
            request.ProfileId ?? string.Empty,
            request.CandidateId,
            request.ExpectedCurrentImageReference,
            request.ExpectedCurrentImageDigest,
            request.ExpectedCurrentLocalImageId,
            request.ExpectedCurrentWorkerRevision,
            request.ExpectedStaticFingerprint ?? string.Empty,
            request.ExpectedPreservedConfigurationFingerprint ?? string.Empty,
            request.ExpectedRoutingFingerprint ?? string.Empty,
            request.ExpectedDesiredGeneration,
            request.ExpectedDesiredStateHash,
            idempotencyKey!),
        cancellationToken);
    return outcome.Status switch
    {
      RollOutProfileImageStatus.Queued when outcome.CommandId is not null =>
          Results.Accepted(
              statusLocation,
              new RollOutProfileImageResponse(
                  outcome.CommandId.Value,
                  "queued",
                  statusLocation)),
      RollOutProfileImageStatus.IdempotentReplay
          when outcome.CommandId is not null =>
          Results.Accepted(
              statusLocation,
              new RollOutProfileImageResponse(
                  outcome.CommandId.Value,
                  "queued",
                  statusLocation)),
      RollOutProfileImageStatus.IdempotencyKeyReuseConflict => Results.Conflict(
          Error(
              outcome.Code ?? "image_rollout_idempotency_key_conflict",
              outcome.Error ??
              "The Idempotency-Key was already used with different rollout authority.")),
      RollOutProfileImageStatus.Unauthorized => Results.Json(
          Error(
              outcome.Code ?? "unauthorized_image_rollout",
              outcome.Error ??
              "The request principal is not an authenticated dashboard user."),
          statusCode: StatusCodes.Status403Forbidden),
      RollOutProfileImageStatus.Invalid => Results.BadRequest(
          Error(
              outcome.Code ?? "invalid_image_rollout_request",
              outcome.Error ?? "The rollout request is invalid.")),
      RollOutProfileImageStatus.CandidateNotFound => Results.NotFound(
          Error(
              outcome.Code ?? "image_rollout_target_not_found",
              outcome.Error ??
              "The requested candidate or node was not found.")),
      RollOutProfileImageStatus.CandidateFailed => Results.Conflict(
          Error(
              outcome.Code ?? "image_candidate_not_ready",
              outcome.Error ??
              "The requested candidate is not eligible for rollout.")),
      RollOutProfileImageStatus.CandidateNotRegistryReady => Results.Conflict(
          Error(
              outcome.Code ?? "image_candidate_not_registry_ready",
              outcome.Error ??
              "The requested candidate has no immutable registry reference.")),
      RollOutProfileImageStatus.Unsupported => Results.Json(
          Error(
              outcome.Code ?? "image_rollout_unsupported",
              outcome.Error ??
              "The connector does not advertise image rollout for this profile."),
          statusCode: StatusCodes.Status409Conflict),
      RollOutProfileImageStatus.UnsupportedTopology => Results.Json(
          Error(
              outcome.Code ?? "image_rollout_unsupported_topology",
              outcome.Error ??
              "The connector cannot preserve the profile's routing state."),
          statusCode: StatusCodes.Status409Conflict),
      RollOutProfileImageStatus.NotAllowed => Results.Json(
          Error(
              outcome.Code ?? "image_rollout_not_allowed",
              outcome.Error ??
              "Local connector policy does not allow this rollout."),
          statusCode: StatusCodes.Status403Forbidden),
      RollOutProfileImageStatus.RecipeNotAllowed => Results.Json(
          Error(
              outcome.Code ?? "image_rollout_recipe_not_allowed",
              outcome.Error ??
              "The candidate recipe is not on the profile's allowlist."),
          statusCode: StatusCodes.Status403Forbidden),
      RollOutProfileImageStatus.RegistryNotAllowed => Results.Json(
          Error(
              outcome.Code ?? "image_rollout_registry_not_allowed",
              outcome.Error ??
              "The connector's local registry policy does not allow this recipe."),
          statusCode: StatusCodes.Status403Forbidden),
      RollOutProfileImageStatus.ArchitectureMismatch => Results.Conflict(
          Error(
              outcome.Code ?? "image_rollout_architecture_mismatch",
              outcome.Error ??
              "The candidate architecture does not match the profile.")),
      RollOutProfileImageStatus.StaleFence => Results.Conflict(
          Error(
              outcome.Code ?? "image_rollout_stale_fence",
              outcome.Error ??
              "The requested fences no longer match the connector's advertised state.")),
      RollOutProfileImageStatus.Conflict => Results.Conflict(
          Error(
              outcome.Code ?? "image_rollout_operation_active",
              outcome.Error ??
              "Another profile operation is already active.")),
      RollOutProfileImageStatus.RateLimited => Results.Json(
          Error(
              outcome.Code ?? "image_rollout_cooldown",
              outcome.Error ??
              "A rollout for this profile was requested too recently."),
          statusCode: StatusCodes.Status429TooManyRequests),
      _ => Results.Problem(
          statusCode: StatusCodes.Status500InternalServerError,
          title: "Unsupported image rollout result."),
    };
  }
}
