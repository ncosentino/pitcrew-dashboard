using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.Extensions.Options;

using PitCrew.Dashboard.Features.Support.Abstractions;

namespace PitCrew.Dashboard.Features.Support;

internal sealed class SupportRelayManagementClient(
    IHttpClientFactory _httpClientFactory,
    IOptions<SupportPlaneOptions> _options,
    TimeProvider _timeProvider)
{
  private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

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

  private bool IsConfigured =>
      !string.IsNullOrWhiteSpace(_options.Value.RelayInternalBearerSecret) &&
      Uri.TryCreate(_options.Value.RelayUrl, UriKind.Absolute, out _);

  private HttpClient CreateClient()
  {
    var client = _httpClientFactory.CreateClient(
        SupportRelayManagementHttpClientOptions.ClientName);
    client.BaseAddress = new Uri(_options.Value.RelayUrl, UriKind.Absolute);
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
        "Bearer",
        _options.Value.RelayInternalBearerSecret);
    client.DefaultRequestHeaders.Date = _timeProvider.GetUtcNow();
    return client;
  }
}

internal enum SupportRelayManagementStatus
{
  Succeeded,
  Skipped,
  Failed,
}
