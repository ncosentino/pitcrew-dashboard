using Microsoft.Extensions.Options;

using PitCrew.Dashboard.Features.Fleet.Abstractions;

namespace PitCrew.Dashboard.Features.Fleet;

internal interface IGetFleetUnitOfWork
{
  Task<FleetResponse> GetAsync(
      string tenantId,
      CancellationToken cancellationToken);
}

internal sealed class GetFleetUnitOfWork(
    IFleetStore _fleetStore,
    ICapacityCommandStore _capacityCommandStore,
    IOptions<FleetDashboardOptions> _options,
    TimeProvider _timeProvider) : IGetFleetUnitOfWork
{
  public async Task<FleetResponse> GetAsync(
      string tenantId,
      CancellationToken cancellationToken)
  {
    var generatedAt = _timeProvider.GetUtcNow();
    var fleetTask = _fleetStore.GetFleetAsync(
        tenantId,
        generatedAt,
        TimeSpan.FromSeconds(_options.Value.NodeOfflineAfterSeconds),
        cancellationToken);
    var controlsTask = _capacityCommandStore.GetControlsAsync(
        tenantId,
        cancellationToken);
    await Task.WhenAll(fleetTask, controlsTask);

    var fleet = await fleetTask;
    var controls = (await controlsTask).ToDictionary(
        item => item.NodeId);
    return fleet with
    {
      Nodes = fleet.Nodes
          .Select(node => node with
          {
            CapacityControls = controls.TryGetValue(
                node.NodeId,
                out var nodeControls)
                ? nodeControls.Profiles
                : [],
          })
          .ToArray(),
    };
  }
}
