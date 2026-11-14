namespace PitCrew.Support.Agent.App.Tests;

internal sealed class TestHttpClientFactory(
    string _expectedName,
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond) :
    IHttpClientFactory
{
  public TestHttpClientFactory(
      string expectedName,
      Func<HttpRequestMessage, HttpResponseMessage> respond)
      : this(expectedName, (request, _) => Task.FromResult(respond(request)))
  {
  }

  public HttpClient CreateClient(string name)
  {
    if (!string.Equals(
        name,
        _expectedName,
        StringComparison.Ordinal))
    {
      throw new InvalidOperationException(
          "The test requested an unexpected HTTP client.");
    }
    return new HttpClient(new Handler(_respond));
  }

  private sealed class Handler(
      Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond) :
      HttpMessageHandler
  {
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
      cancellationToken.ThrowIfCancellationRequested();
      return _respond(request, cancellationToken);
    }
  }
}
