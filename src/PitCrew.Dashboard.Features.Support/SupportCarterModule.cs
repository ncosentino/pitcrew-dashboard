using System.Security.Cryptography;

using Carter;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

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
    admin.MapPost("/enrollments", CreateEnrollmentAsync)
        .AddEndpointFilter<DashboardAntiforgeryEndpointFilter>();
    admin.MapPost("/identities/{nodeId:guid}/revoke", RevokeIdentityAsync)
        .AddEndpointFilter<DashboardAntiforgeryEndpointFilter>();

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

  private static async Task<IResult> CreateEnrollmentAsync(
      HttpContext context,
      string tenantId,
      CreateSupportEnrollmentRequest request,
      ICreateSupportEnrollmentUnitOfWork unitOfWork,
      CancellationToken cancellationToken)
  {
    context.Response.Headers.CacheControl = "no-store";
    var created = await unitOfWork.CreateAsync(
        context.User,
        tenantId,
        new CreateSupportEnrollmentInput(
            request.DisplayName,
            request.NodeSigningPublicKeySpki,
            request.NodeEncryptionPublicKeySpki),
        cancellationToken);
    return created is null
        ? Results.BadRequest(Error("invalid_support_enrollment", "Support enrollment public keys or tenant access are invalid."))
        : Results.Created(
            $"/api/tenants/{tenantId}/support/v1/identities/{created.Identity.NodeId:D}",
            new CreatedSupportEnrollmentResponse(
                created.Identity.NodeId.ToString("D"),
                created.Identity.DisplayName,
                created.EnrollmentCode,
                created.TransportCredential,
                created.EnrollmentExpiresAt,
                created.RelayUrl,
                created.AuthorizationSigningPublicKeySpki,
                created.ResultEncryptionPublicKeySpki));
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

