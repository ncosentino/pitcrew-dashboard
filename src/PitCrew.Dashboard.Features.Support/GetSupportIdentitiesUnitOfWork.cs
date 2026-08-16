using PitCrew.Dashboard.Features.Support.Abstractions;

namespace PitCrew.Dashboard.Features.Support;

internal sealed class GetSupportIdentitiesUnitOfWork(
    ISupportStore _supportStore) : IGetSupportIdentitiesUnitOfWork
{
  public Task<IReadOnlyList<SupportIdentity>> GetAsync(
      string tenantId,
      CancellationToken cancellationToken) =>
      _supportStore.GetIdentitiesAsync(tenantId, cancellationToken);
}
