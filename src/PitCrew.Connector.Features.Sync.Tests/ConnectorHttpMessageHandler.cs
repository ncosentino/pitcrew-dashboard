namespace PitCrew.Connector.Features.Sync.Tests;

internal sealed class ConnectorHttpMessageHandler(
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send) :
    HttpMessageHandler
{
  protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request,
      CancellationToken cancellationToken) =>
      _send(request, cancellationToken);
}
