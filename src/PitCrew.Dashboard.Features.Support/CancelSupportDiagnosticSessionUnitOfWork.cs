using System.Security.Claims;

using PitCrew.Dashboard.Features.Support.Abstractions;

namespace PitCrew.Dashboard.Features.Support;

internal sealed class CancelSupportDiagnosticSessionUnitOfWork(
    IGetSupportDiagnosticSessionUnitOfWork _reader,
    ISupportStore _supportStore,
    SupportRelayManagementClient _relayClient,
    TimeProvider _timeProvider) : ICancelSupportDiagnosticSessionUnitOfWork
{
  public async Task<SupportMutationStatus> CancelAsync(
      ClaimsPrincipal principal,
      string tenantId,
      Guid sessionId,
      CancellationToken cancellationToken)
  {
    var read = await _reader.GetAsync(principal, tenantId, sessionId, cancellationToken);
    if (read.Status != SupportMutationStatus.Succeeded)
    {
      return read.Status;
    }
    var relayStatus = await _relayClient.CancelSessionAsync(
        sessionId,
        cancellationToken);
    if (relayStatus == SupportRelayManagementStatus.Failed)
    {
      return SupportMutationStatus.Conflict;
    }
    return await _supportStore.CancelSessionAsync(
        tenantId,
        sessionId,
        _timeProvider.GetUtcNow(),
        cancellationToken);
  }
}
