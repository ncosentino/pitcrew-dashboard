using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace PitCrew.Dashboard.WebApi.Tests;

internal sealed class SupportSessionRelayHandler :
    HttpMessageHandler
{
  private static readonly JsonSerializerOptions _jsonOptions =
      new(JsonSerializerDefaults.Web);
  private string? _tenantId;
  private Guid _nodeId;
  private Guid _sessionId;
  private DateTimeOffset _expiresAt;

  public string Status { get; set; } = "queued";

  public DateTimeOffset? DispatchedAt { get; set; }

  public DateTimeOffset? RejectedAt { get; set; }

  public string? RejectionDisposition { get; set; }

  protected override async Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request,
      CancellationToken cancellationToken)
  {
    var path = request.RequestUri?.AbsolutePath ?? string.Empty;
    if (request.Method == HttpMethod.Post &&
        path == "/internal/support/v1/nodes")
    {
      return new HttpResponseMessage(HttpStatusCode.NoContent);
    }
    if (request.Method == HttpMethod.Post &&
        path == "/internal/support/v1/sessions")
    {
      using var payload = JsonDocument.Parse(
          await request.Content!.ReadAsStringAsync(
              cancellationToken));
      var root = payload.RootElement;
      _tenantId = root.GetProperty("tenantId").GetString();
      _nodeId = root.GetProperty("nodeId").GetGuid();
      _sessionId = root.GetProperty("sessionId").GetGuid();
      _expiresAt = root.GetProperty("expiresAt")
          .GetDateTimeOffset();
      return new HttpResponseMessage(HttpStatusCode.Accepted);
    }
    if (request.Method == HttpMethod.Get &&
        path ==
            $"/internal/support/v1/sessions/{_sessionId:D}" &&
        _tenantId is not null)
    {
      return new HttpResponseMessage(HttpStatusCode.OK)
      {
        Content = JsonContent.Create(
            new
            {
              TenantId = _tenantId,
              NodeId = _nodeId,
              SessionId = _sessionId,
              Status,
              ExpiresAt = _expiresAt,
              DispatchedAt,
              RejectedAt,
              RejectionDisposition,
            },
            options: _jsonOptions),
      };
    }
    return new HttpResponseMessage(HttpStatusCode.NotFound);
  }
}
