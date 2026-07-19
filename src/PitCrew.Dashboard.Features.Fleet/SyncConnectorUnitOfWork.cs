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

internal sealed class SyncConnectorUnitOfWork(
    IFleetStore _fleetStore,
    ConnectorCredentialService _credentialService,
    IOptions<FleetDashboardOptions> _options,
    TimeProvider _timeProvider)
{
  public async Task<ConnectorSyncResult> SynchronizeAsync(
      string credential,
      ConnectorSyncRequest request,
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

    if (request.ProtocolVersion != PitCrewProtocol.Version)
    {
      return new ConnectorSyncResult(
          ConnectorSyncStatus.Invalid,
          $"Unsupported connector protocol version '{request.ProtocolVersion}'.",
          null);
    }
    if (string.IsNullOrWhiteSpace(request.ConnectorVersion) ||
        request.ConnectorVersion.Length > 128)
    {
      return new ConnectorSyncResult(
          ConnectorSyncStatus.Invalid,
          "Connector version must be between 1 and 128 characters.",
          null);
    }
    if (request.Profiles is null ||
        request.Profiles.Count > 256)
    {
      return new ConnectorSyncResult(
          ConnectorSyncStatus.Invalid,
          "A connector cannot synchronize more than 256 profiles.",
          null);
    }

    var profileIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var profile in request.Profiles)
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
    await _fleetStore.ApplySyncAsync(
        identity.NodeId,
        request.ConnectorVersion,
        acceptedAt,
        request.Profiles,
        cancellationToken);
    return new ConnectorSyncResult(
        ConnectorSyncStatus.Accepted,
        null,
        new ConnectorSyncResponse(
            acceptedAt,
            _options.Value.ConnectorPollSeconds));
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
