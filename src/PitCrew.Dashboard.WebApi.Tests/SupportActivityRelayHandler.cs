using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace PitCrew.Dashboard.WebApi.Tests;

internal sealed class SupportActivityRelayHandler(
    DateTimeOffset _lastPollAt,
    DateTimeOffset _lastResultAt) : HttpMessageHandler
{
  private int _activityRequestCount;

  public bool FailActivity { get; set; }

  public int ActivityRequestCount =>
      Volatile.Read(ref _activityRequestCount);

  public string? LastTenantId { get; private set; }

  public IReadOnlyList<Guid> LastNodeIds { get; private set; } = [];

  public void SetActivity(
      DateTimeOffset lastPollAt,
      DateTimeOffset lastResultAt)
  {
    _lastPollAt = lastPollAt;
    _lastResultAt = lastResultAt;
  }

  protected override async Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request,
      CancellationToken cancellationToken)
  {
    if (request.RequestUri?.AbsolutePath !=
        "/internal/support/v1/nodes/activity")
    {
      return new HttpResponseMessage(HttpStatusCode.NoContent);
    }
    Interlocked.Increment(ref _activityRequestCount);
    using var document = JsonDocument.Parse(
        await request.Content!.ReadAsStringAsync(cancellationToken));
    LastTenantId = document.RootElement
        .GetProperty("tenantId")
        .GetString();
    LastNodeIds = document.RootElement
        .GetProperty("nodeIds")
        .EnumerateArray()
        .Select(element => element.GetGuid())
        .ToArray();
    if (FailActivity)
    {
      return new HttpResponseMessage(
          HttpStatusCode.InternalServerError);
    }
    return new HttpResponseMessage(HttpStatusCode.OK)
    {
      Content = JsonContent.Create(
          LastNodeIds.Select(nodeId => new
          {
            nodeId,
            lastPollAt = _lastPollAt,
            lastResultAt = _lastResultAt,
          })),
    };
  }
}
