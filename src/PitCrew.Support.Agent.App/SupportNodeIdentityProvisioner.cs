using Microsoft.Extensions.Configuration;

using PitCrew.Support.Protocol;

namespace PitCrew.Support.Agent.App;

internal sealed class SupportNodeIdentityProvisioner(
    SupportNodeIdentityStore _store,
    SupportDashboardIdentityClient _dashboardClient,
    SupportAgentBootstrapOptions _bootstrapOptions,
    IConfiguration _configuration)
{
  public async Task<SupportAgentProvisioningOutcome> GetRuntimeOptionsAsync(
      CancellationToken cancellationToken)
  {
    await using var operationLock =
        await _store.AcquireOperationLockAsync(cancellationToken);
    var status = await _store.GetStatusAsync(cancellationToken);
    if (status.Lifecycle == SupportNodeIdentityLifecycle.Active)
    {
      var active = await _store.LoadActiveAsync(cancellationToken);
      return active is null
          ? new(
              SupportAgentProvisioningStatus.ActiveIdentityUnavailable,
              null)
          : new(
              SupportAgentProvisioningStatus.Ready,
              SupportAgentOptions.FromStoredIdentity(
                  active,
                  _bootstrapOptions.SocketPath));
    }
    if (status.Lifecycle is not (
        SupportNodeIdentityLifecycle.Missing or
        SupportNodeIdentityLifecycle.PendingEnrollment))
    {
      return new(
          SupportAgentProvisioningStatus.IdentityLifecycleUnavailable,
          null);
    }
    if (_bootstrapOptions.HasEnrollmentMaterial)
    {
      var pending = await _store.GetOrCreatePendingEnrollmentAsync(
          _bootstrapOptions.TenantId!,
          _bootstrapOptions.DisplayName!,
          _bootstrapOptions.DashboardUrl!.AbsoluteUri,
          _bootstrapOptions.ReplayRoot,
          _bootstrapOptions.PipeName,
          cancellationToken);
      if (pending is null)
      {
        return new(
            SupportAgentProvisioningStatus.PendingIdentityUnavailable,
            null);
      }
      var completed = await _dashboardClient.CompleteEnrollmentAsync(
          _bootstrapOptions.DashboardUrl,
          pending.TenantId,
          _bootstrapOptions.EnrollmentCode!,
          pending.CompletionId,
          pending.Keys,
          cancellationToken);
      if (!IsEnrollmentCompletionValid(completed, pending.DisplayName))
      {
        return new(
            SupportAgentProvisioningStatus.EnrollmentRejected,
            null);
      }
      if (!await _store.CompleteEnrollmentAsync(completed!, cancellationToken))
      {
        return new(
            SupportAgentProvisioningStatus.LocalEnrollmentCommitFailed,
            null);
      }
      var active = await _store.LoadActiveAsync(cancellationToken);
      return active is null
          ? new(
              SupportAgentProvisioningStatus.ActiveIdentityUnavailable,
              null)
          : new(
              SupportAgentProvisioningStatus.Ready,
              SupportAgentOptions.FromStoredIdentity(
                  active,
                  _bootstrapOptions.SocketPath));
    }
    if (status.Lifecycle == SupportNodeIdentityLifecycle.PendingEnrollment)
    {
      return new(
          SupportAgentProvisioningStatus.EnrollmentMaterialUnavailable,
          null);
    }
    var legacy = _bootstrapOptions.CreateLegacyOrNull(_configuration);
    return legacy is null
        ? new(
            SupportAgentProvisioningStatus.LegacyConfigurationUnavailable,
            null)
        : new(
            SupportAgentProvisioningStatus.Ready,
            legacy);
  }

  public async Task<SupportNodeRotationOutcome> RotateAsync(
      CancellationToken cancellationToken)
  {
    await using var operationLock =
        await _store.AcquireOperationLockAsync(cancellationToken);
    var pending = await _store.GetPendingRotationFinalizationAsync(
        cancellationToken);
    if (pending is not null)
    {
      return await FinalizeAsync(
          pending,
          resumed: true,
          cancellationToken);
    }
    var rotation = await _store.StageRotationAsync(cancellationToken);
    if (rotation is null)
    {
      return new SupportNodeRotationOutcome(
          SupportNodeRotationStatus.IdentityUnavailable,
          null);
    }
    var completed = await _dashboardClient.PrepareRotationAsync(
        rotation,
        cancellationToken);
    if (!IsCompletionValid(completed, expectedDisplayName: null) ||
        completed!.NodeId != rotation.NodeId ||
        !string.Equals(
            completed.TransportCredential,
            rotation.ReplacementTransportCredential,
            StringComparison.Ordinal))
    {
      return new SupportNodeRotationOutcome(
          SupportNodeRotationStatus.PrepareRejected,
          rotation.RotationId);
    }
    if (!await _store.CommitRotationAsync(
        rotation.RotationId,
        completed,
        cancellationToken))
    {
      return new SupportNodeRotationOutcome(
          SupportNodeRotationStatus.LocalCommitFailed,
          rotation.RotationId);
    }
    pending = await _store.GetPendingRotationFinalizationAsync(
        cancellationToken);
    return pending is null
        ? new SupportNodeRotationOutcome(
            SupportNodeRotationStatus.LocalCommitFailed,
            rotation.RotationId)
        : await FinalizeAsync(
            pending,
            resumed: false,
            cancellationToken);
  }

  private async Task<SupportNodeRotationOutcome> FinalizeAsync(
      PendingSupportIdentityRotation pending,
      bool resumed,
      CancellationToken cancellationToken)
  {
    var completed = await _dashboardClient.FinalizeRotationAsync(
        pending,
        cancellationToken);
    if (!IsCompletionValid(completed, expectedDisplayName: null) ||
        completed!.NodeId != pending.NodeId ||
        !string.Equals(
            completed.TransportCredential,
            pending.CurrentTransportCredential,
            StringComparison.Ordinal))
    {
      return new SupportNodeRotationOutcome(
          SupportNodeRotationStatus.FinalizationPending,
          pending.RotationId);
    }
    if (!await _store.CompleteRotationFinalizationAsync(
        pending.RotationId,
        cancellationToken))
    {
      return new SupportNodeRotationOutcome(
          SupportNodeRotationStatus.FinalizationPending,
          pending.RotationId);
    }
    return new SupportNodeRotationOutcome(
        resumed
            ? SupportNodeRotationStatus.ResumedAndSucceeded
            : SupportNodeRotationStatus.Succeeded,
        pending.RotationId);
  }

  private static bool IsCompletionValid(
      SupportIdentityCompletionData? completion,
      string? expectedDisplayName)
  {
    if (completion is null ||
        completion.NodeId == Guid.Empty ||
        completion.TransportCredential.Length is < 32 or > 256 ||
        expectedDisplayName is not null &&
        !string.Equals(
            completion.DisplayName,
            expectedDisplayName,
            StringComparison.Ordinal))
    {
      return false;
    }
    try
    {
      var relayUrl = new Uri(completion.RelayUrl, UriKind.Absolute);
      if (!SupportAgentBootstrapOptions.IsAllowedOrigin(relayUrl))
      {
        return false;
      }
      using var signing = SupportKeyFactory.ImportEcdsaPublicKey(
          completion.DashboardAuthorizationSigningPublicKeySpki);
      using var encryption = SupportKeyFactory.ImportRsaPublicKey(
          completion.DashboardResultEncryptionPublicKeySpki);
      return signing.KeySize == 256 && encryption.KeySize == 3072;
    }
    catch (System.Security.Cryptography.CryptographicException)
    {
      return false;
    }
    catch (FormatException)
    {
      return false;
    }
  }

  private static bool IsEnrollmentCompletionValid(
      SupportEnrollmentCompletionData? completion,
      string expectedDisplayName)
  {
    if (completion is null ||
        completion.NodeId == Guid.Empty ||
        !string.Equals(
            completion.DisplayName,
            expectedDisplayName,
            StringComparison.Ordinal))
    {
      return false;
    }
    try
    {
      var relayUrl = new Uri(completion.RelayUrl, UriKind.Absolute);
      if (!SupportAgentBootstrapOptions.IsAllowedOrigin(relayUrl))
      {
        return false;
      }
      using var signing = SupportKeyFactory.ImportEcdsaPublicKey(
          completion.DashboardAuthorizationSigningPublicKeySpki);
      using var encryption = SupportKeyFactory.ImportRsaPublicKey(
          completion.DashboardResultEncryptionPublicKeySpki);
      return signing.KeySize == 256 && encryption.KeySize == 3072;
    }
    catch (System.Security.Cryptography.CryptographicException)
    {
      return false;
    }
    catch (FormatException)
    {
      return false;
    }
  }
}

internal sealed record SupportNodeRotationOutcome(
    SupportNodeRotationStatus Status,
    Guid? RotationId)
{
  public bool Succeeded =>
      Status is SupportNodeRotationStatus.Succeeded or
      SupportNodeRotationStatus.ResumedAndSucceeded;
}

internal enum SupportNodeRotationStatus
{
  Succeeded,
  ResumedAndSucceeded,
  IdentityUnavailable,
  PrepareRejected,
  LocalCommitFailed,
  FinalizationPending,
}
