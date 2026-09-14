using System.Security.Claims;
using System.Security.Cryptography;

using Microsoft.Extensions.Options;

using PitCrew.Dashboard.Features.Fleet.Abstractions;
using PitCrew.Dashboard.Features.Support.Abstractions;
using PitCrew.Protocol;
using PitCrew.Support.Protocol;

namespace PitCrew.Dashboard.Features.Support;

internal sealed class CreateSupportDiagnosticSessionUnitOfWork(
    SupportPrincipalAuthorizer _authorizer,
    ISupportStore _supportStore,
    IAlertIncidentStore _incidentStore,
    SupportSecretService _secretService,
    DashboardSupportKeyService _keyService,
    SupportRelayManagementClient _relayClient,
    IOptions<SupportPlaneOptions> _options,
    TimeProvider _timeProvider) : ICreateSupportDiagnosticSessionUnitOfWork
{
  public async Task<SupportSessionMutation> CreateAsync(
      ClaimsPrincipal principal,
      string tenantId,
      SupportDiagnosticSessionInput input,
      CancellationToken cancellationToken)
  {
    var validation = Validate(input);
    if (validation is not null)
    {
      return new SupportSessionMutation(SupportMutationStatus.Invalid, validation, null);
    }
    var decision = await _authorizer.CanRequestOrReadAsync(
        principal,
        tenantId,
        input.ProfileId,
        cancellationToken);
    if (!decision.Allowed || decision.ActorId is null)
    {
      return new SupportSessionMutation(SupportMutationStatus.Forbidden, null, null);
    }

    var now = _timeProvider.GetUtcNow();
    var seconds = Math.Min(input.ExpiresInSeconds, _options.Value.MaximumSessionLifetimeSeconds);
    var existing = await _supportStore.GetSessionByIntentOrNullAsync(
        tenantId,
        input.IntentId,
        cancellationToken);
    if (existing is not null)
    {
      return await ReconcileExistingAsync(
          existing,
          decision.ActorId,
          input,
          seconds,
          now,
          cancellationToken);
    }

    var identity = await _supportStore.GetIdentityOrNullAsync(
        tenantId,
        input.NodeId,
        cancellationToken);
    if (identity is null)
    {
      return new SupportSessionMutation(SupportMutationStatus.NotFound, null, null);
    }
    if (identity.RevokedAt is not null)
    {
      return new SupportSessionMutation(SupportMutationStatus.Revoked, null, null);
    }
    if (input.IncidentId is Guid incidentId)
    {
      var incident = await _incidentStore.GetByIdAsync(
          tenantId,
          incidentId,
          cancellationToken);
      if (incident is null)
      {
        return new SupportSessionMutation(SupportMutationStatus.NotFound, null, null);
      }
      if (!string.Equals(
          incident.ProfileId,
          input.ProfileId,
          StringComparison.Ordinal))
      {
        return new SupportSessionMutation(
            SupportMutationStatus.Invalid,
            "Incident correlation does not match the requested profile.",
            null);
      }
    }

    var sessionId = Guid.NewGuid();
    var request = new SupportDiagnosticRequest(
        "support-plane-v1",
        tenantId,
        input.NodeId,
        sessionId,
        SupportCapability.DiagnosticsSnapshotV1,
        1,
        input.DiagnosticMode,
        input.ProfileId,
        sessionId.ToString("N"),
        now,
        now.AddSeconds(seconds),
        _secretService.CreateNonce());
    var requestPayload = SupportCanonicalJson.SerializeRequest(request);
    var requestDigest = Convert.ToHexString(
            SHA256.HashData(requestPayload))
        .ToLowerInvariant();
    var nodeSigningKeyFingerprint = Convert.ToHexString(
            SHA256.HashData(SupportBase64Url.Decode(
                identity.NodeSigningPublicKeySpki)))
        .ToLowerInvariant();
    using var nodeEncryptionKey = SupportKeyFactory.ImportRsaPublicKey(identity.NodeEncryptionPublicKeySpki);
    var envelope = SupportEnvelopeCryptography.Seal(
        requestPayload,
        nodeEncryptionKey,
        _keyService.AuthorizationSigningKey,
        "dashboard-support-auth-v1",
        input.NodeId.ToString("N"));
    var session = new SupportDiagnosticSession(
        tenantId,
        sessionId,
        input.NodeId,
        input.DiagnosticMode,
        input.ProfileId,
        request.PackageId,
        SupportCapability.DiagnosticsSnapshotV1,
        requestDigest,
        nodeSigningKeyFingerprint,
        SupportDiagnosticSessionStatus.Queued,
        decision.ActorId,
        now,
        request.ExpiresAt,
        envelope,
        null,
        null,
        null,
        null,
        null,
        null,
        null)
    {
      IncidentId = input.IncidentId,
    };
    var status = await _supportStore.CreateSessionAsync(
        session,
        input.IntentId,
        identity.NodeSigningPublicKeySpki,
        identity.NodeEncryptionPublicKeySpki,
        cancellationToken);
    if (status != SupportMutationStatus.Succeeded)
    {
      if (status == SupportMutationStatus.Conflict)
      {
        existing = await _supportStore.GetSessionByIntentOrNullAsync(
            tenantId,
            input.IntentId,
            cancellationToken);
        if (existing is not null &&
            MatchesIntent(existing, decision.ActorId, input, seconds))
        {
          return await ReconcileExistingAsync(
              existing,
              decision.ActorId,
              input,
              seconds,
              now,
              cancellationToken);
        }
      }
      return new SupportSessionMutation(status, null, null);
    }
    return await EnqueueAsync(session, now, cancellationToken);
  }

  private async Task<SupportSessionMutation> ReconcileExistingAsync(
      SupportDiagnosticSession session,
      string actorId,
      SupportDiagnosticSessionInput input,
      int lifetimeSeconds,
      DateTimeOffset now,
      CancellationToken cancellationToken)
  {
    if (!MatchesIntent(
        session,
        actorId,
        input,
        lifetimeSeconds))
    {
      return new SupportSessionMutation(
          SupportMutationStatus.Conflict,
          "The request intent is already bound to different diagnostic parameters.",
          session);
    }
    if (session.Status is (
            SupportDiagnosticSessionStatus.Queued or
            SupportDiagnosticSessionStatus.Dispatched) &&
        session.ExpiresAt <= now)
    {
      _ = await _supportStore.UpdateSessionLifecycleAsync(
          session.TenantId,
          session.SessionId,
          SupportDiagnosticSessionStatus.Expired,
          session.DispatchedAt,
          null,
          session.ExpiresAt,
          cancellationToken);
      session = await _supportStore.GetSessionOrNullAsync(
          session.TenantId,
          session.SessionId,
          cancellationToken) ?? session with
          {
            Status = SupportDiagnosticSessionStatus.Expired,
            CompletedAt = session.ExpiresAt,
          };
    }
    return new SupportSessionMutation(
        SupportMutationStatus.Succeeded,
        null,
        session);
  }

  private async Task<SupportSessionMutation> EnqueueAsync(
      SupportDiagnosticSession session,
      DateTimeOffset now,
      CancellationToken cancellationToken)
  {
    var relayStatus = await _relayClient.EnqueueSessionAsync(
        session,
        cancellationToken);
    if (relayStatus == SupportRelayManagementStatus.Conflict)
    {
      await _supportStore.CancelSessionAsync(
          session.TenantId,
          session.SessionId,
          now,
          cancellationToken);
      return new SupportSessionMutation(SupportMutationStatus.Conflict, null, null);
    }
    if (relayStatus == SupportRelayManagementStatus.Unavailable)
    {
      return new SupportSessionMutation(
          SupportMutationStatus.Unavailable,
          "Relay acceptance is not yet confirmed.",
          session);
    }
    return new SupportSessionMutation(
        SupportMutationStatus.Succeeded,
        null,
        session);
  }

  private static string? Validate(SupportDiagnosticSessionInput input)
  {
    if (input.IntentId == Guid.Empty)
    {
      return "A support request intent identifier is required.";
    }
    if (input.NodeId == Guid.Empty)
    {
      return "A support node identifier is required.";
    }
    if (input.IncidentId == Guid.Empty)
    {
      return "Incident ID must be omitted or a non-empty identifier.";
    }
    if (!SupportDiagnosticModes.IsSupported(input.DiagnosticMode))
    {
      return "Diagnostic mode must be one of the support-plane v1 closed modes.";
    }
    if (input.ProfileId is not null && !PitCrewProfileId.IsValid(input.ProfileId))
    {
      return "Profile ID must match a locally configured PitCrew profile identifier.";
    }
    if (input.ExpiresInSeconds is < 300 or > 3600)
    {
      return "Expiry must be between 300 and 3600 seconds.";
    }
    return null;
  }

  private static bool MatchesIntent(
      SupportDiagnosticSession session,
      string actorId,
      SupportDiagnosticSessionInput input,
      int lifetimeSeconds) =>
      string.Equals(
          session.RequestedByGitHubUserId,
          actorId,
          StringComparison.Ordinal) &&
      session.NodeId == input.NodeId &&
      string.Equals(
          session.DiagnosticMode,
          input.DiagnosticMode,
          StringComparison.Ordinal) &&
      string.Equals(
          session.ProfileId,
          input.ProfileId,
          StringComparison.Ordinal) &&
      session.IncidentId == input.IncidentId &&
      session.ExpiresAt - session.RequestedAt ==
          TimeSpan.FromSeconds(lifetimeSeconds);
}
