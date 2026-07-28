using System.Globalization;

using Microsoft.Extensions.Options;

using PitCrew.Dashboard.Features.Fleet.Abstractions;

namespace PitCrew.Dashboard.Features.Fleet;

internal sealed class GetAlertsUnitOfWork(
    IAlertIncidentStore _incidentStore,
    IOptions<FleetDashboardOptions> _options,
    TimeProvider _timeProvider) : IGetAlertsUnitOfWork
{
  public async Task<AlertQueryResult> GetAsync(
      string tenantId,
      AlertQueryInput input,
      CancellationToken cancellationToken)
  {
    var filter = input.Status?.Trim().ToLowerInvariant() switch
    {
      null or "" or "active" => AlertIncidentFilter.Active,
      "resolved" => AlertIncidentFilter.Resolved,
      "all" => AlertIncidentFilter.All,
      _ => (AlertIncidentFilter?)null,
    };
    if (filter is null)
    {
      return Invalid("Status must be 'active', 'resolved', or 'all'.");
    }

    var maximum = _options.Value.MaximumAlertIncidentsPerQuery;
    var limit = maximum;
    if (!string.IsNullOrWhiteSpace(input.Limit) &&
        (!int.TryParse(
            input.Limit,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out limit) ||
         limit < 1 ||
         limit > maximum))
    {
      return Invalid(
          $"The incident limit must be between 1 and {maximum}.");
    }

    var now = _timeProvider.GetUtcNow();
    return new AlertQueryResult(
        AlertQueryStatus.Succeeded,
        null,
        await _incidentStore.GetAsync(
            tenantId,
            filter.Value,
            limit,
            now,
            cancellationToken));
  }

  private static AlertQueryResult Invalid(string error) =>
      new(AlertQueryStatus.Invalid, error, null);
}
