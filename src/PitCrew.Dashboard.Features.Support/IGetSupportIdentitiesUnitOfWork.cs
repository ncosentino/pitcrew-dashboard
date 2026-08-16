using PitCrew.Dashboard.Features.Support.Abstractions;

namespace PitCrew.Dashboard.Features.Support;

internal interface IGetSupportIdentitiesUnitOfWork
{
  Task<IReadOnlyList<SupportIdentity>> GetAsync(
      string tenantId,
      CancellationToken cancellationToken);
}
