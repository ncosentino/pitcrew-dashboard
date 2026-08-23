using System.Collections.Concurrent;

namespace PitCrew.Dashboard.Adapters.GitHub.Tests;

internal sealed class RecordingHttpMessageHandler : HttpMessageHandler
{
  private readonly ConcurrentQueue<
      Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>>
      _responses = new();
  private readonly ConcurrentQueue<HttpRequestSnapshot> _requests = new();

  public IReadOnlyList<HttpRequestSnapshot> Requests => [.. _requests];

  public void Enqueue(HttpResponseMessage response) =>
      _responses.Enqueue((_, _) => Task.FromResult(response));

  public void Enqueue(
      Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>
          response) =>
      _responses.Enqueue(response);

  protected override async Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request,
      CancellationToken cancellationToken)
  {
    var headers = request.Headers.ToDictionary(
        static pair => pair.Key,
        static pair => string.Join(",", pair.Value),
        StringComparer.OrdinalIgnoreCase);
    if (request.Content is not null)
    {
      foreach (var pair in request.Content.Headers)
      {
        headers[pair.Key] = string.Join(",", pair.Value);
      }
    }
    var body = request.Content is null
        ? null
        : await request.Content.ReadAsStringAsync(cancellationToken);
    _requests.Enqueue(
        new HttpRequestSnapshot(
            request.Method,
            request.RequestUri!,
            headers,
            body));

    if (!_responses.TryDequeue(out var response))
    {
      throw new InvalidOperationException(
          "No deterministic HTTP response was queued.");
    }
    return await response(request, cancellationToken);
  }
}
