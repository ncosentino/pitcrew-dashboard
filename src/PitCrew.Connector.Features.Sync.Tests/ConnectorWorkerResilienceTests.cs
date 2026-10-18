using System.Net;

using Moq;

using PitCrew.Protocol;

namespace PitCrew.Connector.Features.Sync.Tests;

public sealed class ConnectorWorkerResilienceTests
{
  [Test]
  [Arguments(HttpStatusCode.BadRequest, ConnectorHealthFailureCategories.PayloadRejected)]
  [Arguments(HttpStatusCode.Unauthorized, ConnectorHealthFailureCategories.CredentialRejected)]
  [Arguments(HttpStatusCode.RequestTimeout, ConnectorHealthFailureCategories.SynchronizationTimeout)]
  [Arguments(HttpStatusCode.TooManyRequests, ConnectorHealthFailureCategories.SynchronizationRateLimited)]
  [Arguments(HttpStatusCode.ServiceUnavailable, ConnectorHealthFailureCategories.SynchronizationServer)]
  public async Task Rejection_Is_Durable_And_Retries_Without_Stopping_The_Worker(
      HttpStatusCode status,
      string category,
      CancellationToken cancellationToken)
  {
    await using var context = new ConnectorWorkerTestContext();
    context.EnableHttpTransport();
    await context.InitializeAsync(cancellationToken);
    context.ResponseFactory = count =>
    {
      if (count == 1)
      {
        var rejection = new HttpResponseMessage(status)
        {
          Content = new StringContent("synthetic-private-response-not-for-journaling"),
        };
        rejection.Headers.Add("X-PitCrew-Sync-Rejection", "Profile");
        return rejection;
      }
      return new HttpResponseMessage(HttpStatusCode.OK)
      {
        Content = System.Net.Http.Json.JsonContent.Create(
            new ConnectorSyncResponse(context.Clock.GetUtcNow(), 15, null, null),
            PitCrewProtocolJsonContext.Default.ConnectorSyncResponse),
      };
    };
    await context.Worker.StartAsync(cancellationToken);
    var retry = await context.Clock.WaitForDelayAsync(cancellationToken);
    var failure = await context.ReadSnapshotAsync(cancellationToken);
    await Assert.That(failure.LastFailureCategory).IsEqualTo(category);
    await Assert.That(failure.LastFailureHttpStatusCode).IsEqualTo((int)status);
    await Assert.That(failure.LastFailureRejectionReason).IsEqualTo("Profile");
    await Assert.That(failure.NextRetryAt)
        .IsEqualTo(context.Clock.GetUtcNow().Add(retry));
    await Assert.That(retry).IsGreaterThan(TimeSpan.Zero)
        .Because("HTTP rejection must back off instead of spinning");
    await Assert.That(context.Worker.ExecuteTask!.IsCompleted).IsFalse()
        .Because("HTTP rejection is recoverable without terminating the worker");

    context.Clock.Advance(retry);
    await context.Clock.WaitForDelayAsync(cancellationToken);
    var recovered = await context.ReadSnapshotAsync(cancellationToken);
    await Assert.That(context.Requests.Count).IsGreaterThanOrEqualTo(2)
        .Because("the same worker must synchronize again after rejection");
    await Assert.That(recovered.State).IsEqualTo(ConnectorHealthStates.Healthy);
    await Assert.That(recovered.ConsecutiveFailures).IsEqualTo(0);
    await Assert.That(recovered.LastSuccessAt).IsEqualTo(context.Clock.GetUtcNow());
    await Assert.That(recovered.LastFailureHttpStatusCode).IsEqualTo((int)status);
    await Assert.That(context.Requests[1].ConnectorHealth).IsNotNull();
    await Assert.That(ConnectorHealthReplayContract.IsValid(
            context.Requests[1].ConnectorHealth,
            context.Clock.GetUtcNow().AddMinutes(5)))
        .IsTrue()
        .Because("rejection replay must stay compatible with existing Dashboard contracts");
    var journalText = await File.ReadAllTextAsync(
        Path.Combine(context.Root, "health", "connector-events.jsonl"),
        cancellationToken);
    await Assert.That(journalText).DoesNotContain("synthetic-private-response");
    context.ProcessLifetime.Verify(value => value.Fail(), Times.Never);
  }

  [Test]
  [Arguments("arbitrary-private-header", ConnectorSyncRejectionReason.Unspecified)]
  [Arguments("5", ConnectorSyncRejectionReason.Unspecified)]
  [Arguments(" Profile ", ConnectorSyncRejectionReason.Unspecified)]
  [Arguments("Profile", ConnectorSyncRejectionReason.Profile)]
  public async Task Api_Client_Only_Retains_Allowlisted_Rejection_Metadata(
      string header,
      ConnectorSyncRejectionReason expected,
      CancellationToken cancellationToken)
  {
    await using var context = new ConnectorWorkerTestContext();
    context.EnableHttpTransport();
    context.ResponseFactory = _ =>
    {
      var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
      {
        Content = new StringContent("synthetic-private-response"),
      };
      response.Headers.TryAddWithoutValidation("X-PitCrew-Sync-Rejection", header);
      return response;
    };
    var attempt = await context.ApiClient.SyncAsync(
        "synthetic-credential",
        new ConnectorSyncRequest(
            PitCrewProtocol.Version, "test", context.Clock.GetUtcNow(), []),
        cancellationToken);
    await Assert.That(attempt.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    await Assert.That(attempt.Response).IsNull();
    await Assert.That(attempt.RejectionReason).IsEqualTo(expected);
  }

  [Test]
  public async Task Fatal_Worker_Failure_Invokes_The_Process_Failure_Boundary(
      CancellationToken cancellationToken)
  {
    await using var context = new ConnectorWorkerTestContext();
    context.Options.DashboardUrl = "invalid-uri";
    context.ProcessLifetime.Setup(value => value.Fail());
    await context.Worker.StartAsync(cancellationToken);
    await Assert.ThrowsAsync<InvalidOperationException>(
        async () => await context.Worker.ExecuteTask!.WaitAsync(cancellationToken));
    context.ProcessLifetime.Verify(value => value.Fail(), Times.Once);
    var snapshot = await context.ReadSnapshotAsync(cancellationToken);
    await Assert.That(snapshot.LastFailureCategory)
        .IsEqualTo(ConnectorHealthFailureCategories.ConfigurationInvalid);
  }

  [Test]
  public async Task Intentional_Stop_Does_Not_Invoke_Process_Failure(
      CancellationToken cancellationToken)
  {
    await using var context = new ConnectorWorkerTestContext();
    context.EnableHttpTransport();
    await context.InitializeAsync(cancellationToken);
    await context.Worker.StartAsync(cancellationToken);
    await context.Clock.WaitForDelayAsync(cancellationToken);
    await context.Worker.StopAsync(cancellationToken);
    context.ProcessLifetime.Verify(value => value.Fail(), Times.Never);
    var snapshot = await context.ReadSnapshotAsync(cancellationToken);
    await Assert.That(snapshot.State).IsEqualTo(ConnectorHealthStates.Stopping);
  }
}
