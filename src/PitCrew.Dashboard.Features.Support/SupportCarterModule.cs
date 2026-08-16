using System.Security.Cryptography;

using Carter;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

using PitCrew.Dashboard.Features.Access;
using PitCrew.Dashboard.Features.Support.Abstractions;
using PitCrew.Dashboard.Kernel.Authentication;

namespace PitCrew.Dashboard.Features.Support;

/// <summary>
/// Maps Dashboard-owned support-plane identity and diagnostic session APIs.
/// </summary>
public sealed class SupportCarterModule : ICarterModule
{
  /// <summary>
  /// Adds support-plane v1 routes.
  /// </summary>
  /// <param name="app">Endpoint route builder.</param>
  public void AddRoutes(IEndpointRouteBuilder app)
  {
    var admin = app.MapGroup("/api/tenants/{tenantId}/support/v1")
        .RequireAuthorization(AccessPolicies.TenantAdministrator);
    admin.MapGet("/identities", GetIdentitiesAsync);
    admin.MapPost(
        "/enrollment-authorizations",
        CreateEnrollmentAuthorizationAsync)
        .AddEndpointFilter<DashboardAntiforgeryEndpointFilter>();
    admin.MapPost("/enrollments", CreateLegacyEnrollmentAsync)
        .AddEndpointFilter<DashboardAntiforgeryEndpointFilter>();
    admin.MapPost("/identities/{nodeId:guid}/revoke", RevokeIdentityAsync)
        .AddEndpointFilter<DashboardAntiforgeryEndpointFilter>();

    app.MapPost(
        "/api/support-agent/v1/enrollments/complete",
        CompleteEnrollmentAsync)
        .AddEndpointFilter<SupportEnrollmentRateLimitEndpointFilter>()
        .AllowAnonymous();
    app.MapPost(
        "/api/support-agent/v1/identities/{nodeId:guid}/rotate",
        RotateIdentityAsync)
        .AddEndpointFilter<SupportRotationRateLimitEndpointFilter>()
        .AllowAnonymous();
    app.MapPost(
        "/api/support-agent/v1/identities/{nodeId:guid}/rotate/finalize",
        FinalizeIdentityRotationAsync)
        .AddEndpointFilter<SupportRotationRateLimitEndpointFilter>()
        .AllowAnonymous();

    var sessions = app.MapGroup("/api/tenants/{tenantId}/support/v1")
        .RequireAuthorization(AccessPolicies.SupportDiagnosticRequester);
    sessions.MapGet("/sessions", GetSessionsAsync);
    sessions.MapPost("/sessions", CreateSessionAsync)
        .AddEndpointFilter<SupportAntiforgeryEndpointFilter>();
    sessions.MapGet("/sessions/{sessionId:guid}", GetSessionAsync);
    sessions.MapPost("/sessions/{sessionId:guid}/cancel", CancelSessionAsync)
        .AddEndpointFilter<SupportAntiforgeryEndpointFilter>();
  }

  private static async Task<IResult> GetIdentitiesAsync(
      string tenantId,
      IGetSupportIdentitiesUnitOfWork unitOfWork,
      CancellationToken cancellationToken) =>
      Results.Ok((await unitOfWork.GetAsync(tenantId, cancellationToken)).Select(MapIdentity));

  private static async Task<IResult> CreateEnrollmentAuthorizationAsync(
      HttpContext context,
      string tenantId,
      CreateSupportEnrollmentAuthorizationRequest request,
      ICreateSupportEnrollmentUnitOfWork unitOfWork,
      CancellationToken cancellationToken)
  {
    context.Response.Headers.CacheControl = "no-store";
    var created = await unitOfWork.CreateAsync(
        context.User,
        tenantId,
        new CreateSupportEnrollmentInput(
            request.DisplayName),
        cancellationToken);
    return created is null
        ? Results.BadRequest(Error("invalid_support_enrollment", "Support enrollment request or tenant access is invalid."))
        : Results.Created(
            $"/api/tenants/{tenantId}/support/v1/enrollment-authorizations",
            new CreatedSupportEnrollmentAuthorizationResponse(
                created.DisplayName,
                created.EnrollmentCode,
                created.ExpiresAt));
  }

  private static async Task<IResult> CreateLegacyEnrollmentAsync(
      HttpContext context,
      string tenantId,
      CreateSupportEnrollmentRequest request,
      ICreateSupportEnrollmentUnitOfWork createUnitOfWork,
      ICompleteSupportEnrollmentUnitOfWork completeUnitOfWork,
      IOptions<SupportPlaneOptions> options,
      CancellationToken cancellationToken)
  {
    context.Response.Headers.CacheControl = "no-store";
    if (!options.Value.AllowLegacyManualEnrollment)
    {
      return Results.NotFound();
    }
    var created = await createUnitOfWork.CreateAsync(
        context.User,
        tenantId,
        new CreateSupportEnrollmentInput(request.DisplayName),
        cancellationToken);
    if (created is null)
    {
      return Results.BadRequest(Error(
          "invalid_support_enrollment",
          "Support enrollment request or tenant access is invalid."));
    }
    var completed = await completeUnitOfWork.CompleteAsync(
        new CompleteSupportEnrollmentInput(
            tenantId,
            created.EnrollmentCode,
            Guid.NewGuid(),
            request.NodeSigningPublicKeySpki,
            request.NodeEncryptionPublicKeySpki),
        cancellationToken);
    if (completed.Status == SupportMutationStatus.Succeeded &&
        completed.Enrollment is not null &&
        completed.Enrollment.TransportCredential is not null)
    {
      var enrollment = completed.Enrollment;
      return Results.Created(
          $"/api/tenants/{tenantId}/support/v1/identities/{enrollment.Identity.NodeId:D}",
          new CreatedSupportEnrollmentResponse(
              enrollment.Identity.NodeId.ToString("D"),
              enrollment.Identity.DisplayName,
              created.EnrollmentCode,
              enrollment.TransportCredential,
              created.ExpiresAt,
              enrollment.RelayUrl,
              enrollment.AuthorizationSigningPublicKeySpki,
              enrollment.ResultEncryptionPublicKeySpki));
    }
    return MapEnrollmentCompletion(completed.Status, completed.Enrollment);
  }

  private static async Task<IResult> CompleteEnrollmentAsync(
      HttpContext context,
      CompleteSupportEnrollmentRequest request,
      ICompleteSupportEnrollmentUnitOfWork unitOfWork,
      CancellationToken cancellationToken)
  {
    context.Response.Headers.CacheControl = "no-store";
    var completed = await unitOfWork.CompleteAsync(
        new CompleteSupportEnrollmentInput(
            request.TenantId,
            request.EnrollmentCode,
            request.CompletionId,
            request.NodeSigningPublicKeySpki,
            request.NodeEncryptionPublicKeySpki),
        cancellationToken);
    return MapEnrollmentCompletion(completed.Status, completed.Enrollment);
  }

  private static async Task<IResult> RotateIdentityAsync(
      HttpContext context,
      Guid nodeId,
      RotateSupportIdentityRequest request,
      IRotateSupportIdentityUnitOfWork unitOfWork,
      CancellationToken cancellationToken)
  {
    context.Response.Headers.CacheControl = "no-store";
    var rotated = await unitOfWork.RotateAsync(
        new RotateSupportIdentityInput(
            request.RotationId,
            request.TenantId,
            nodeId,
            request.CurrentTransportCredential,
            request.ReplacementTransportCredential,
            request.NodeSigningPublicKeySpki,
            request.NodeEncryptionPublicKeySpki),
        cancellationToken);
    return MapIdentityRotationCompletion(rotated.Status, rotated.Identity);
  }

  private static async Task<IResult> FinalizeIdentityRotationAsync(
      HttpContext context,
      Guid nodeId,
      FinalizeSupportIdentityRotationRequest request,
      IRotateSupportIdentityUnitOfWork unitOfWork,
      CancellationToken cancellationToken)
  {
    context.Response.Headers.CacheControl = "no-store";
    var rotated = await unitOfWork.FinalizeAsync(
        new FinalizeSupportIdentityRotationInput(
            request.RotationId,
            request.TenantId,
            nodeId,
            request.CurrentTransportCredential),
        cancellationToken);
    return MapIdentityRotationCompletion(rotated.Status, rotated.Identity);
  }

  private static async Task<IResult> RevokeIdentityAsync(
      HttpContext context,
      string tenantId,
      Guid nodeId,
      IRevokeSupportIdentityUnitOfWork unitOfWork,
      CancellationToken cancellationToken) =>
      MutationResult(await unitOfWork.RevokeAsync(context.User, tenantId, nodeId, cancellationToken));

  private static async Task<IResult> GetSessionsAsync(
      HttpContext context,
      string tenantId,
      IGetSupportDiagnosticSessionUnitOfWork unitOfWork,
      CancellationToken cancellationToken) =>
      Results.Ok((await unitOfWork.GetRecentAsync(
          context.User,
          tenantId,
          cancellationToken)).Select(MapSession));

  private static async Task<IResult> CreateSessionAsync(
      HttpContext context,
      string tenantId,
      CreateSupportDiagnosticSessionRequest request,
      ICreateSupportDiagnosticSessionUnitOfWork unitOfWork,
      CancellationToken cancellationToken)
  {
    context.Response.Headers.CacheControl = "no-store";
    var result = await unitOfWork.CreateAsync(
        context.User,
        tenantId,
        new SupportDiagnosticSessionInput(
            request.NodeId,
            request.DiagnosticMode,
            request.ProfileId,
            request.ExpiresInSeconds),
        cancellationToken);
    return result.Status switch
    {
      SupportMutationStatus.Succeeded when result.Session is not null => Results.Accepted(
          $"/api/tenants/{tenantId}/support/v1/sessions/{result.Session.SessionId:D}",
          MapSession(result.Session)),
      SupportMutationStatus.Invalid => Results.BadRequest(Error("invalid_support_session", result.Error ?? "The support diagnostic session is invalid.")),
      SupportMutationStatus.Forbidden => Results.Forbid(),
      SupportMutationStatus.NotFound => Results.NotFound(),
      SupportMutationStatus.Revoked => Results.Conflict(Error("support_identity_revoked", "The support identity is revoked.")),
      _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Unsupported support session creation result."),
    };
  }

  private static async Task<IResult> GetSessionAsync(
      HttpContext context,
      string tenantId,
      Guid sessionId,
      IGetSupportDiagnosticSessionUnitOfWork unitOfWork,
      CancellationToken cancellationToken)
  {
    context.Response.Headers.CacheControl = "no-store";
    var result = await unitOfWork.GetAsync(context.User, tenantId, sessionId, cancellationToken);
    return result.Status switch
    {
      SupportMutationStatus.Succeeded when result.Session is not null => Results.Ok(MapSession(result.Session)),
      SupportMutationStatus.Forbidden => Results.Forbid(),
      SupportMutationStatus.NotFound => Results.NotFound(),
      _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Unsupported support session query result."),
    };
  }

  private static async Task<IResult> CancelSessionAsync(
      HttpContext context,
      string tenantId,
      Guid sessionId,
      ICancelSupportDiagnosticSessionUnitOfWork unitOfWork,
      CancellationToken cancellationToken) =>
      MutationResult(await unitOfWork.CancelAsync(context.User, tenantId, sessionId, cancellationToken));

  private static SupportIdentityResponse MapIdentity(SupportIdentity identity) =>
      new(
          identity.NodeId.ToString("D"),
          identity.DisplayName,
          identity.Status.ToString(),
          identity.CreatedAt,
          identity.RevokedAt,
          identity.LastPollAt,
          identity.LastResultAt,
          identity.CapabilityVersion);

  private static SupportDiagnosticSessionResponse MapSession(SupportDiagnosticSession session) =>
      new(
          session.SessionId.ToString("D"),
          session.NodeId.ToString("D"),
          session.DiagnosticMode,
          session.ProfileId,
          session.Capability,
          session.RequestDigest,
          session.NodeSigningKeyFingerprint,
          session.Status.ToString(),
          session.RequestedAt,
          session.ExpiresAt,
          session.Report is not null &&
          session.Markdown is not null &&
          session.Attestation is not null
              ? new SupportDiagnosticResultResponse(
                  session.Report.Value,
                  session.Markdown,
                  new SupportDiagnosticAttestationResponse(
                      session.Attestation.NodeSigningPublicKeySpki,
                      session.Attestation.PayloadBase64Url,
                      session.Attestation.SignatureBase64Url,
                      session.Attestation.SignatureAlgorithm))
              : null);

  private static IResult MutationResult(SupportMutationStatus status) =>
      status switch
      {
        SupportMutationStatus.Succeeded => Results.NoContent(),
        SupportMutationStatus.NotFound => Results.NotFound(),
        SupportMutationStatus.Forbidden => Results.Forbid(),
        SupportMutationStatus.Revoked => Results.Conflict(Error("support_identity_revoked", "The support identity is revoked.")),
        SupportMutationStatus.Conflict => Results.Conflict(Error("support_mutation_conflict", "The support resource is not in a mutable state.")),
        _ => Results.BadRequest(Error("invalid_support_mutation", "The support mutation is invalid.")),
      };

  private static IResult MapEnrollmentCompletion(
      SupportMutationStatus status,
      CompletedSupportEnrollment? enrollment) =>
      status switch
      {
        SupportMutationStatus.Succeeded when enrollment is not null => Results.Ok(
            new CompletedSupportEnrollmentResponse(
                enrollment.Identity.NodeId.ToString("D"),
                enrollment.Identity.DisplayName,
                enrollment.TransportCredentialEnvelope,
                enrollment.RelayUrl,
                enrollment.AuthorizationSigningPublicKeySpki,
                enrollment.ResultEncryptionPublicKeySpki)),
        SupportMutationStatus.NotFound => Results.NotFound(),
        SupportMutationStatus.Revoked => Results.Conflict(Error(
            "support_identity_revoked",
            "The support identity is revoked.")),
        SupportMutationStatus.Forbidden => Results.Unauthorized(),
        SupportMutationStatus.Conflict => Results.Conflict(Error(
            "support_identity_conflict",
            "The support identity could not be changed in its current state.")),
        _ => Results.BadRequest(Error(
            "invalid_support_identity_request",
            "The support identity request is invalid or expired.")),
      };

  private static IResult MapIdentityRotationCompletion(
      SupportMutationStatus status,
      CreatedSupportEnrollment? enrollment) =>
      status switch
      {
        SupportMutationStatus.Succeeded when enrollment is not null => Results.Ok(
            new CompletedSupportIdentityResponse(
                enrollment.Identity.NodeId.ToString("D"),
                enrollment.Identity.DisplayName,
                enrollment.TransportCredential,
                enrollment.RelayUrl,
                enrollment.AuthorizationSigningPublicKeySpki,
                enrollment.ResultEncryptionPublicKeySpki)),
        SupportMutationStatus.NotFound => Results.NotFound(),
        SupportMutationStatus.Revoked => Results.Conflict(Error(
            "support_identity_revoked",
            "The support identity is revoked.")),
        SupportMutationStatus.Forbidden => Results.Unauthorized(),
        SupportMutationStatus.Conflict => Results.Conflict(Error(
            "support_identity_conflict",
            "The support identity could not be changed in its current state.")),
        _ => Results.BadRequest(Error(
            "invalid_support_identity_request",
            "The support identity request is invalid or expired.")),
      };

  private static object Error(string code, string message) =>
      new
      {
        error = new
        {
          code,
          message,
        },
      };
}
