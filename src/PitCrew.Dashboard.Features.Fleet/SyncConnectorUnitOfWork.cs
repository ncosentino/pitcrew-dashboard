using Microsoft.Extensions.Options;

using PitCrew.Dashboard.Features.Fleet.Abstractions;
using PitCrew.Protocol;

namespace PitCrew.Dashboard.Features.Fleet;

internal enum ConnectorSyncStatus
{
  Accepted,
  Unauthorized,
  Invalid,
}

internal sealed record ConnectorSyncResult(
    ConnectorSyncStatus Status,
    string? Error,
    ConnectorSyncResponse? Response);

internal sealed record ConnectorSynchronizationInput(
    int ProtocolVersion,
    string ConnectorVersion,
    DateTimeOffset SentAt,
    IReadOnlyList<ManagerObservedState> Profiles);

internal interface ISyncConnectorUnitOfWork
{
  Task<ConnectorSyncResult> SynchronizeAsync(
      string credential,
      ConnectorSynchronizationInput input,
      CancellationToken cancellationToken);
}

internal sealed class SyncConnectorUnitOfWork(
    IFleetStore _fleetStore,
    ConnectorCredentialService _credentialService,
    IOptions<FleetDashboardOptions> _options,
    TimeProvider _timeProvider) : ISyncConnectorUnitOfWork
{
  public async Task<ConnectorSyncResult> SynchronizeAsync(
      string credential,
      ConnectorSynchronizationInput input,
      CancellationToken cancellationToken)
  {
    var identity = await _fleetStore.ResolveNodeOrNullAsync(
        _credentialService.Hash(credential),
        cancellationToken);
    if (identity is null)
    {
      return new ConnectorSyncResult(
          ConnectorSyncStatus.Unauthorized,
          null,
          null);
    }

    if (input.ProtocolVersion < PitCrewProtocol.MinimumSupportedVersion ||
        input.ProtocolVersion > PitCrewProtocol.Version)
    {
      return new ConnectorSyncResult(
          ConnectorSyncStatus.Invalid,
          $"Unsupported connector protocol version '{input.ProtocolVersion}'.",
          null);
    }
    if (string.IsNullOrWhiteSpace(input.ConnectorVersion) ||
        input.ConnectorVersion.Length > 128)
    {
      return new ConnectorSyncResult(
          ConnectorSyncStatus.Invalid,
          "Connector version must be between 1 and 128 characters.",
          null);
    }
    if (input.Profiles is null ||
        input.Profiles.Count > 256)
    {
      return new ConnectorSyncResult(
          ConnectorSyncStatus.Invalid,
          "A connector cannot synchronize more than 256 profiles.",
          null);
    }

    var profileIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var profile in input.Profiles)
    {
      if (!IsValidProfile(profile))
      {
        return new ConnectorSyncResult(
            ConnectorSyncStatus.Invalid,
            $"Profile '{profile.ProfileId}' does not satisfy the observed-state contract.",
            null);
      }
      if (!profileIds.Add(profile.ProfileId))
      {
        return new ConnectorSyncResult(
            ConnectorSyncStatus.Invalid,
            $"Profile '{profile.ProfileId}' appears more than once.",
            null);
      }
    }

    var acceptedAt = _timeProvider.GetUtcNow();
    var credentialUpdate = new ConnectorCredentialUpdate(
        ConnectorCredentialUpdateKind.None,
        string.Empty);
    ConnectorCredentialRotation? credentialRotation = null;
    if (identity.CredentialSlot == ConnectorCredentialSlot.Pending)
    {
      credentialUpdate = new ConnectorCredentialUpdate(
          ConnectorCredentialUpdateKind.Promote,
          _credentialService.Hash(credential));
    }
    else if (identity.RotationRequested &&
        input.ProtocolVersion >= 2)
    {
      var replacement = _credentialService.CreateNodeCredential();
      credentialUpdate = new ConnectorCredentialUpdate(
          ConnectorCredentialUpdateKind.Stage,
          _credentialService.Hash(replacement));
      credentialRotation = new ConnectorCredentialRotation(
          replacement);
    }

    await _fleetStore.ApplySyncAsync(
        identity.NodeId,
        input.ConnectorVersion,
        acceptedAt,
        input.Profiles,
        credentialUpdate,
        cancellationToken);
    return new ConnectorSyncResult(
        ConnectorSyncStatus.Accepted,
        null,
        new ConnectorSyncResponse(
            acceptedAt,
            _options.Value.ConnectorPollSeconds,
            credentialRotation));
  }

  private static bool IsValidProfile(ManagerObservedState profile)
  {
    if (profile.SchemaVersion != 1 ||
        profile.ManagerContractVersion < 5 ||
        string.IsNullOrWhiteSpace(profile.ProfileId) ||
        !IsValidProfileId(profile.ProfileId) ||
        string.IsNullOrWhiteSpace(profile.ManagerInstanceId) ||
        profile.ManagerInstanceId.Length > 128 ||
        profile.ObservedAt == default ||
        profile.Generation < 0 ||
        profile.DesiredSlots < 0 ||
        profile.ActiveSlots < 0 ||
        profile.DrainingSlots < 0 ||
        profile.Slots is null ||
        profile.Slots.Count > 10000 ||
        profile.ActiveSlots != profile.Slots.Count(slot => slot.ProcessRunning) ||
        profile.DrainingSlots != profile.Slots.Count(slot =>
            string.Equals(
                slot.State,
                "draining",
                StringComparison.OrdinalIgnoreCase)) ||
        profile.ManagerStatus is not (
            "starting" or
            "running" or
            "stopping" or
            "stopped") ||
        profile.Scope is not ("repo" or "org" or "ent") ||
        profile.DesiredStateStatus is not (
            "waiting" or
            "accepted" or
            "invalid" or
            "stale" or
            "conflict") ||
        profile.DesiredStateHash is not null &&
        profile.DesiredStateHash.Length != 64)
    {
      return false;
    }

    var slotKeys = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase);
    foreach (var slot in profile.Slots)
    {
      if (string.IsNullOrWhiteSpace(slot.Key) ||
      slot.Key.Length > 128 ||
      !slotKeys.Add(slot.Key) ||
      slot.Repository?.Length > 2048 ||
      slot.FailureCount < 0 ||
          slot.BackoffSeconds < 0 ||
          slot.State is not (
              "starting" or
              "online" or
              "backoff" or
              "restarting" or
              "draining" or
              "stopped"))
      {
        return false;
      }
    }

    return true;
  }

  private static bool IsValidProfileId(string profileId)
  {
    if (profileId.Length is < 1 or > 32 ||
        profileId[0] is < 'a' or > 'z')
    {
      return false;
    }

    return profileId.All(character =>
        character is >= 'a' and <= 'z' or
            >= '0' and <= '9' or
            '-');
  }
}
