using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using Microsoft.Extensions.Options;

using NexusLabs.Needlr;

using PitCrew.Protocol;

namespace PitCrew.Connector.Features.Sync;

[DoNotAutoRegister]
internal sealed class ConnectorApiClient(
    HttpClient _httpClient,
    IOptions<ConnectorOptions> _options)
{
  public async Task<ConnectorEnrollmentResponse> EnrollAsync(
      ConnectorEnrollmentRequest request,
      CancellationToken cancellationToken)
  {
    using var message = new HttpRequestMessage(
        HttpMethod.Post,
        "api/connectors/v1/enroll")
    {
      Content = JsonContent.Create(
            request,
            PitCrewProtocolJsonContext.Default.ConnectorEnrollmentRequest),
    };
    message.Headers.Add(
        "X-PitCrew-Enrollment-Code",
        _options.Value.EnrollmentCode);
    using var response = await _httpClient.SendAsync(
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

  public async Task<ConnectorSyncResponse> SyncAsync(
      string credential,
      ConnectorSyncRequest request,
      CancellationToken cancellationToken)
  {
    using var message = new HttpRequestMessage(
        HttpMethod.Post,
        "api/connectors/v1/sync")
    {
      Content = JsonContent.Create(
            request,
            PitCrewProtocolJsonContext.Default.ConnectorSyncRequest),
    };
    message.Headers.Authorization = new AuthenticationHeaderValue(
        "Bearer",
        credential);
    using var response = await _httpClient.SendAsync(
        message,
        HttpCompletionOption.ResponseHeadersRead,
        cancellationToken);
    if (response.StatusCode == HttpStatusCode.Unauthorized)
    {
      throw new HttpRequestException(
          "The dashboard rejected the connector credential.",
          null,
          HttpStatusCode.Unauthorized);
    }
    if ((int)response.StatusCode is >= 400 and < 500 &&
        response.StatusCode != HttpStatusCode.TooManyRequests)
    {
      throw new HttpRequestException(
          $"Dashboard rejected the synchronization payload with status {(int)response.StatusCode}.",
          null,
          response.StatusCode);
    }
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync(
        PitCrewProtocolJsonContext.Default.ConnectorSyncResponse,
        cancellationToken) ??
        throw new InvalidOperationException(
            "Dashboard synchronization response was empty.");
  }
}
