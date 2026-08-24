using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using PitCrew.Support.Protocol;

namespace PitCrew.Support.Agent.App;

internal sealed record AgentRelayPollResponse(
    Guid SessionId,
    string RequestEnvelope,
    DateTimeOffset ExpiresAt)
{
  private static readonly JsonSerializerOptions _jsonOptions =
      new(JsonSerializerDefaults.Web);

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
      return JsonSerializer.Deserialize<SupportEnvelope>(
          value,
          _jsonOptions);
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
  private static readonly JsonSerializerOptions _jsonOptions =
      new(JsonSerializerDefaults.Web);

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
            _jsonOptions,
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
      Content = JsonContent.Create(
          new
          {
            ResultEnvelope = JsonSerializer.Serialize(
                resultEnvelope,
                _jsonOptions),
          },
          options: _jsonOptions),
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

  public async Task<SupportRelayOutcomeReportStatus>
      ReportRejectionAsync(
          SupportAgentOptions options,
          Guid sessionId,
          string disposition,
          CancellationToken cancellationToken)
  {
    using var client = _httpClientFactory.CreateClient(
        SupportRelayTransportHttpClientOptions.ClientName);
    using var request = new HttpRequestMessage(
        HttpMethod.Post,
        new Uri(
            options.RelayUrl,
            $"/api/support-relay/v1/nodes/{options.NodeId:D}/sessions/{sessionId:D}/outcome"))
    {
      Content = JsonContent.Create(
          new SupportRelayRequestOutcomeRequest(disposition),
          options: _jsonOptions),
    };
    request.Headers.Authorization = new AuthenticationHeaderValue(
        "Bearer",
        options.TransportCredential);
    using var response = await client.SendAsync(
        request,
        cancellationToken);
    if (response.StatusCode is
        System.Net.HttpStatusCode.Unauthorized or
        System.Net.HttpStatusCode.Forbidden)
    {
      return SupportRelayOutcomeReportStatus
          .CredentialRejected;
    }
    if (response.StatusCode ==
        System.Net.HttpStatusCode.NoContent)
    {
      return SupportRelayOutcomeReportStatus.Succeeded;
    }
    if (response.StatusCode is
        System.Net.HttpStatusCode.NotFound or
        System.Net.HttpStatusCode.Conflict)
    {
      return SupportRelayOutcomeReportStatus
          .SessionUnavailable;
    }
    response.EnsureSuccessStatusCode();
    throw new InvalidOperationException(
        "A successful rejection outcome response had an unsupported status.");
  }
}
