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
      DeserializeEnvelopeOrNull(RequestEnvelope);

  private static SupportEnvelope? DeserializeEnvelopeOrNull(string value)
  {
    if (string.IsNullOrWhiteSpace(value) || value.Length > 1_048_576)
    {
      return null;
    }
    try
    {
      return System.Text.Json.JsonSerializer.Deserialize<SupportEnvelope>(value);
    }
    catch (System.Text.Json.JsonException)
    {
      return null;
    }
  }
}

internal sealed class SupportRelayTransportClient(
    IHttpClientFactory _httpClientFactory)
{
  public async Task<SupportRelayPollOutcome> PollAsync(
      SupportAgentOptions options,
      CancellationToken cancellationToken)
  {
    using var client = _httpClientFactory.CreateClient(SupportRelayTransportHttpClientOptions.ClientName);
    using var request = new HttpRequestMessage(
        HttpMethod.Get,
        new Uri(
            options.RelayUrl,
            $"/api/support-relay/v1/nodes/{options.NodeId:D}/poll"));
    request.Headers.Authorization = new AuthenticationHeaderValue(
        "Bearer",
        options.TransportCredential);
    using var response = await client.SendAsync(request, cancellationToken);
    if (response.StatusCode is
        System.Net.HttpStatusCode.Unauthorized or
        System.Net.HttpStatusCode.Forbidden)
    {
      return new SupportRelayPollOutcome(false, null);
    }
    if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
    {
      return new SupportRelayPollOutcome(true, null);
    }
    response.EnsureSuccessStatusCode();
    return new SupportRelayPollOutcome(
        true,
        await response.Content.ReadFromJsonAsync<AgentRelayPollResponse>(
            cancellationToken: cancellationToken));
  }

  public async Task<SupportRelayUploadOutcome> UploadResultAsync(
      SupportAgentOptions options,
      Guid sessionId,
      SupportEnvelope resultEnvelope,
      CancellationToken cancellationToken)
  {
    using var client = _httpClientFactory.CreateClient(SupportRelayTransportHttpClientOptions.ClientName);
    using var request = new HttpRequestMessage(
        HttpMethod.Post,
        new Uri(
            options.RelayUrl,
            $"/api/support-relay/v1/nodes/{options.NodeId:D}/sessions/{sessionId:D}/result"))
    {
      Content = JsonContent.Create(new
      {
        ResultEnvelope = System.Text.Json.JsonSerializer.Serialize(resultEnvelope),
      }),
    };
    request.Headers.Authorization = new AuthenticationHeaderValue(
        "Bearer",
        options.TransportCredential);
    using var response = await client.SendAsync(request, cancellationToken);
    return response.StatusCode switch
    {
      System.Net.HttpStatusCode.Unauthorized or
      System.Net.HttpStatusCode.Forbidden =>
          SupportRelayUploadOutcome.CredentialRejected,
      System.Net.HttpStatusCode.NoContent => SupportRelayUploadOutcome.Succeeded,
      _ => SupportRelayUploadOutcome.SessionRejected,
    };
  }
}
