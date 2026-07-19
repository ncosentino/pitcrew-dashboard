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
    IOptions<FleetDashboardOptions> _options,
    TimeProvider _timeProvider) : IGetFleetUnitOfWork
{
  public Task<FleetResponse> GetAsync(
      string tenantId,
      CancellationToken cancellationToken)
  {
    var generatedAt = _timeProvider.GetUtcNow();
    return _fleetStore.GetFleetAsync(
        tenantId,
        generatedAt,
        TimeSpan.FromSeconds(_options.Value.NodeOfflineAfterSeconds),
        cancellationToken);
  }
}
