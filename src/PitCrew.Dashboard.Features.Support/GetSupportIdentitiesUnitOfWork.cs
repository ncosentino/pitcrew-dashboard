using PitCrew.Dashboard.Features.Support.Abstractions;

namespace PitCrew.Dashboard.Features.Support;

internal sealed class GetSupportIdentitiesUnitOfWork(
    ISupportStore _supportStore,
    SupportRelayManagementClient _relayClient) : IGetSupportIdentitiesUnitOfWork
{
  public async Task<IReadOnlyList<SupportIdentity>> GetAsync(
      string tenantId,
      CancellationToken cancellationToken)
  {
    var identities = await _supportStore.GetIdentitiesAsync(
        tenantId,
        cancellationToken);
    if (identities.Count == 0)
    {
      return identities;
    }
    var activity = await _relayClient.GetNodeActivityAsync(
        tenantId,
        identities.Select(identity => identity.NodeId).ToArray(),
        cancellationToken);
    if (activity is null || activity.Count == 0)
    {
      return identities;
    }
    await _supportStore.UpdateIdentityActivityAsync(
        tenantId,
        activity,
        cancellationToken);
    var activityByNode = activity.ToDictionary(item => item.NodeId);
    return identities
        .Select(identity =>
            activityByNode.TryGetValue(identity.NodeId, out var item)
                ? identity with
                {
                  LastPollAt = Latest(
                      identity.LastPollAt,
                      item.LastPollAt),
                  LastResultAt = Latest(
                      identity.LastResultAt,
                      item.LastResultAt),
                }
                : identity)
        .ToArray();
  }

  private static DateTimeOffset? Latest(
      DateTimeOffset? current,
      DateTimeOffset? candidate) =>
      current is null || candidate > current
          ? candidate ?? current
          : current;
}
