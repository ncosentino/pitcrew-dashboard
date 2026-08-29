using Microsoft.Extensions.Http;

using NexusLabs.Needlr;

namespace PitCrew.Dashboard.Adapters.GitHub;

[DoNotAutoRegister]
internal sealed class GitHubHttpMessageHandlerBuilderFilter :
    IHttpMessageHandlerBuilderFilter
{
  public Action<HttpMessageHandlerBuilder> Configure(
      Action<HttpMessageHandlerBuilder> next) =>
      builder =>
      {
        next(builder);
        if (!string.Equals(
                builder.Name,
                GitHubApiHttpClientOptions.ClientName,
                StringComparison.Ordinal))
        {
          return;
        }

        if (builder.PrimaryHandler is HttpClientHandler clientHandler)
        {
          clientHandler.AllowAutoRedirect = false;
          clientHandler.UseCookies = false;
          return;
        }

        if (builder.PrimaryHandler is SocketsHttpHandler socketsHandler)
        {
          socketsHandler.AllowAutoRedirect = false;
          socketsHandler.UseCookies = false;
          return;
        }

        builder.PrimaryHandler = new SocketsHttpHandler
        {
          AllowAutoRedirect = false,
          UseCookies = false,
        };
      };
}
