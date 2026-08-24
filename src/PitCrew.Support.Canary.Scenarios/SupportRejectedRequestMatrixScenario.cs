using System.Text.Json;

using PitCrew.Support.Canary.Contracts;

namespace PitCrew.Support.Canary.Scenarios;

/// <summary>
/// Exercises bounded agent rejection outcomes through cryptographically valid
/// requests injected into the real relay.
/// </summary>
public sealed class SupportRejectedRequestMatrixScenario : ICanaryScenario
{
  private const string ScenarioId =
      "support-request-rejection-matrix-v1";
  private readonly SupportFreshEnrollmentDiagnosticScenario _inner =
      new(
          ScenarioId,
          [
              CanaryCapabilities.SupportAgentProcess,
              CanaryCapabilities.SupportBrokerProcess,
              CanaryCapabilities.RejectedRequestInjection,
          ],
          afterFirstAcceptedPoll: null,
          diagnosticModes: null,
          afterBootstrapFinalization:
              ExerciseRejectedRequestsAsync);

  internal static IReadOnlyList<(
      string InjectionCase,
      string ExpectedDisposition)> ExpectedDispositions { get; } =
  [
      (
          CanaryRejectedRequestCases.MalformedRequest,
          "request-malformed"),
      (
          CanaryRejectedRequestCases.SessionMismatch,
          "session-mismatch"),
      (
          CanaryRejectedRequestCases.WrongTenantOrNode,
          "wrong-tenant-or-node"),
      (
          CanaryRejectedRequestCases.UnsupportedCapability,
          "unsupported-capability"),
      (
          CanaryRejectedRequestCases.UnsupportedDiagnosticMode,
          "unsupported-diagnostic-mode"),
      (
          CanaryRejectedRequestCases.ExpiredRequest,
          "request-expired"),
      (
          CanaryRejectedRequestCases.InvalidNonce,
          "invalid-nonce"),
      (
          CanaryRejectedRequestCases.ReplaySeed,
          "completed"),
      (
          CanaryRejectedRequestCases.RequestReplay,
          "request-replay"),
  ];

  /// <inheritdoc />
  public string Id => _inner.Id;

  /// <inheritdoc />
  public IReadOnlySet<string> RequiredCapabilities =>
      _inner.RequiredCapabilities;

  /// <inheritdoc />
  public Task<CanaryScenarioResult> RunAsync(
      CanaryRuntimeManifest runtime,
      CanaryScenarioContext context,
      CancellationToken cancellationToken) =>
      _inner.RunAsync(
          runtime,
          context,
          cancellationToken);

  private static async Task<string>
      ExerciseRejectedRequestsAsync(
          CanaryRuntimeManifest runtime,
          CanaryScenarioContext context,
          Guid nodeId,
          string agentStateRoot,
          CancellationToken cancellationToken)
  {
    var encryptionPublicKey =
        ReadNodeEncryptionPublicKey(agentStateRoot);
    var replayId = Guid.NewGuid();
    foreach (var expectation in ExpectedDispositions)
    {
      var sessionId = Guid.NewGuid();
      DeleteIfPresent(
          Path.Combine(
              agentStateRoot,
              "agent-startup-status.json"));
      await ExecuteControlAsync(
          context.RunRoot,
          runtime.RunId,
          CanaryRejectedRequestControlFile.CreateEnqueueRequest(
              runtime.RunId,
              Guid.NewGuid(),
              expectation.InjectionCase,
              sessionId,
              nodeId,
              encryptionPublicKey,
              expectation.InjectionCase is
                  CanaryRejectedRequestCases.ReplaySeed or
                  CanaryRejectedRequestCases.RequestReplay
                  ? replayId
                  : null),
          "request-enqueued",
          context.TimeProvider,
          cancellationToken);
      await WaitForDispositionAsync(
          agentStateRoot,
          expectation.ExpectedDisposition,
          context.TimeProvider,
          cancellationToken);
      if (expectation.ExpectedDisposition != "completed")
      {
        await ExecuteControlAsync(
            context.RunRoot,
            runtime.RunId,
            CanaryRejectedRequestControlFile
                .CreateCancellationRequest(
                    runtime.RunId,
                    Guid.NewGuid(),
                    sessionId),
            "request-cancelled",
            context.TimeProvider,
            cancellationToken);
      }
    }
    return "request-rejection-matrix-verified";
  }

  private static string ReadNodeEncryptionPublicKey(
      string agentStateRoot)
  {
    using var identity = JsonDocument.Parse(
        File.ReadAllText(
            Path.Combine(
                agentStateRoot,
                "identity-state",
                "identity",
                "identity.json")));
    var publicKey = identity.RootElement
        .GetProperty("keys")
        .GetProperty("encryptionPublicKeySpki")
        .GetString();
    return string.IsNullOrWhiteSpace(publicKey)
        ? throw new CanaryScenarioFailureException(
            "request-injection-control-rejected")
        : publicKey;
  }

  private static async Task ExecuteControlAsync(
      string runRoot,
      string runId,
      CanaryRejectedRequestControlRequest request,
      string expectedDisposition,
      TimeProvider timeProvider,
      CancellationToken cancellationToken)
  {
    var requestPath = Path.Combine(
        runRoot,
        CanaryRejectedRequestControlFile.RequestFileName);
    var resultPath = Path.Combine(
        runRoot,
        CanaryRejectedRequestControlFile.ResultFileName);
    if (File.Exists(requestPath) ||
        File.Exists(resultPath))
    {
      throw new CanaryScenarioFailureException(
          "request-injection-control-rejected");
    }
    try
    {
      CanaryRejectedRequestControlFile.WriteRequest(
          requestPath,
          request);
      var result = await WaitForControlResultAsync(
          resultPath,
          runId,
          request.RequestId,
          timeProvider,
          cancellationToken);
      if (result.Status != "succeeded" ||
          result.Disposition != expectedDisposition)
      {
        throw new CanaryScenarioFailureException(
            "request-injection-control-rejected");
      }
    }
    catch (InvalidDataException exception)
    {
      throw new CanaryScenarioFailureException(
          "request-injection-control-rejected",
          exception);
    }
    finally
    {
      DeleteIfPresent(requestPath);
      DeleteIfPresent(resultPath);
    }
  }

  private static async Task<CanaryRejectedRequestControlResult>
      WaitForControlResultAsync(
          string path,
          string runId,
          Guid requestId,
          TimeProvider timeProvider,
          CancellationToken cancellationToken)
  {
    using var timeout = new CancellationTokenSource(
        TimeSpan.FromSeconds(15),
        timeProvider);
    using var linked =
        CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);
    using var timer = new PeriodicTimer(
        TimeSpan.FromMilliseconds(100),
        timeProvider);
    try
    {
      while (await timer.WaitForNextTickAsync(linked.Token))
      {
        if (!File.Exists(path))
        {
          continue;
        }
        var result =
            CanaryRejectedRequestControlFile.ReadResult(path);
        if (!string.Equals(
                result.RunId,
                runId,
                StringComparison.Ordinal) ||
            result.RequestId != requestId)
        {
          throw new CanaryScenarioFailureException(
              "request-injection-control-rejected");
        }
        return result;
      }
    }
    catch (OperationCanceledException)
        when (!cancellationToken.IsCancellationRequested)
    {
      throw new CanaryScenarioFailureException(
          "request-injection-control-rejected");
    }
    throw new CanaryScenarioFailureException(
        "request-injection-control-rejected");
  }

  private static async Task WaitForDispositionAsync(
      string agentStateRoot,
      string expectedDisposition,
      TimeProvider timeProvider,
      CancellationToken cancellationToken)
  {
    var statusPath = Path.Combine(
        agentStateRoot,
        "agent-startup-status.json");
    using var timeout = new CancellationTokenSource(
        TimeSpan.FromSeconds(45),
        timeProvider);
    using var linked =
        CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);
    using var timer = new PeriodicTimer(
        TimeSpan.FromMilliseconds(200),
        timeProvider);
    try
    {
      while (await timer.WaitForNextTickAsync(linked.Token))
      {
        if (!File.Exists(statusPath))
        {
          continue;
        }
        using var status = JsonDocument.Parse(
            await File.ReadAllTextAsync(
                statusPath,
                linked.Token));
        var root = status.RootElement;
        if (root.GetProperty("phase").GetString() !=
            "request-processing")
        {
          continue;
        }
        var disposition = root
            .GetProperty("disposition")
            .GetString();
        if (disposition == expectedDisposition)
        {
          return;
        }
        throw new CanaryScenarioFailureException(
            disposition == "unhandled-exception"
                ? "agent-unhandled-exception"
                : "request-rejection-matrix-mismatch");
      }
    }
    catch (OperationCanceledException)
        when (!cancellationToken.IsCancellationRequested)
    {
      throw new CanaryScenarioFailureException(
          "agent-poll-timeout");
    }
  }

  private static void DeleteIfPresent(string path)
  {
    if (File.Exists(path))
    {
      File.Delete(path);
    }
  }
}
