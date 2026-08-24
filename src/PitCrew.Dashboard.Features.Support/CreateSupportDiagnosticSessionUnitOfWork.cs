using System.Security.Claims;
using System.Security.Cryptography;

using Microsoft.Extensions.Options;

using PitCrew.Dashboard.Features.Support.Abstractions;
using PitCrew.Protocol;
using PitCrew.Support.Protocol;

namespace PitCrew.Dashboard.Features.Support;

internal sealed class CreateSupportDiagnosticSessionUnitOfWork(
    SupportPrincipalAuthorizer _authorizer,
    ISupportStore _supportStore,
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
        input.NodeId,
        input.ProfileId,
        cancellationToken);
    if (!decision.Allowed || decision.ActorId is null)
    {
      return new SupportSessionMutation(SupportMutationStatus.Forbidden, null, null);
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

    var now = _timeProvider.GetUtcNow();
    var seconds = Math.Min(input.ExpiresInSeconds, _options.Value.MaximumSessionLifetimeSeconds);
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
        null);
    var status = await _supportStore.CreateSessionAsync(
        session,
        identity.NodeSigningPublicKeySpki,
        identity.NodeEncryptionPublicKeySpki,
        cancellationToken);
    if (status != SupportMutationStatus.Succeeded)
    {
      return new SupportSessionMutation(status, null, null);
    }
    var relayStatus = await _relayClient.EnqueueSessionAsync(
        session,
        cancellationToken);
    if (relayStatus == SupportRelayManagementStatus.Failed)
    {
      await _supportStore.CancelSessionAsync(
          tenantId,
          sessionId,
          now,
          cancellationToken);
      return new SupportSessionMutation(SupportMutationStatus.Conflict, null, null);
    }
    return new SupportSessionMutation(
        status,
        null,
        session);
  }

  private static string? Validate(SupportDiagnosticSessionInput input)
  {
    if (input.NodeId == Guid.Empty)
    {
      return "A support node identifier is required.";
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
}
