using Microsoft.Extensions.Options;

using PitCrew.Dashboard.Features.Fleet.Abstractions;

namespace PitCrew.Dashboard.Features.Fleet;

internal sealed class GetFleetUnitOfWork(
    IFleetStore _fleetStore,
    IOptions<FleetDashboardOptions> _options,
    TimeProvider _timeProvider)
{
  public Task<FleetResponse> GetAsync(CancellationToken cancellationToken)
  {
    var generatedAt = _timeProvider.GetUtcNow();
    return _fleetStore.GetFleetAsync(
        _options.Value.TenantId,
        generatedAt,
        TimeSpan.FromSeconds(_options.Value.NodeOfflineAfterSeconds),
        cancellationToken);
  }
}
