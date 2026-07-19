using System.Net.Http.Headers;
using System.Text.Json;

using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.Extensions.Logging;

using NexusLabs.Needlr;

namespace PitCrew.Dashboard.Kernel.Authentication;

[DoNotAutoRegister]
internal sealed partial class GitHubOAuthEvents(
    ILogger<GitHubOAuthEvents> _logger) : OAuthEvents
{
  public override async Task CreatingTicket(
      OAuthCreatingTicketContext context)
  {
    using var request = new HttpRequestMessage(
        HttpMethod.Get,
        context.Options.UserInformationEndpoint);
    request.Headers.Accept.Add(
        new MediaTypeWithQualityHeaderValue(
            "application/vnd.github+json"));
    request.Headers.Add(
        "X-GitHub-Api-Version",
        "2022-11-28");
    request.Headers.Authorization = new AuthenticationHeaderValue(
        "Bearer",
        context.AccessToken);
    request.Headers.UserAgent.Add(
        new ProductInfoHeaderValue("PitCrew-Dashboard", "1.0"));

    using var response = await context.Backchannel.SendAsync(
        request,
        HttpCompletionOption.ResponseHeadersRead,
        context.HttpContext.RequestAborted);
    if (!response.IsSuccessStatusCode)
    {
      LogProfileRequestRejected((int)response.StatusCode);
      context.Fail("GitHub did not return the authenticated user profile.");
      return;
    }

    await using var content = await response.Content.ReadAsStreamAsync(
        context.HttpContext.RequestAborted);
    using var profile = await JsonDocument.ParseAsync(
        content,
        cancellationToken: context.HttpContext.RequestAborted);
    context.RunClaimActions(profile.RootElement);

    var githubUserId = context.Identity?.FindFirst(
        PitCrewClaimTypes.GitHubUserId)?.Value;
    var githubLogin = context.Identity?.FindFirst(
        PitCrewClaimTypes.GitHubLogin)?.Value;
    if (string.IsNullOrWhiteSpace(githubUserId) ||
        string.IsNullOrWhiteSpace(githubLogin))
    {
      context.Fail(
          "GitHub did not return the required user identifier and login.");
    }
  }

  [LoggerMessage(
      Level = LogLevel.Warning,
      Message = "GitHub user profile request returned HTTP {StatusCode}.")]
  private partial void LogProfileRequestRejected(int statusCode);
}
