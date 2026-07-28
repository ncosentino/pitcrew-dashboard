using PitCrew.Dashboard.Features.Fleet.Abstractions;

namespace PitCrew.Dashboard.Features.Fleet;

internal sealed record AlertQueryInput(
    string? Status,
    string? Limit);

internal enum AlertQueryStatus
{
  Succeeded,
  Invalid,
}

internal sealed record AlertQueryResult(
    AlertQueryStatus Status,
    string? Error,
    AlertIncidentPage? Page);

internal interface IGetAlertsUnitOfWork
{
  Task<AlertQueryResult> GetAsync(
      string tenantId,
      AlertQueryInput input,
      CancellationToken cancellationToken);
}
