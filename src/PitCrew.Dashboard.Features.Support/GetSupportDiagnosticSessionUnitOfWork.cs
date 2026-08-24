using System.Security.Claims;

using PitCrew.Dashboard.Features.Support.Abstractions;

namespace PitCrew.Dashboard.Features.Support;

internal sealed class GetSupportDiagnosticSessionUnitOfWork(
    SupportPrincipalAuthorizer _authorizer,
    PitCrew.Dashboard.Features.Access.IDiagnosticAccessScopeAccessor _diagnosticScopeAccessor,
    ISupportStore _supportStore,
    SupportRelayManagementClient _relayClient,
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
      session = await ProjectRelayLifecycleAsync(
          session,
          identity,
          cancellationToken);
    }
    if ((session.Status is
            SupportDiagnosticSessionStatus.Queued or
            SupportDiagnosticSessionStatus.Dispatched) &&
        session.ExpiresAt <= _timeProvider.GetUtcNow())
    {
      _ = await _supportStore.UpdateSessionLifecycleAsync(
          session.TenantId,
          session.SessionId,
          SupportDiagnosticSessionStatus.Expired,
          session.DispatchedAt,
          null,
          session.ExpiresAt,
          cancellationToken);
      session = await _supportStore.GetSessionOrNullAsync(
          session.TenantId,
          session.SessionId,
          cancellationToken) ?? session with
          {
            Status = SupportDiagnosticSessionStatus.Expired,
            CompletedAt = session.ExpiresAt,
          };
    }
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

  private async Task<SupportDiagnosticSession>
      ProjectRelayLifecycleAsync(
          SupportDiagnosticSession session,
          SupportIdentity identity,
          CancellationToken cancellationToken)
  {
    var state = await _relayClient.GetSessionStateOrNullAsync(
        session.SessionId,
        cancellationToken);
    if (state is null ||
        !string.Equals(
            state.TenantId,
            session.TenantId,
            StringComparison.Ordinal) ||
        state.NodeId != session.NodeId ||
        state.SessionId != session.SessionId ||
        state.ExpiresAt != session.ExpiresAt)
    {
      return session;
    }
    if (state.DispatchedAt is not null &&
        session.DispatchedAt is null)
    {
      _ = await _supportStore.UpdateSessionLifecycleAsync(
          session.TenantId,
          session.SessionId,
          SupportDiagnosticSessionStatus.Dispatched,
          state.DispatchedAt,
          null,
          state.DispatchedAt.Value,
          cancellationToken);
      session = await _supportStore.GetSessionOrNullAsync(
          session.TenantId,
          session.SessionId,
          cancellationToken) ?? session with
          {
            Status = SupportDiagnosticSessionStatus.Dispatched,
            DispatchedAt = state.DispatchedAt,
          };
    }
    if (state.Status == SupportDiagnosticSessionStatus.Completed)
    {
      return await _resultIngestor.IngestOrCurrentAsync(
          session,
          identity,
          cancellationToken);
    }
    if (state.Status is not (
            SupportDiagnosticSessionStatus.Dispatched or
            SupportDiagnosticSessionStatus.Rejected or
            SupportDiagnosticSessionStatus.Cancelled or
            SupportDiagnosticSessionStatus.Expired))
    {
      return session;
    }
    var transitionedAt = state.Status switch
    {
      SupportDiagnosticSessionStatus.Rejected =>
          state.RejectedAt ?? _timeProvider.GetUtcNow(),
      SupportDiagnosticSessionStatus.Expired =>
          state.ExpiresAt,
      _ => _timeProvider.GetUtcNow(),
    };
    _ = await _supportStore.UpdateSessionLifecycleAsync(
        session.TenantId,
        session.SessionId,
        state.Status,
        state.DispatchedAt,
        state.RejectionDisposition,
        transitionedAt,
        cancellationToken);
    return await _supportStore.GetSessionOrNullAsync(
        session.TenantId,
        session.SessionId,
        cancellationToken) ?? session with
        {
          Status = state.Status,
          DispatchedAt =
              session.DispatchedAt ?? state.DispatchedAt,
          RejectionDisposition =
              state.RejectionDisposition,
          CompletedAt = transitionedAt,
        };
  }
}
