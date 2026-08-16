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
    if (actor is null ||
        !await _accessContextService.IsTenantAdministratorAsync(
            actor,
            tenantId,
            cancellationToken))
    {
      return SupportMutationStatus.Forbidden;
    }
    var identity = await _supportStore.GetIdentityOrNullAsync(
        tenantId,
        nodeId,
        cancellationToken);
    if (identity is null)
    {
      return SupportMutationStatus.NotFound;
    }
    if (identity.RevokedAt is not null)
    {
      return SupportMutationStatus.Conflict;
    }
    var relayStatus = await _relayClient.RevokeNodeAsync(
        nodeId,
        cancellationToken);
    if (relayStatus == SupportRelayManagementStatus.Failed)
    {
      return SupportMutationStatus.Conflict;
    }
    return await _supportStore.RevokeIdentityAsync(
        tenantId,
        nodeId,
        actor.User.GitHubUserId,
        _timeProvider.GetUtcNow(),
        cancellationToken);
  }
}
