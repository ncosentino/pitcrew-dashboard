namespace PitCrew.Dashboard.Adapters.GitHub.Tests;

internal sealed class RecordingHttpClientFactory(
    RecordingHttpMessageHandler _handler) : IHttpClientFactory
{
  public HttpClient CreateClient(string name)
  {
    if (name != GitHubApiHttpClientOptions.ClientName)
    {
      throw new InvalidOperationException(
          "The adapter requested an unexpected named HTTP client.");
    }
    return new HttpClient(_handler, disposeHandler: false);
  }
}
