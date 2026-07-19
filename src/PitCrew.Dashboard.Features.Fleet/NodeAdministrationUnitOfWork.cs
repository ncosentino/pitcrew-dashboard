using PitCrew.Dashboard.Features.Fleet.Abstractions;

namespace PitCrew.Dashboard.Features.Fleet;

internal interface INodeAdministrationUnitOfWork
{
  Task<NodeMutationStatus> RevokeAsync(
      string tenantId,
      Guid nodeId,
      CancellationToken cancellationToken);

  Task<NodeMutationStatus> RequestCredentialRotationAsync(
      string tenantId,
      Guid nodeId,
      CancellationToken cancellationToken);
}

internal sealed class NodeAdministrationUnitOfWork(
    IFleetStore _fleetStore,
    TimeProvider _timeProvider) : INodeAdministrationUnitOfWork
{
  public Task<NodeMutationStatus> RevokeAsync(
      string tenantId,
      Guid nodeId,
      CancellationToken cancellationToken) =>
      _fleetStore.RevokeNodeAsync(
          tenantId,
          nodeId,
          _timeProvider.GetUtcNow(),
          cancellationToken);

  public Task<NodeMutationStatus> RequestCredentialRotationAsync(
      string tenantId,
      Guid nodeId,
      CancellationToken cancellationToken) =>
      _fleetStore.RequestCredentialRotationAsync(
          tenantId,
          nodeId,
          _timeProvider.GetUtcNow(),
          cancellationToken);
}
