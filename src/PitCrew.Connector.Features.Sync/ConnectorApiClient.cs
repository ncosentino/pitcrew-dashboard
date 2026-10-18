using System.Net.Http.Headers;
using System.Net.Http.Json;

using Microsoft.Extensions.Options;

using PitCrew.Protocol;

namespace PitCrew.Connector.Features.Sync;

internal sealed class ConnectorApiClient(
    IHttpClientFactory _httpClientFactory,
    IOptions<ConnectorOptions> _options)
{
  public async Task<ConnectorEnrollmentResponse> EnrollAsync(
      ConnectorEnrollmentRequest request,
      CancellationToken cancellationToken)
  {
    using var client = _httpClientFactory.CreateClient("PitCrewConnector");
    using var message = new HttpRequestMessage(
        HttpMethod.Post,
        new Uri(new Uri(_options.Value.DashboardUrl), "api/connectors/v1/enroll"))
    {
      Content = JsonContent.Create(
            request,
            PitCrewProtocolJsonContext.Default.ConnectorEnrollmentRequest),
    };
    message.Headers.Add(
        "X-PitCrew-Enrollment-Code",
        _options.Value.EnrollmentCode);
    using var response = await client.SendAsync(
        message,
        HttpCompletionOption.ResponseHeadersRead,
        cancellationToken);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync(
        PitCrewProtocolJsonContext.Default.ConnectorEnrollmentResponse,
        cancellationToken) ??
        throw new InvalidOperationException(
            "Dashboard enrollment response was empty.");
  }

  public async Task<ConnectorSyncAttempt> SyncAsync(
      string credential,
      ConnectorSyncRequest request,
      CancellationToken cancellationToken)
  {
    using var client = _httpClientFactory.CreateClient("PitCrewConnector");
    using var message = new HttpRequestMessage(
        HttpMethod.Post,
        new Uri(new Uri(_options.Value.DashboardUrl), "api/connectors/v1/sync"))
    {
      Content = JsonContent.Create(
            request,
            PitCrewProtocolJsonContext.Default.ConnectorSyncRequest),
    };
    message.Headers.Authorization = new AuthenticationHeaderValue(
        "Bearer",
        credential);
    using var response = await client.SendAsync(
        message,
        HttpCompletionOption.ResponseHeadersRead,
        cancellationToken);
    if (!response.IsSuccessStatusCode)
    {
      var reason = ConnectorSyncRejectionReason.Unspecified;
      if (response.Headers.TryGetValues("X-PitCrew-Sync-Rejection", out var values))
      {
        var value = values.Take(2).ToArray();
        if (value.Length == 1 &&
            value[0].Length <= 64 &&
            Enum.TryParse<ConnectorSyncRejectionReason>(value[0], out var parsed) &&
            Enum.IsDefined(parsed) &&
            string.Equals(parsed.ToString(), value[0], StringComparison.Ordinal))
        {
          reason = parsed;
        }
      }
      return new ConnectorSyncAttempt(response.StatusCode, null, reason);
    }
    var result = await response.Content.ReadFromJsonAsync(
        PitCrewProtocolJsonContext.Default.ConnectorSyncResponse,
        cancellationToken) ??
        throw new InvalidOperationException(
            "Dashboard synchronization response was empty.");
    return new ConnectorSyncAttempt(response.StatusCode, result);
  }
}
