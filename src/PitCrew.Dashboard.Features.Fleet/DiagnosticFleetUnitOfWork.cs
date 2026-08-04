using System.Security.Claims;

using Microsoft.Extensions.Options;

using PitCrew.Dashboard.Features.Access;
using PitCrew.Dashboard.Features.Access.Abstractions;
using PitCrew.Dashboard.Features.Fleet.Abstractions;

namespace PitCrew.Dashboard.Features.Fleet;

internal enum DiagnosticQueryStatus
{
  Succeeded,
  Invalid,
  Forbidden,
  NotFound,
}

internal sealed record DiagnosticFleetQueryInput(
    string? AfterNodeId,
    string? Limit);

internal sealed record DiagnosticFleetQueryResult(
    DiagnosticQueryStatus Status,
    string? Error,
    DiagnosticFleetPageResponse? Fleet,
    NodeHistoryResponse? History);

internal interface IGetDiagnosticFleetUnitOfWork
{
  Task<DiagnosticFleetQueryResult> GetFleetAsync(
      ClaimsPrincipal principal,
      string tenantId,
      DiagnosticFleetQueryInput input,
      CancellationToken cancellationToken);

  Task<DiagnosticFleetQueryResult> GetNodeHistoryAsync(
      ClaimsPrincipal principal,
      string tenantId,
      Guid nodeId,
      HistoryQueryInput input,
      CancellationToken cancellationToken);

  Task<DiagnosticFleetQueryResult> GetProfileHistoryAsync(
      ClaimsPrincipal principal,
      string tenantId,
      Guid nodeId,
      string profileId,
      HistoryQueryInput input,
      CancellationToken cancellationToken);

  HistoryCapabilities GetCapabilities();
}

internal sealed class GetDiagnosticFleetUnitOfWork(
    IDiagnosticAccessScopeAccessor _scopeAccessor,
    IFleetStore _fleetStore,
    IGetFleetHistoryUnitOfWork _historyUnitOfWork,
    IOptions<FleetDashboardOptions> _options,
    TimeProvider _timeProvider) :
    IGetDiagnosticFleetUnitOfWork
{
  private const int DefaultPageSize = 50;
  private const int MaximumPageSize = 100;

  public async Task<DiagnosticFleetQueryResult> GetFleetAsync(
      ClaimsPrincipal principal,
      string tenantId,
      DiagnosticFleetQueryInput input,
      CancellationToken cancellationToken)
  {
    var scope = GetScopeOrNull(principal, tenantId);
    if (scope is null)
    {
      return Forbidden();
    }
    var limit = ParseLimitOrNull(input.Limit);
    if (limit is null)
    {
      return Invalid(
          $"The 'limit' value must be between 1 and {MaximumPageSize}.");
    }
    Guid? afterNodeId = null;
    if (!string.IsNullOrWhiteSpace(input.AfterNodeId))
    {
      if (!Guid.TryParse(
          input.AfterNodeId,
          System.Globalization.CultureInfo.InvariantCulture,
          out var parsed))
      {
        return Invalid(
            "The 'afterNodeId' cursor must be a GUID.");
      }
      afterNodeId = parsed;
    }

    var fleet = await _fleetStore.GetFleetAsync(
        tenantId,
        _timeProvider.GetUtcNow(),
        TimeSpan.FromSeconds(
            _options.Value.NodeOfflineAfterSeconds),
        cancellationToken);
    var allowedNodes = scope.NodeIds.Count == 0
        ? null
        : scope.NodeIds.ToHashSet();
    var allowedProfiles = CreateProfileRestrictionSet(
        scope.ProfileIds);
    var nodes = fleet.Nodes
        .Where(node =>
            allowedNodes is null ||
            allowedNodes.Contains(node.NodeId))
        .Where(node =>
            afterNodeId is null ||
            node.NodeId.CompareTo(afterNodeId.Value) > 0)
        .OrderBy(node => node.NodeId)
        .Select(node => node with
        {
          Profiles = node.Profiles
              .Where(profile =>
                  allowedProfiles is null ||
                  allowedProfiles.Contains(profile.ProfileId))
              .Select(profile => allowedProfiles is null
                  ? profile
                  : profile with { Host = null })
              .ToArray(),
          Hardware = allowedProfiles is null
              ? node.Hardware
              : null,
          CapacityControls = [],
          RecoveryControls = [],
        })
        .Where(node =>
            allowedProfiles is null ||
            node.Profiles.Count > 0)
        .Take(limit.Value + 1)
        .ToArray();
    var hasMore = nodes.Length > limit.Value;
    var page = hasMore
        ? nodes[..limit.Value]
        : nodes;
    return new DiagnosticFleetQueryResult(
        DiagnosticQueryStatus.Succeeded,
        null,
        new DiagnosticFleetPageResponse(
            fleet.GeneratedAt,
            page,
            hasMore ? page[^1].NodeId : null),
        null);
  }

  public async Task<DiagnosticFleetQueryResult> GetNodeHistoryAsync(
      ClaimsPrincipal principal,
      string tenantId,
      Guid nodeId,
      HistoryQueryInput input,
      CancellationToken cancellationToken)
  {
    var scope = GetScopeOrNull(principal, tenantId);
    if (scope is null ||
        !AllowsNode(scope, nodeId) ||
        scope.ProfileIds.Count > 0)
    {
      return Forbidden();
    }
    var result = await _historyUnitOfWork.GetNodeHistoryAsync(
        tenantId,
        nodeId,
        input,
        cancellationToken);
    if (result.Status != HistoryQueryStatus.Succeeded ||
        result.Response is null)
    {
      return FromHistoryResult(result);
    }
    return new DiagnosticFleetQueryResult(
        DiagnosticQueryStatus.Succeeded,
        null,
        null,
        result.Response with
        {
          Profiles = result.Response.Profiles,
        });
  }

  public async Task<DiagnosticFleetQueryResult> GetProfileHistoryAsync(
      ClaimsPrincipal principal,
      string tenantId,
      Guid nodeId,
      string profileId,
      HistoryQueryInput input,
      CancellationToken cancellationToken)
  {
    var scope = GetScopeOrNull(principal, tenantId);
    if (scope is null ||
        !AllowsNode(scope, nodeId) ||
        !AllowsProfile(scope, profileId))
    {
      return Forbidden();
    }
    var result = await _historyUnitOfWork.GetProfileHistoryAsync(
        tenantId,
        nodeId,
        profileId,
        input,
        cancellationToken);
    if (result.Response is not null)
    {
      result = result with
      {
        Response = result.Response with
        {
          HardwareRevisions = [],
          HardwareRevisionsTruncated = false,
        },
      };
    }
    return FromHistoryResult(result);
  }

  public HistoryCapabilities GetCapabilities() =>
      _historyUnitOfWork.GetCapabilities();

  private DiagnosticAccessScope? GetScopeOrNull(
      ClaimsPrincipal principal,
      string tenantId)
  {
    var scope = _scopeAccessor.GetOrNull(principal);
    return scope is not null &&
        string.Equals(
            scope.TenantId,
            tenantId,
            StringComparison.Ordinal)
        ? scope
        : null;
  }

  private static bool AllowsNode(
      DiagnosticAccessScope scope,
      Guid nodeId) =>
      scope.NodeIds.Count == 0 ||
      scope.NodeIds.Contains(nodeId);

  private static bool AllowsProfile(
      DiagnosticAccessScope scope,
      string profileId) =>
      scope.ProfileIds.Count == 0 ||
      scope.ProfileIds.Contains(
          profileId,
          StringComparer.Ordinal);

  private static HashSet<string>? CreateProfileRestrictionSet(
      IReadOnlyList<string> profileIds)
  {
    if (profileIds.Count == 0)
    {
      return null;
    }
    // Profile IDs are schema-canonical lowercase identifiers. Ordinal matching
    // rejects malformed case instead of broadening a credential restriction.
#pragma warning disable NLF0016
    return new HashSet<string>(
        profileIds,
        StringComparer.Ordinal);
#pragma warning restore NLF0016
  }

  private static int? ParseLimitOrNull(string? value)
  {
    if (string.IsNullOrWhiteSpace(value))
    {
      return DefaultPageSize;
    }
    return int.TryParse(
        value,
        System.Globalization.NumberStyles.None,
        System.Globalization.CultureInfo.InvariantCulture,
        out var parsed) &&
        parsed is >= 1 and <= MaximumPageSize
        ? parsed
        : null;
  }

  private static DiagnosticFleetQueryResult FromHistoryResult(
      HistoryQueryResult result) =>
      result.Status switch
      {
        HistoryQueryStatus.Succeeded =>
            new DiagnosticFleetQueryResult(
                DiagnosticQueryStatus.Succeeded,
                null,
                null,
                result.Response),
        HistoryQueryStatus.Invalid =>
            Invalid(result.Error ??
                "The history query is invalid."),
        HistoryQueryStatus.NotFound =>
            new DiagnosticFleetQueryResult(
                DiagnosticQueryStatus.NotFound,
                null,
                null,
                null),
        _ => new DiagnosticFleetQueryResult(
            DiagnosticQueryStatus.NotFound,
            null,
            null,
            null),
      };

  private static DiagnosticFleetQueryResult Invalid(string error) =>
      new(
          DiagnosticQueryStatus.Invalid,
          error,
          null,
          null);

  private static DiagnosticFleetQueryResult Forbidden() =>
      new(
          DiagnosticQueryStatus.Forbidden,
          null,
          null,
          null);
}
