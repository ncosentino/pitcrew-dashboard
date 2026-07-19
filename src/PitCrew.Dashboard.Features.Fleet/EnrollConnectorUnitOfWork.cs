using Microsoft.Extensions.Options;

using PitCrew.Dashboard.Features.Fleet.Abstractions;
using PitCrew.Protocol;

namespace PitCrew.Dashboard.Features.Fleet;

internal sealed class EnrollConnectorUnitOfWork(
    IFleetStore _fleetStore,
    ConnectorCredentialService _credentialService,
    IOptions<FleetDashboardOptions> _options,
    TimeProvider _timeProvider)
{
  public async Task<ConnectorEnrollmentResponse?> EnrollOrNullAsync(
      string enrollmentToken,
      ConnectorEnrollmentRequest request,
      CancellationToken cancellationToken)
  {
    if (string.IsNullOrWhiteSpace(_options.Value.EnrollmentToken))
    {
      return null;
    }
    if (!_credentialService.Matches(
        _options.Value.EnrollmentToken,
        enrollmentToken))
    {
      return null;
    }

    var credential = _credentialService.CreateCredential();
    var nodeId = await _fleetStore.EnrollNodeAsync(
        _options.Value.TenantId,
        request.ConnectorInstanceId,
        request.DisplayName,
        _credentialService.Hash(credential),
        _timeProvider.GetUtcNow(),
        cancellationToken);
    return new ConnectorEnrollmentResponse(nodeId, credential);
  }
}
