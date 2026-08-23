using System.Net.Http.Headers;
using System.Net.Http.Json;

using Microsoft.Extensions.Options;

using PitCrew.Dashboard.Features.Images.Abstractions;

namespace PitCrew.Dashboard.Adapters.GitHub;

internal sealed class GitHubAppTokenProvider(
    IHttpClientFactory _httpClientFactory,
    GitHubAppJwtSigner _jwtSigner,
    IOptions<GitHubAppOptions> _options,
    TimeProvider _timeProvider)
{
  private const int MaximumTokenResponseBytes = 65_536;

  public async Task<GitHubClientOutcome<string>> CreateAsync(
      long installationId,
      long repositoryId,
      CancellationToken cancellationToken)
  {
    if (installationId <= 0 || repositoryId <= 0)
    {
      return InvalidRequest("installation-or-repository-id-invalid");
    }

    var jwtOutcome = await _jwtSigner.CreateAsync(cancellationToken);
    if (jwtOutcome.Kind != GitHubClientOutcomeKind.Success ||
        string.IsNullOrEmpty(jwtOutcome.Value))
    {
      return jwtOutcome;
    }

    using var request = new HttpRequestMessage(
        HttpMethod.Post,
        $"app/installations/{installationId}/access_tokens");
    ApplyPinnedHeaders(request);
    request.Headers.Authorization = new AuthenticationHeaderValue(
        "Bearer",
        jwtOutcome.Value);
    request.Content = JsonContent.Create(
        new GitHubInstallationTokenPayload(
            [repositoryId],
            new GitHubInstallationTokenPermissions("write", "read")),
        GitHubJsonContext.Default.GitHubInstallationTokenPayload);

    try
    {
      using var client = CreateClient();
      using var timeoutSource = new CancellationTokenSource(
          _options.Value.Timeout,
          _timeProvider);
      using var requestSource = CancellationTokenSource.CreateLinkedTokenSource(
          cancellationToken,
          timeoutSource.Token);
      using var response = await client.SendAsync(
          request,
          HttpCompletionOption.ResponseHeadersRead,
          requestSource.Token);
      var responseOutcome =
          await GitHubHttpResponseReader.ReadJsonAsync(
              response,
              GitHubJsonContext.Default.GitHubInstallationTokenReplyPayload,
              MaximumTokenResponseBytes,
              _timeProvider,
              requestSource.Token);
      if (responseOutcome.Kind != GitHubClientOutcomeKind.Success ||
          responseOutcome.Value is null)
      {
        return new(
            responseOutcome.Kind,
            null,
            responseOutcome.RetryAt,
            responseOutcome.Detail);
      }

      var token = responseOutcome.Value.Token;
      if (string.IsNullOrEmpty(token) ||
          token.Length > 4096 ||
          token.IndexOfAny(['\r', '\n', '\0']) >= 0 ||
          responseOutcome.Value.ExpiresAt <= _timeProvider.GetUtcNow().AddMinutes(1))
      {
        return new(
            GitHubClientOutcomeKind.InvalidResponse,
            null,
            null,
            "installation-token-response-invalid");
      }

      return new(GitHubClientOutcomeKind.Success, token, null, null);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      return new(GitHubClientOutcomeKind.Cancelled, null, null, "cancelled");
    }
    catch (OperationCanceledException)
    {
      return new(GitHubClientOutcomeKind.TimedOut, null, null, "request-timed-out");
    }
    catch (HttpRequestException)
    {
      return new(
          GitHubClientOutcomeKind.TransientFailure,
          null,
          null,
          "transport-failure");
    }
  }

  internal static void ApplyPinnedHeaders(HttpRequestMessage request)
  {
    request.Headers.Accept.Add(
        new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    request.Headers.TryAddWithoutValidation(
        "X-GitHub-Api-Version",
        GitHubImageWorkflowClient.ApiVersion);
    request.Headers.UserAgent.ParseAdd("PitCrew-Dashboard-GitHubApp/1");
  }

  private HttpClient CreateClient()
  {
    var client = _httpClientFactory.CreateClient(
        GitHubApiHttpClientOptions.ClientName);
    client.BaseAddress = _options.Value.BaseAddress;
    client.Timeout = Timeout.InfiniteTimeSpan;
    client.MaxResponseContentBufferSize = GitHubHttpResponseReader.MaximumJsonBytes;
    return client;
  }

  private static GitHubClientOutcome<string> InvalidRequest(string detail) =>
      new(GitHubClientOutcomeKind.InvalidRequest, null, null, detail);
}
