using System.Security.Claims;

using PitCrew.Dashboard.Features.Support.Abstractions;

namespace PitCrew.Dashboard.Features.Support;

internal sealed class RevokeSupportIdentityUnitOfWork(
    SupportDashboardAccessService _accessContextService,
    ISupportStore _supportStore,
    SupportRelayManagementClient _relayClient,
    TimeProvider _timeProvider) : IRevokeSupportIdentityUnitOfWork
{
  public async Task<SupportMutationStatus> RevokeAsync(
      ClaimsPrincipal principal,
      string tenantId,
      Guid nodeId,
      CancellationToken cancellationToken)
  {
    var actor = await _accessContextService.GetOrNullAsync(principal, cancellationToken);
    if (actor is null)
    {
      return SupportMutationStatus.Forbidden;
    }
    var status = await _supportStore.RevokeIdentityAsync(
        tenantId,
        nodeId,
        actor.User.GitHubUserId,
        _timeProvider.GetUtcNow(),
        cancellationToken);
    if (status == SupportMutationStatus.Succeeded)
    {
      var relayStatus = await _relayClient.RevokeNodeAsync(
          nodeId,
          cancellationToken);
      return relayStatus == SupportRelayManagementStatus.Failed
          ? SupportMutationStatus.Conflict
          : status;
    }
    return status;
  }
}
