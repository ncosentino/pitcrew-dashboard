using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using PitCrew.Dashboard.Features.Support.Abstractions;
using PitCrew.Support.Protocol;

namespace PitCrew.Dashboard.Features.Support;

internal sealed class SupportRelayManagementClient(
    IHttpClientFactory _httpClientFactory,
    IOptions<SupportPlaneOptions> _options,
    TimeProvider _timeProvider,
    ILogger<SupportRelayManagementClient> _logger)
{
  private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
  private static readonly TimeSpan _maximumActivityClockSkew = TimeSpan.FromMinutes(5);
  private const int MaxNodeActivityBatchSize = 256;

  public async Task<SupportRelayManagementStatus> RegisterNodeAsync(
      SupportIdentityWrite write,
      CancellationToken cancellationToken)
  {
    if (!IsConfigured)
    {
      return SupportRelayManagementStatus.Skipped;
    }
    using var client = CreateClient();
    using var response = await client.PostAsJsonAsync(
        "/internal/support/v1/nodes",
        new
        {
          write.Identity.TenantId,
          write.Identity.NodeId,
          write.TransportCredentialHash,
        },
        _jsonOptions,
        cancellationToken);
    return response.IsSuccessStatusCode
        ? SupportRelayManagementStatus.Succeeded
        : SupportRelayManagementStatus.Failed;
  }

  public async Task<SupportRelayManagementStatus> RevokeNodeAsync(
      Guid nodeId,
      CancellationToken cancellationToken)
  {
    if (!IsConfigured)
    {
      return SupportRelayManagementStatus.Skipped;
    }
    using var client = CreateClient();
    using var response = await client.PostAsync(
        $"/internal/support/v1/nodes/{nodeId:D}/revoke",
        null,
        cancellationToken);
    return response.IsSuccessStatusCode ||
        response.StatusCode == HttpStatusCode.NotFound
        ? SupportRelayManagementStatus.Succeeded
        : SupportRelayManagementStatus.Failed;
  }

  public async Task<SupportRelayManagementStatus> PrepareNodeCredentialAsync(
      SupportIdentityRotation rotation,
      CancellationToken cancellationToken)
  {
    if (!IsConfigured)
    {
      return SupportRelayManagementStatus.Skipped;
    }
    using var client = CreateClient();
    using var response = await client.PostAsJsonAsync(
        $"/internal/support/v1/nodes/{rotation.NodeId:D}/prepare-credential",
        new
        {
          rotation.RotationId,
          rotation.TenantId,
          rotation.ExpectedTransportCredentialHash,
          rotation.ReplacementTransportCredentialHash,
        },
        _jsonOptions,
        cancellationToken);
    return response.IsSuccessStatusCode
        ? SupportRelayManagementStatus.Succeeded
        : SupportRelayManagementStatus.Failed;
  }

  public async Task<SupportRelayManagementStatus> PromoteNodeCredentialAsync(
      SupportIdentityRotation rotation,
      CancellationToken cancellationToken)
  {
    if (!IsConfigured)
    {
      return SupportRelayManagementStatus.Skipped;
    }
    using var client = CreateClient();
    using var response = await client.PostAsJsonAsync(
        $"/internal/support/v1/nodes/{rotation.NodeId:D}/promote-credential",
        new
        {
          rotation.RotationId,
          rotation.TenantId,
          rotation.ExpectedTransportCredentialHash,
          rotation.ReplacementTransportCredentialHash,
        },
        _jsonOptions,
        cancellationToken);
    return response.IsSuccessStatusCode
        ? SupportRelayManagementStatus.Succeeded
        : SupportRelayManagementStatus.Failed;
  }

  public async Task<SupportRelayManagementStatus> EnqueueSessionAsync(
      SupportDiagnosticSession session,
      CancellationToken cancellationToken)
  {
    if (!IsConfigured)
    {
      return SupportRelayManagementStatus.Skipped;
    }
    using var client = CreateClient();
    using var response = await client.PostAsJsonAsync(
        "/internal/support/v1/sessions",
        new
        {
          session.TenantId,
          session.NodeId,
          session.SessionId,
          session.ExpiresAt,
          RequestEnvelope = JsonSerializer.Serialize(session.RequestEnvelope, _jsonOptions),
        },
        _jsonOptions,
        cancellationToken);
    return response.IsSuccessStatusCode
        ? SupportRelayManagementStatus.Succeeded
        : SupportRelayManagementStatus.Failed;
  }

  public async Task<SupportRelayManagementStatus> CancelSessionAsync(
      Guid sessionId,
      CancellationToken cancellationToken)
  {
    if (!IsConfigured)
    {
      return SupportRelayManagementStatus.Skipped;
    }
    using var client = CreateClient();
    using var response = await client.PostAsync(
        $"/internal/support/v1/sessions/{sessionId:D}/cancel",
        null,
        cancellationToken);
    return response.IsSuccessStatusCode
        ? SupportRelayManagementStatus.Succeeded
        : SupportRelayManagementStatus.Failed;
  }

  public async Task<string?> FetchResultOrNullAsync(
      Guid sessionId,
      CancellationToken cancellationToken)
  {
    if (!IsConfigured)
    {
      return null;
    }
    using var client = CreateClient();
    using var response = await client.GetAsync(
        $"/internal/support/v1/sessions/{sessionId:D}/result",
        cancellationToken);
    return response.IsSuccessStatusCode
        ? await response.Content.ReadAsStringAsync(cancellationToken)
        : null;
  }

  public async Task<SupportRelaySessionState?>
      GetSessionStateOrNullAsync(
          Guid sessionId,
          CancellationToken cancellationToken)
  {
    if (!IsConfigured)
    {
      return null;
    }
    try
    {
      return await GetSessionStateCoreAsync(
          sessionId,
          cancellationToken);
    }
    catch (HttpRequestException)
    {
      SupportRelayLifecycleLog.RefreshFailed(
          _logger,
          nameof(HttpRequestException));
      return null;
    }
    catch (TaskCanceledException)
        when (!cancellationToken.IsCancellationRequested)
    {
      SupportRelayLifecycleLog.RefreshFailed(
          _logger,
          nameof(TaskCanceledException));
      return null;
    }
    catch (JsonException)
    {
      SupportRelayLifecycleLog.RefreshFailed(
          _logger,
          nameof(JsonException));
      return null;
    }
  }

  private async Task<SupportRelaySessionState?>
      GetSessionStateCoreAsync(
          Guid sessionId,
          CancellationToken cancellationToken)
  {
    using var client = CreateClient();
    using var response = await client.GetAsync(
        $"/internal/support/v1/sessions/{sessionId:D}",
        cancellationToken);
    if (!response.IsSuccessStatusCode)
    {
      SupportRelayLifecycleLog.NonSuccessStatus(
          _logger,
          (int)response.StatusCode);
      return null;
    }
    var state = await response.Content
        .ReadFromJsonAsync<RelaySessionStateDto>(
            _jsonOptions,
            cancellationToken);
    var latestActivityAt = _timeProvider
        .GetUtcNow()
        .Add(_maximumActivityClockSkew);
    if (state is null ||
        string.IsNullOrWhiteSpace(state.TenantId) ||
        state.TenantId.Length > 200 ||
        state.NodeId == Guid.Empty ||
        state.SessionId != sessionId ||
        !Enum.TryParse<SupportDiagnosticSessionStatus>(
            state.Status,
            ignoreCase: true,
            out var status) ||
        !Enum.IsDefined(status) ||
        (status == SupportDiagnosticSessionStatus.Rejected) !=
            (state.RejectionDisposition is not null) ||
        (status == SupportDiagnosticSessionStatus.Rejected) !=
            (state.RejectedAt is not null) ||
        status == SupportDiagnosticSessionStatus.Queued &&
            (state.DispatchedAt is not null ||
             state.RejectedAt is not null) ||
        status == SupportDiagnosticSessionStatus.Rejected &&
            state.DispatchedAt is null ||
        state.RejectionDisposition is not null &&
        !SupportRequestRejectionDispositions.IsSupported(
            state.RejectionDisposition) ||
        !IsValidActivityTimestamp(
            state.DispatchedAt,
            latestActivityAt) ||
        !IsValidActivityTimestamp(
            state.RejectedAt,
            latestActivityAt))
    {
      return null;
    }
    return new SupportRelaySessionState(
        state.TenantId,
        state.NodeId,
        state.SessionId,
        status,
        state.ExpiresAt.ToUniversalTime(),
        state.DispatchedAt?.ToUniversalTime(),
        state.RejectedAt?.ToUniversalTime(),
        state.RejectionDisposition);
  }

  public async Task<IReadOnlyList<SupportIdentityActivity>?> GetNodeActivityAsync(
      string tenantId,
      IReadOnlyList<Guid> nodeIds,
      CancellationToken cancellationToken)
  {
    if (!IsConfigured)
    {
      return null;
    }
    if (nodeIds.Count == 0)
    {
      return [];
    }
    if (nodeIds.Count > MaxNodeActivityBatchSize)
    {
      SupportRelayActivityLog.BatchTooLarge(
          _logger,
          MaxNodeActivityBatchSize);
      return null;
    }
    try
    {
      using var client = CreateClient();
      using var response = await client.PostAsJsonAsync(
          "/internal/support/v1/nodes/activity",
          new RelayNodeActivityRequest(tenantId, nodeIds),
          _jsonOptions,
          cancellationToken);
      if (!response.IsSuccessStatusCode)
      {
        SupportRelayActivityLog.NonSuccessStatus(
            _logger,
            (int)response.StatusCode);
        return null;
      }
      var activity =
          await response.Content.ReadFromJsonAsync<List<SupportIdentityActivity>>(
              _jsonOptions,
              cancellationToken);
      var latestActivityAt = _timeProvider
          .GetUtcNow()
          .Add(_maximumActivityClockSkew);
      if (activity is null ||
          activity.Count > nodeIds.Count ||
          activity.Select(item => item.NodeId).Distinct().Count() !=
              activity.Count ||
          activity.Any(item => !nodeIds.Contains(item.NodeId)) ||
          activity.Any(item =>
              !IsValidActivityTimestamp(
                  item.LastPollAt,
                  latestActivityAt) ||
              !IsValidActivityTimestamp(
                  item.LastResultAt,
                  latestActivityAt)))
      {
        SupportRelayActivityLog.InvalidProjection(_logger);
        return null;
      }
      return activity
          .Select(item => item with
          {
            LastPollAt = item.LastPollAt?.ToUniversalTime(),
            LastResultAt = item.LastResultAt?.ToUniversalTime(),
          })
          .ToArray();
    }
    catch (HttpRequestException)
    {
      SupportRelayActivityLog.RefreshFailed(
          _logger,
          nameof(HttpRequestException));
      return null;
    }
    catch (TaskCanceledException)
        when (!cancellationToken.IsCancellationRequested)
    {
      SupportRelayActivityLog.RefreshFailed(
          _logger,
          nameof(TaskCanceledException));
      return null;
    }
    catch (JsonException)
    {
      SupportRelayActivityLog.RefreshFailed(
          _logger,
          nameof(JsonException));
      return null;
    }
  }

  private static bool IsValidActivityTimestamp(
      DateTimeOffset? timestamp,
      DateTimeOffset latestActivityAt) =>
      timestamp is null ||
      timestamp >= DateTimeOffset.UnixEpoch &&
      timestamp <= latestActivityAt;

  internal bool IsConfigured
  {
    get
    {
      return _options.Value.RelayInternalBearerSecret.Length is >= 16 and <= 4096 &&
          !_options.Value.RelayInternalBearerSecret.Contains('\r') &&
          !_options.Value.RelayInternalBearerSecret.Contains('\n') &&
          Uri.TryCreate(
              _options.Value.RelayUrl,
              UriKind.Absolute,
              out var relayUrl) &&
          IsAllowedRelayOrigin(relayUrl) &&
          GetManagementOriginOrNull() is not null;
    }
  }

  private HttpClient CreateClient()
  {
    var client = _httpClientFactory.CreateClient(
        SupportRelayManagementHttpClientOptions.ClientName);
    client.BaseAddress = GetManagementOriginOrNull() ??
        throw new InvalidOperationException(
            "The support relay management origin is invalid.");
    client.MaxResponseContentBufferSize = 4_194_304;
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
        "Bearer",
        _options.Value.RelayInternalBearerSecret);
    client.DefaultRequestHeaders.Date = _timeProvider.GetUtcNow();
    return client;
  }

  private Uri? GetManagementOriginOrNull()
  {
    var value = string.IsNullOrWhiteSpace(_options.Value.RelayInternalUrl)
        ? _options.Value.RelayUrl
        : _options.Value.RelayInternalUrl;
    return Uri.TryCreate(value, UriKind.Absolute, out var relayUrl) &&
        IsAllowedManagementOrigin(relayUrl)
            ? relayUrl
            : null;
  }

  private static bool IsAllowedManagementOrigin(Uri relayUrl) =>
      IsAllowedRelayOrigin(relayUrl) ||
      IsOriginOnly(relayUrl) &&
      relayUrl.Scheme == Uri.UriSchemeHttp &&
      relayUrl.HostNameType == UriHostNameType.Dns &&
      !relayUrl.Host.Contains('.', StringComparison.Ordinal);

  private static bool IsAllowedRelayOrigin(Uri relayUrl) =>
      IsOriginOnly(relayUrl) &&
      (relayUrl.Scheme == Uri.UriSchemeHttps ||
       relayUrl.Scheme == Uri.UriSchemeHttp && relayUrl.IsLoopback);

  private static bool IsOriginOnly(Uri relayUrl) =>
      string.IsNullOrEmpty(relayUrl.UserInfo) &&
      string.IsNullOrEmpty(relayUrl.Query) &&
      string.IsNullOrEmpty(relayUrl.Fragment) &&
      relayUrl.AbsolutePath == "/";

  private sealed record RelayNodeActivityRequest(
      string TenantId,
      IReadOnlyList<Guid> NodeIds);

  private sealed record RelaySessionStateDto(
      string TenantId,
      Guid NodeId,
      Guid SessionId,
      string Status,
      DateTimeOffset ExpiresAt,
      DateTimeOffset? DispatchedAt,
      DateTimeOffset? RejectedAt,
      string? RejectionDisposition);
}

internal enum SupportRelayManagementStatus
{
  Succeeded,
  Skipped,
  Failed,
}
