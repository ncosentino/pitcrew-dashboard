using System.Net;

using PitCrew.Support.Canary.Contracts;

namespace PitCrew.Support.Canary.Scenarios;

/// <summary>
/// Exercises the fresh-enrollment workflow across one typed relay restart.
/// </summary>
public sealed class SupportRelayRestartRecoveryScenario : ICanaryScenario
{
  private const string ScenarioId =
      "support-relay-restart-recovery-v1";
  private readonly SupportFreshEnrollmentDiagnosticScenario _inner =
      new(
          ScenarioId,
          [CanaryCapabilities.RelayRestartControl],
          RestartRelayAsync);

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

  private static async Task<string> RestartRelayAsync(
      CanaryRuntimeManifest runtime,
      CanaryScenarioContext context,
      CancellationToken cancellationToken)
  {
    var requestPath = Path.Combine(
        context.RunRoot,
        CanaryTopologyControlFile.RequestFileName);
    var resultPath = Path.Combine(
        context.RunRoot,
        CanaryTopologyControlFile.ResultFileName);
    if (File.Exists(requestPath) || File.Exists(resultPath))
    {
      throw new CanaryScenarioFailureException(
          "relay-restart-rejected");
    }
    var requestId = Guid.NewGuid();
    try
    {
      CanaryTopologyControlFile.WriteRequest(
          requestPath,
          CanaryTopologyControlFile.CreateRestartRelayRequest(
              runtime.RunId,
              requestId));
      var result = await WaitForResultAsync(
          resultPath,
          runtime.RunId,
          requestId,
          cancellationToken);
      if (result.Status != "succeeded")
      {
        throw new CanaryScenarioFailureException(
            "relay-restart-rejected");
      }
      await WaitForRelayHealthAsync(
          runtime.RelayUrl,
          cancellationToken);
      return "relay-restarted";
    }
    catch (InvalidDataException exception)
    {
      throw new CanaryScenarioFailureException(
          "relay-restart-rejected",
          exception);
    }
    finally
    {
      DeleteIfPresent(requestPath);
      DeleteIfPresent(resultPath);
    }
  }

  private static async Task<CanaryTopologyControlResult>
      WaitForResultAsync(
          string path,
          string runId,
          Guid requestId,
          CancellationToken cancellationToken)
  {
    using var timeout =
        CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
    timeout.CancelAfter(TimeSpan.FromSeconds(60));
    using var timer = new PeriodicTimer(
        TimeSpan.FromMilliseconds(100));
    try
    {
      while (await timer.WaitForNextTickAsync(timeout.Token))
      {
        if (!File.Exists(path))
        {
          continue;
        }
        var result = CanaryTopologyControlFile.ReadResult(path);
        if (!string.Equals(
                result.RunId,
                runId,
                StringComparison.Ordinal) ||
            result.RequestId != requestId)
        {
          throw new CanaryScenarioFailureException(
              "relay-restart-rejected");
        }
        return result;
      }
    }
    catch (OperationCanceledException)
        when (!cancellationToken.IsCancellationRequested)
    {
      throw new CanaryScenarioFailureException(
          "relay-restart-timeout");
    }
    throw new CanaryScenarioFailureException(
        "relay-restart-timeout");
  }

  private static async Task WaitForRelayHealthAsync(
      string relayUrl,
      CancellationToken cancellationToken)
  {
    using var timeout =
        CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
    timeout.CancelAfter(TimeSpan.FromSeconds(30));
    using var timer = new PeriodicTimer(
        TimeSpan.FromMilliseconds(200));
    using var client = new HttpClient
    {
      Timeout = TimeSpan.FromSeconds(2),
    };
    var healthUrl = new Uri(
        new Uri(relayUrl, UriKind.Absolute),
        "healthz");
    try
    {
      while (await timer.WaitForNextTickAsync(timeout.Token))
      {
        if (await IsRelayHealthyAsync(
                client,
                healthUrl,
                timeout.Token))
        {
          return;
        }
      }
    }
    catch (OperationCanceledException)
        when (!cancellationToken.IsCancellationRequested)
    {
      throw new CanaryScenarioFailureException(
          "relay-restart-timeout");
    }
    throw new CanaryScenarioFailureException(
        "relay-restart-timeout");
  }

  private static async Task<bool> IsRelayHealthyAsync(
      HttpClient client,
      Uri healthUrl,
      CancellationToken cancellationToken)
  {
    HttpResponseMessage response;
    try
    {
      response = await client.GetAsync(
          healthUrl,
          HttpCompletionOption.ResponseHeadersRead,
          cancellationToken);
    }
    catch (HttpRequestException)
    {
      return false;
    }
    catch (TaskCanceledException)
        when (!cancellationToken.IsCancellationRequested)
    {
      return false;
    }
    using (response)
    {
      return response.StatusCode == HttpStatusCode.OK;
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
