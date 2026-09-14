using System.Security.Cryptography;
using System.Text.Json;

using Carter;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

using PitCrew.Dashboard.Features.Access;
using PitCrew.Dashboard.Features.Support.Abstractions;
using PitCrew.Dashboard.Kernel.Authentication;
using PitCrew.Protocol;
using PitCrew.Support.Protocol;

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
      TimeProvider timeProvider,
      CancellationToken cancellationToken)
  {
    var generatedAt = timeProvider.GetUtcNow();
    return Results.Ok((await unitOfWork.GetRecentAsync(
          context.User,
          tenantId,
          cancellationToken)).Select(session =>
              MapSession(session, generatedAt)));
  }

  private static async Task<IResult> CreateSessionAsync(
      HttpContext context,
      string tenantId,
      CreateSupportDiagnosticSessionRequest request,
      ICreateSupportDiagnosticSessionUnitOfWork unitOfWork,
      TimeProvider timeProvider,
      CancellationToken cancellationToken)
  {
    context.Response.Headers.CacheControl = "no-store";
    var intentId = request.IntentId == Guid.Empty
        ? Guid.NewGuid()
        : request.IntentId;
    var result = await unitOfWork.CreateAsync(
        context.User,
        tenantId,
        new SupportDiagnosticSessionInput(
            intentId,
            request.NodeId,
            request.DiagnosticMode,
            request.ProfileId,
            request.ExpiresInSeconds),
        cancellationToken);
    return result.Status switch
    {
      SupportMutationStatus.Succeeded when result.Session is not null => Results.Accepted(
          $"/api/tenants/{tenantId}/support/v1/sessions/{result.Session.SessionId:D}",
          MapSession(
              result.Session,
              timeProvider.GetUtcNow())),
      SupportMutationStatus.Invalid => Results.BadRequest(Error("invalid_support_session", result.Error ?? "The support diagnostic session is invalid.")),
      SupportMutationStatus.Forbidden => Results.Forbid(),
      SupportMutationStatus.NotFound => Results.NotFound(),
      SupportMutationStatus.Revoked => Results.Conflict(Error("support_identity_revoked", "The support identity is revoked.")),
      SupportMutationStatus.Conflict => Results.Conflict(Error(
          "support_session_enqueue_conflict",
          result.Error ?? "The support session could not be queued in its current state.")),
      SupportMutationStatus.Unavailable => Results.Json(
          Error(
              "support_relay_unavailable",
              result.Error ?? "Relay acceptance is not yet confirmed. Retry with the same request intent."),
          statusCode: StatusCodes.Status503ServiceUnavailable),
      _ => Results.BadRequest(Error(
          "invalid_support_session_result",
          "The support diagnostic session outcome is invalid.")),
    };
  }

  private static async Task<IResult> GetSessionAsync(
      HttpContext context,
      string tenantId,
      Guid sessionId,
      IGetSupportDiagnosticSessionUnitOfWork unitOfWork,
      TimeProvider timeProvider,
      CancellationToken cancellationToken)
  {
    context.Response.Headers.CacheControl = "no-store";
    var result = await unitOfWork.GetAsync(context.User, tenantId, sessionId, cancellationToken);
    return result.Status switch
    {
      SupportMutationStatus.Succeeded when result.Session is not null => Results.Ok(
          MapSession(
              result.Session,
              timeProvider.GetUtcNow())),
      SupportMutationStatus.Forbidden => Results.Forbid(),
      SupportMutationStatus.NotFound => Results.NotFound(),
      SupportMutationStatus.Invalid => Results.BadRequest(Error(
          "invalid_support_session_query",
          "The support session query is invalid.")),
      SupportMutationStatus.Conflict => Results.Conflict(Error(
          "support_session_query_conflict",
          "The support session could not be read in its current state.")),
      SupportMutationStatus.Unavailable => Results.Json(
          Error(
              "support_session_query_unavailable",
              "The support session could not be refreshed. Its last known state remains available."),
          statusCode: StatusCodes.Status503ServiceUnavailable),
      _ => Results.Conflict(Error(
          "support_session_query_outcome",
          "The support session query did not produce a readable result.")),
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

  private static SupportDiagnosticSessionResponse MapSession(
      SupportDiagnosticSession session,
      DateTimeOffset generatedAt)
  {
    var resultCoverage = ResultCoverage(session.Report);
    return new SupportDiagnosticSessionResponse(
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
          session.DispatchedAt,
          session.RejectionDisposition,
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
              : null)
    {
      EvidenceClaims =
      [
        new EvidenceClaim(
            "diagnostic-authorization",
            "dashboard-authorization",
            "support-session-request",
            session.RequestedByGitHubUserId,
            session.RequestedAt,
            session.RequestedAt,
            session.RequestedAt,
            null,
            generatedAt,
            session.ExpiresAt,
            "complete",
            "live",
            "current",
            "authorized",
            null),
        new EvidenceClaim(
            "diagnostic-transport",
            "support-relay",
            "support-session-lifecycle",
            null,
            session.DispatchedAt ?? session.CompletedAt,
            session.DispatchedAt ?? session.CompletedAt,
            session.CompletedAt,
            null,
            generatedAt,
            session.ExpiresAt,
            session.DispatchedAt is null ? "unavailable" : "complete",
            "live",
            session.Status.ToString().ToLowerInvariant(),
            session.Status.ToString().ToLowerInvariant(),
            session.RejectionDisposition),
        new EvidenceClaim(
            "diagnostic-result",
            "verified-node-result",
            "local-broker",
            session.NodeId.ToString("D"),
            ResultCompletedAt(session.Report),
            session.ResultReceivedAt,
            session.CompletedAt,
            session.ResultVerifiedAt,
            generatedAt,
            null,
            resultCoverage,
            "live",
            session.Status == SupportDiagnosticSessionStatus.Completed
                ? resultCoverage
                : "unavailable",
            session.Status == SupportDiagnosticSessionStatus.Completed
                ? session.ProfileId
                : null,
            session.Status == SupportDiagnosticSessionStatus.Completed
                ? null
                : session.RejectionDisposition ?? "result-unavailable"),
      ],
    };
  }

  private static string ResultCoverage(JsonElement? report)
  {
    if (report is null)
    {
      return "unavailable";
    }
    var hasMeasurements =
        report.Value.TryGetProperty(
            "verifiedMeasurements",
            out var measurements) &&
        HasEvidenceItems(measurements);
    var hasUnavailable =
        report.Value.TryGetProperty(
            "unavailableEvidence",
            out var unavailable) &&
        HasEvidenceItems(unavailable);
    return hasUnavailable
        ? hasMeasurements ? "partial" : "unavailable"
        : "complete";
  }

  private static bool HasEvidenceItems(JsonElement value)
  {
    if (value.ValueKind == JsonValueKind.Array)
    {
      return value.GetArrayLength() > 0;
    }
    if (value.ValueKind != JsonValueKind.Object)
    {
      return false;
    }
    using var properties = value.EnumerateObject();
    return properties.MoveNext();
  }

  private static DateTimeOffset? ResultCompletedAt(
      JsonElement? report) =>
      report is not null &&
      report.Value.TryGetProperty(
          "completedAt",
          out var completedAt) &&
      completedAt.ValueKind == JsonValueKind.String &&
      completedAt.TryGetDateTimeOffset(out var parsed)
          ? parsed
          : null;

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
            new SupportEnrollmentCompletionResponse(
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
            new SupportIdentityCompletionResponse(
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
