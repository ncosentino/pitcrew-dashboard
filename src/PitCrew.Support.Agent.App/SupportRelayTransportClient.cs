using System.Net.Http.Headers;
using System.Net.Http.Json;

using PitCrew.Support.Protocol;

namespace PitCrew.Support.Agent.App;

internal sealed record AgentRelayPollResponse(
    Guid SessionId,
    string RequestEnvelope,
    DateTimeOffset ExpiresAt)
{
  public SupportEnvelope? GetRequestEnvelopeOrNull() =>
      System.Text.Json.JsonSerializer.Deserialize<SupportEnvelope>(RequestEnvelope);
}

internal sealed class SupportRelayTransportClient(
    IHttpClientFactory _httpClientFactory,
    string _transportCredential)
{
  public async Task<AgentRelayPollResponse?> PollAsync(
      Guid nodeId,
      CancellationToken cancellationToken)
  {
    using var client = _httpClientFactory.CreateClient(SupportRelayTransportHttpClientOptions.ClientName);
    using var request = new HttpRequestMessage(
        HttpMethod.Get,
        $"/api/support-relay/v1/nodes/{nodeId:D}/poll");
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _transportCredential);
    using var response = await client.SendAsync(request, cancellationToken);
    if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
    {
      return null;
    }
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<AgentRelayPollResponse>(
        cancellationToken: cancellationToken);
  }

  public async Task<bool> UploadResultAsync(
      Guid nodeId,
      Guid sessionId,
      SupportEnvelope resultEnvelope,
      CancellationToken cancellationToken)
  {
    using var client = _httpClientFactory.CreateClient(SupportRelayTransportHttpClientOptions.ClientName);
    using var request = new HttpRequestMessage(
        HttpMethod.Post,
        $"/api/support-relay/v1/nodes/{nodeId:D}/sessions/{sessionId:D}/result")
    {
      Content = JsonContent.Create(new
      {
        ResultEnvelope = System.Text.Json.JsonSerializer.Serialize(resultEnvelope),
      }),
    };
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _transportCredential);
    using var response = await client.SendAsync(request, cancellationToken);
    return response.IsSuccessStatusCode;
  }
}
