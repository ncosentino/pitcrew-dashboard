using Carter;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using PitCrew.Dashboard.Features.Access;
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

  private static bool ReadLimit(
      HttpContext context,
      out int limit,
      out IResult? error)
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
          "invalid_image_recipe_query",
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
}
