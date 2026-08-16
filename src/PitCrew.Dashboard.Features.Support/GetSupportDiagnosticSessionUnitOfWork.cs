using System.Security.Claims;

using PitCrew.Dashboard.Features.Support.Abstractions;

namespace PitCrew.Dashboard.Features.Support;

internal sealed class GetSupportDiagnosticSessionUnitOfWork(
    SupportPrincipalAuthorizer _authorizer,
    PitCrew.Dashboard.Features.Access.IDiagnosticAccessScopeAccessor _diagnosticScopeAccessor,
    ISupportStore _supportStore,
    SupportRelayResultIngestor _resultIngestor,
    TimeProvider _timeProvider) : IGetSupportDiagnosticSessionUnitOfWork
{
  public async Task<SupportSessionMutation> GetAsync(
      ClaimsPrincipal principal,
      string tenantId,
      Guid sessionId,
      CancellationToken cancellationToken)
  {
    var session = await _supportStore.GetSessionOrNullAsync(tenantId, sessionId, cancellationToken);
    if (session is null)
    {
      return new SupportSessionMutation(SupportMutationStatus.NotFound, null, null);
    }
    var decision = await _authorizer.CanRequestOrReadAsync(
        principal,
        tenantId,
        session.NodeId,
        session.ProfileId,
        cancellationToken);
    if (!decision.Allowed)
    {
      return new SupportSessionMutation(SupportMutationStatus.Forbidden, null, null);
    }
    var identity = await _supportStore.GetIdentityOrNullAsync(
        tenantId,
        session.NodeId,
        cancellationToken);
    if (identity is not null &&
        (session.Status is SupportDiagnosticSessionStatus.Queued or
            SupportDiagnosticSessionStatus.Dispatched))
    {
      session = await _resultIngestor.IngestOrCurrentAsync(
          session,
          identity,
          cancellationToken);
    }
    session = WithCurrentLifecycle(session);
    return new SupportSessionMutation(SupportMutationStatus.Succeeded, null, session);
  }

  public async Task<IReadOnlyList<SupportDiagnosticSession>> GetRecentAsync(
      ClaimsPrincipal principal,
      string tenantId,
      CancellationToken cancellationToken)
  {
    var sessions = (await _supportStore.GetSessionsAsync(
            tenantId,
            50,
            cancellationToken))
        .Select(WithCurrentLifecycle)
        .ToArray();
    var scope = _diagnosticScopeAccessor.GetOrNull(principal);
    if (scope is null)
    {
      return sessions;
    }
    if (!string.Equals(scope.TenantId, tenantId, StringComparison.Ordinal))
    {
      return [];
    }
    return sessions
        .Where(session =>
            (scope.NodeIds.Count == 0 || scope.NodeIds.Contains(session.NodeId)) &&
            (scope.ProfileIds.Count == 0 ||
             session.ProfileId is not null &&
             scope.ProfileIds.Contains(session.ProfileId, StringComparer.Ordinal)))
        .ToArray();
  }

  private SupportDiagnosticSession WithCurrentLifecycle(
      SupportDiagnosticSession session) =>
      (session.Status is SupportDiagnosticSessionStatus.Queued or
          SupportDiagnosticSessionStatus.Dispatched) &&
      session.ExpiresAt <= _timeProvider.GetUtcNow()
          ? session with
          {
            Status = SupportDiagnosticSessionStatus.Expired,
          }
          : session;
}
