using PitCrew.Dashboard.Features.Fleet.Abstractions;
using PitCrew.Protocol;

namespace PitCrew.Dashboard.Features.Fleet;

internal sealed record ConnectorEnrollmentInput(
    string ConnectorInstanceId,
    string DisplayName);

internal interface IEnrollConnectorUnitOfWork
{
  Task<ConnectorEnrollmentResponse?> EnrollOrNullAsync(
      string enrollmentCode,
      ConnectorEnrollmentInput input,
      CancellationToken cancellationToken);
}

internal sealed class EnrollConnectorUnitOfWork(
    IFleetStore _fleetStore,
    ConnectorCredentialService _credentialService,
    TimeProvider _timeProvider) : IEnrollConnectorUnitOfWork
{
  public async Task<ConnectorEnrollmentResponse?> EnrollOrNullAsync(
      string enrollmentCode,
      ConnectorEnrollmentInput input,
      CancellationToken cancellationToken)
  {
    var credential = _credentialService.CreateNodeCredential();
    var commit = await _fleetStore.RedeemEnrollmentCodeAsync(
        _credentialService.Hash(enrollmentCode),
        input.ConnectorInstanceId,
        input.DisplayName,
        _credentialService.Hash(credential),
        _timeProvider.GetUtcNow(),
        cancellationToken);
    return commit.Status == ConnectorEnrollmentStatus.Accepted &&
        commit.NodeId is not null
        ? new ConnectorEnrollmentResponse(
            commit.NodeId.Value,
            credential)
        : null;
  }
}
