using System.Security.Claims;

using Microsoft.Extensions.Options;

using PitCrew.Dashboard.Features.Access;
using PitCrew.Dashboard.Features.Access.Abstractions;
using PitCrew.Dashboard.Features.Fleet.Abstractions;

namespace PitCrew.Dashboard.Features.Fleet;

internal sealed class GetDiagnosticProfileStatusUnitOfWork(
    IDiagnosticAccessScopeAccessor _scopeAccessor,
    IFleetStore _fleetStore,
    ICapacityCommandStore _capacityCommandStore,
    IOptions<FleetDashboardOptions> _options,
    TimeProvider _timeProvider) :
    IGetDiagnosticProfileStatusUnitOfWork
{
  public async Task<DiagnosticProfileStatusQueryResult> GetAsync(
      ClaimsPrincipal principal,
      string tenantId,
      Guid nodeId,
      string profileId,
      CancellationToken cancellationToken)
  {
    var scope = DiagnosticFleetAccess.GetScopeOrNull(
        _scopeAccessor,
        principal,
        tenantId);
    if (scope is null ||
        !DiagnosticFleetAccess.AllowsNode(scope, nodeId) ||
        !DiagnosticFleetAccess.AllowsProfile(scope, profileId))
    {
      return Forbidden();
    }

    var generatedAt = _timeProvider.GetUtcNow();
    var fleetTask = _fleetStore.GetFleetAsync(
        tenantId,
        generatedAt,
        TimeSpan.FromSeconds(
            _options.Value.NodeOfflineAfterSeconds),
        cancellationToken);
    var controlsTask = _capacityCommandStore.GetControlsAsync(
        tenantId,
        cancellationToken);
    await Task.WhenAll(fleetTask, controlsTask);

    var fleet = await fleetTask;
    var node = fleet.Nodes.FirstOrDefault(candidate =>
        candidate.NodeId == nodeId);
    if (node is null)
    {
      return NotFound();
    }
    var controls = await controlsTask;
    var nodeControls = controls.FirstOrDefault(candidate =>
        candidate.NodeId == nodeId);
    var capacityControl = nodeControls?.Profiles.FirstOrDefault(candidate =>
        string.Equals(
            candidate.ProfileId,
            profileId,
            StringComparison.Ordinal));
    var profileStatus = ProfileOperationalStatusProjector.Project(
        node,
        profileId,
        capacityControl,
        generatedAt,
        TimeSpan.FromSeconds(
            _options.Value.AlertManagerStaleAfterSeconds));
    return new DiagnosticProfileStatusQueryResult(
        DiagnosticQueryStatus.Succeeded,
        null,
        profileStatus);
  }

  private static DiagnosticProfileStatusQueryResult Forbidden() =>
      new(
          DiagnosticQueryStatus.Forbidden,
          null,
          null);

  private static DiagnosticProfileStatusQueryResult NotFound() =>
      new(
          DiagnosticQueryStatus.NotFound,
          null,
          null);
}
