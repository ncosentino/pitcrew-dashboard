namespace PitCrew.Support.Agent.App.Tests;

internal sealed class TestHttpClientFactory(
    string _expectedName,
    Func<HttpRequestMessage, HttpResponseMessage> _respond) :
    IHttpClientFactory
{
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
      Func<HttpRequestMessage, HttpResponseMessage> _respond) :
      HttpMessageHandler
  {
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
      cancellationToken.ThrowIfCancellationRequested();
      return Task.FromResult(_respond(request));
    }
  }
}
