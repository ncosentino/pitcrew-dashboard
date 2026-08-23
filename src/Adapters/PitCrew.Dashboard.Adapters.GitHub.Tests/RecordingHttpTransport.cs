namespace PitCrew.Dashboard.Adapters.GitHub.Tests;

internal sealed class RecordingHttpTransport : HttpMessageHandler
{
  private readonly HttpExchangeQueue _exchanges = new();

  public IReadOnlyList<HttpRequestSnapshot> Requests => _exchanges.Requests;

  public void Enqueue(HttpResponseMessage response) =>
      _exchanges.Enqueue(response);

  public void Enqueue(
      Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>
          response) =>
      _exchanges.Enqueue(response);

  protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request,
      CancellationToken cancellationToken) =>
      _exchanges.ExchangeAsync(request, cancellationToken);
}
