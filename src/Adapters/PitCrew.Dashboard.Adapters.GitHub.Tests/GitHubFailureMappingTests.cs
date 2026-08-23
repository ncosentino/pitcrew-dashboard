using System.Net;
using System.Net.Http.Headers;
using System.Text;

using PitCrew.Dashboard.Features.Images.Abstractions;

namespace PitCrew.Dashboard.Adapters.GitHub.Tests;

public sealed class GitHubFailureMappingTests
{
  [Test]
  public async Task Http_And_Json_Failures_Map_To_Closed_Outcomes(
      CancellationToken cancellationToken)
  {
    var notFound = await GitHubFailureScenarioRunner.RunAsync(
        new HttpResponseMessage(HttpStatusCode.NotFound),
        cancellationToken);
    var forbidden = await GitHubFailureScenarioRunner.RunAsync(
        new HttpResponseMessage(HttpStatusCode.Forbidden),
        cancellationToken);
    var transient = await GitHubFailureScenarioRunner.RunAsync(
        new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
        cancellationToken);
    var malformed = await GitHubFailureScenarioRunner.RunAsync(
        GitHubAdapterTestContext.JsonResponse("{"),
        cancellationToken);
    var oversized = await GitHubFailureScenarioRunner.RunAsync(
        GitHubAdapterTestContext.JsonResponse(
            new string('x', GitHubHttpResponseReader.MaximumJsonBytes + 1)),
        cancellationToken);
    using var rateResponse = new HttpResponseMessage(
        HttpStatusCode.TooManyRequests);
    rateResponse.Headers.RetryAfter = new RetryConditionHeaderValue(
        TimeSpan.FromSeconds(90));
    var rateLimited = await GitHubFailureScenarioRunner.RunAsync(
        rateResponse,
        cancellationToken);

    await Assert.That(notFound.Kind)
        .IsEqualTo(GitHubClientOutcomeKind.NotFound);
    await Assert.That(forbidden.Kind)
        .IsEqualTo(GitHubClientOutcomeKind.UnauthorizedOrForbidden);
    await Assert.That(transient.Kind)
        .IsEqualTo(GitHubClientOutcomeKind.TransientFailure);
    await Assert.That(malformed.Kind)
        .IsEqualTo(GitHubClientOutcomeKind.InvalidResponse);
    await Assert.That(oversized.Kind)
        .IsEqualTo(GitHubClientOutcomeKind.InvalidResponse);
    await Assert.That(rateLimited.Kind)
        .IsEqualTo(GitHubClientOutcomeKind.RateLimited);
    await Assert.That(rateLimited.RetryAt)
        .IsEqualTo(GitHubAdapterTestContext.FixedNow.AddSeconds(90));
  }

  [Test]
  public async Task Secondary_Rate_Limit_Uses_Forbidden_Retry_After(
      CancellationToken cancellationToken)
  {
    using var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
    response.Headers.RetryAfter = new RetryConditionHeaderValue(
        TimeSpan.FromSeconds(45));
    response.Headers.TryAddWithoutValidation(
        "X-RateLimit-Remaining",
        "12");

    var outcome = await GitHubFailureScenarioRunner.RunAsync(
        response,
        cancellationToken);

    await Assert.That(outcome.Kind)
        .IsEqualTo(GitHubClientOutcomeKind.RateLimited);
    await Assert.That(outcome.RetryAt)
        .IsEqualTo(GitHubAdapterTestContext.FixedNow.AddSeconds(45));
  }

  [Test]
  public async Task Rate_Limit_Reset_Is_Parsed_And_Bounded(
      CancellationToken cancellationToken)
  {
    using var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
    response.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", "0");
    response.Headers.TryAddWithoutValidation(
        "X-RateLimit-Reset",
        GitHubAdapterTestContext.FixedNow.AddDays(4)
            .ToUnixTimeSeconds()
            .ToString(System.Globalization.CultureInfo.InvariantCulture));

    var outcome = await GitHubFailureScenarioRunner.RunAsync(
        response,
        cancellationToken);

    await Assert.That(outcome.Kind)
        .IsEqualTo(GitHubClientOutcomeKind.RateLimited);
    await Assert.That(outcome.RetryAt)
        .IsEqualTo(GitHubAdapterTestContext.FixedNow.AddDays(1));
  }

  [Test]
  public async Task Cancellation_And_Timeout_Are_Distinct_Outcomes(
      CancellationToken cancellationToken)
  {
    using var cancelledContext = new GitHubAdapterTestContext();
    using var cancelledSource =
        CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    await cancelledSource.CancelAsync();

    var cancelled = await cancelledContext.Client.LoadRepositoryAsync(
        77,
        42,
        cancelledSource.Token);

    using var timeoutContext = new GitHubAdapterTestContext();
    timeoutContext.EnqueueToken();
    timeoutContext.Handler.Enqueue(
        static (_, _) => Task.FromException<HttpResponseMessage>(
            new TaskCanceledException("deterministic timeout")));
    var timedOut = await timeoutContext.Client.LoadRepositoryAsync(
        77,
        42,
        cancellationToken);

    await Assert.That(cancelled.Kind)
        .IsEqualTo(GitHubClientOutcomeKind.Cancelled);
    await Assert.That(timedOut.Kind)
        .IsEqualTo(GitHubClientOutcomeKind.TimedOut);
  }

  [Test]
  public async Task Oversized_Content_Length_Is_Rejected_Without_Reading_Body(
      CancellationToken cancellationToken)
  {
    using var response = new HttpResponseMessage(HttpStatusCode.OK)
    {
      Content = new ByteArrayContent(Encoding.UTF8.GetBytes("{}")),
    };
    response.Content.Headers.ContentLength =
        GitHubHttpResponseReader.MaximumJsonBytes + 1;

    var outcome = await GitHubFailureScenarioRunner.RunAsync(
        response,
        cancellationToken);

    await Assert.That(outcome.Kind)
        .IsEqualTo(GitHubClientOutcomeKind.InvalidResponse);
    await Assert.That(outcome.Detail)
        .IsEqualTo("response-body-oversized");
  }
}
