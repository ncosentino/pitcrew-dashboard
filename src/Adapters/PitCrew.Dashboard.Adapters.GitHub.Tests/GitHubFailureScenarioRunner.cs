using PitCrew.Dashboard.Features.Images.Abstractions;

namespace PitCrew.Dashboard.Adapters.GitHub.Tests;

internal static class GitHubFailureScenarioRunner
{
  public static async Task<GitHubClientOutcome<GitHubRepositoryIdentity>> RunAsync(
      HttpResponseMessage operationResponse,
      CancellationToken cancellationToken)
  {
    using var context = new GitHubAdapterTestContext();
    context.EnqueueToken();
    context.Handler.Enqueue(operationResponse);
    return await context.Client.LoadRepositoryAsync(
        77,
        42,
        cancellationToken);
  }
}
