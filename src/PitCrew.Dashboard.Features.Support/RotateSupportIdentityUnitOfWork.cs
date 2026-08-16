using Microsoft.Extensions.Options;

using PitCrew.Dashboard.Features.Support.Abstractions;

namespace PitCrew.Dashboard.Features.Support;

internal sealed class RotateSupportIdentityUnitOfWork(
    ISupportStore _supportStore,
    SupportSecretService _secretService,
    DashboardSupportKeyService _keyService,
    SupportRelayManagementClient _relayClient,
    IOptions<SupportPlaneOptions> _options,
    TimeProvider _timeProvider) : IRotateSupportIdentityUnitOfWork
{
  public async Task<SupportIdentityRotationCompletion> RotateAsync(
      RotateSupportIdentityInput input,
      CancellationToken cancellationToken)
  {
    if (input.RotationId == Guid.Empty ||
        !IsTenantValid(input.TenantId) ||
        !IsCredentialValid(input.CurrentTransportCredential) ||
        !IsCredentialValid(input.ReplacementTransportCredential) ||
        string.Equals(
            input.CurrentTransportCredential,
            input.ReplacementTransportCredential,
            StringComparison.Ordinal) ||
        !SupportPublicKeyValidator.AreValid(
            input.NodeSigningPublicKeySpki,
            input.NodeEncryptionPublicKeySpki))
    {
      return Failed(SupportMutationStatus.Invalid);
    }
    var rotation = new SupportIdentityRotation(
        input.RotationId,
        input.TenantId,
        input.NodeId,
        _secretService.Hash(input.CurrentTransportCredential),
        _secretService.Hash(input.ReplacementTransportCredential),
        input.NodeSigningPublicKeySpki,
        input.NodeEncryptionPublicKeySpki);
    var authorization = await _supportStore.GetIdentityRotationStatusAsync(
        rotation,
        cancellationToken);
    var rejected = MapRejectedStatus(authorization);
    if (rejected is not null)
    {
      return Failed(rejected.Value);
    }
    var relayStatus = await _relayClient.PrepareNodeCredentialAsync(
        rotation,
        cancellationToken);
    if (relayStatus == SupportRelayManagementStatus.Failed)
    {
      return Failed(SupportMutationStatus.Conflict);
    }
    if (authorization == SupportIdentityRotationStatus.Authorized)
    {
      var status = await _supportStore.PrepareIdentityRotationAsync(
          rotation,
          _timeProvider.GetUtcNow(),
          cancellationToken);
      if (status != SupportMutationStatus.Succeeded)
      {
        return Failed(status);
      }
    }
    return await CompletedAsync(
        input.TenantId,
        input.NodeId,
        input.ReplacementTransportCredential,
        cancellationToken);
  }

  public async Task<SupportIdentityRotationCompletion> FinalizeAsync(
      FinalizeSupportIdentityRotationInput input,
      CancellationToken cancellationToken)
  {
    if (input.RotationId == Guid.Empty ||
        !IsTenantValid(input.TenantId) ||
        !IsCredentialValid(input.CurrentTransportCredential))
    {
      return Failed(SupportMutationStatus.Invalid);
    }
    var stored = await _supportStore.GetIdentityRotationOrNullAsync(
        input.TenantId,
        input.NodeId,
        input.RotationId,
        cancellationToken);
    if (stored is null)
    {
      return Failed(SupportMutationStatus.NotFound);
    }
    if (!string.Equals(
        _secretService.Hash(input.CurrentTransportCredential),
        stored.Rotation.ReplacementTransportCredentialHash,
        StringComparison.Ordinal))
    {
      return Failed(SupportMutationStatus.Forbidden);
    }
    var authorization = await _supportStore.GetIdentityRotationStatusAsync(
        stored.Rotation,
        cancellationToken);
    var rejected = MapRejectedStatus(authorization);
    if (rejected is not null)
    {
      return Failed(rejected.Value);
    }
    if (authorization == SupportIdentityRotationStatus.Prepared)
    {
      var promoted = await _supportStore.PromoteIdentityRotationAsync(
          stored.Rotation,
          _timeProvider.GetUtcNow(),
          cancellationToken);
      if (promoted != SupportMutationStatus.Succeeded)
      {
        return Failed(promoted);
      }
    }
    if (authorization != SupportIdentityRotationStatus.Finalized)
    {
      var relayStatus = await _relayClient.PromoteNodeCredentialAsync(
          stored.Rotation,
          cancellationToken);
      if (relayStatus == SupportRelayManagementStatus.Failed)
      {
        return Failed(SupportMutationStatus.Conflict);
      }
      var finalized = await _supportStore.FinalizeIdentityRotationAsync(
          stored.Rotation,
          _timeProvider.GetUtcNow(),
          cancellationToken);
      if (finalized != SupportMutationStatus.Succeeded)
      {
        return Failed(finalized);
      }
    }
    return await CompletedAsync(
        input.TenantId,
        input.NodeId,
        input.CurrentTransportCredential,
        cancellationToken);
  }

  private async Task<SupportIdentityRotationCompletion> CompletedAsync(
      string tenantId,
      Guid nodeId,
      string transportCredential,
      CancellationToken cancellationToken)
  {
    var identity = await _supportStore.GetIdentityOrNullAsync(
        tenantId,
        nodeId,
        cancellationToken);
    return identity is null
        ? Failed(SupportMutationStatus.NotFound)
        : new SupportIdentityRotationCompletion(
            SupportMutationStatus.Succeeded,
            new CreatedSupportEnrollment(
                identity,
                transportCredential,
                _options.Value.RelayUrl,
                _keyService.AuthorizationSigningPublicKeySpki,
                _keyService.ResultEncryptionPublicKeySpki));
  }

  private static SupportMutationStatus? MapRejectedStatus(
      SupportIdentityRotationStatus status) =>
      status switch
      {
        SupportIdentityRotationStatus.NotFound =>
            SupportMutationStatus.NotFound,
        SupportIdentityRotationStatus.Revoked =>
            SupportMutationStatus.Revoked,
        SupportIdentityRotationStatus.Forbidden =>
            SupportMutationStatus.Forbidden,
        SupportIdentityRotationStatus.ActiveSessions or
        SupportIdentityRotationStatus.Conflict =>
            SupportMutationStatus.Conflict,
        _ => null,
      };

  private static SupportIdentityRotationCompletion Failed(
      SupportMutationStatus status) =>
      new(status, null);

  private static bool IsTenantValid(string tenantId) =>
      !string.IsNullOrWhiteSpace(tenantId) && tenantId.Length <= 128;

  private static bool IsCredentialValid(string credential) =>
      credential.Length is >= 32 and <= 256 &&
      !credential.Contains('\r') &&
      !credential.Contains('\n');
}
