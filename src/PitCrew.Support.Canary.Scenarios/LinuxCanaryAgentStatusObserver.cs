using System.Text.Json;

namespace PitCrew.Support.Canary.Scenarios;

internal sealed class LinuxCanaryAgentStatusObserver(
    string _agentStateRoot,
    LinuxCanaryCommandRunner _commands,
    LinuxCanaryBrokerFailureClassifier _brokerFailureClassifier)
{
  private string StatusPath => Path.Combine(
      _agentStateRoot,
      "agent-startup-status.json");

  public async Task WaitForAcceptedPollAsync(
      CancellationToken cancellationToken)
  {
    using var timeout =
        CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
    timeout.CancelAfter(TimeSpan.FromSeconds(60));
    using var timer = new PeriodicTimer(
        TimeSpan.FromMilliseconds(500));
    try
    {
      while (await timer.WaitForNextTickAsync(timeout.Token))
      {
        var content = await _commands.ReadPrivilegedFileAsync(
            StatusPath,
            allowUnavailable: true,
            timeout.Token);
        if (content is null)
        {
          continue;
        }
        try
        {
          using var document = JsonDocument.Parse(content);
          var status = document.RootElement;
          var phase = status.GetProperty("phase").GetString();
          var disposition = status
              .GetProperty("disposition")
              .GetString();
          if (phase == "relay-poll" &&
              disposition == "accepted")
          {
            return;
          }
          if (disposition is
              "unhandled-exception" or
              "credential-rejected" or
              "enrollment-rejected" or
              "active-identity-unavailable")
          {
            throw new CanaryScenarioFailureException(
                $"agent-{disposition}");
          }
        }
        catch (JsonException)
        {
          continue;
        }
      }
    }
    catch (OperationCanceledException)
        when (!cancellationToken.IsCancellationRequested)
    {
      throw new CanaryScenarioFailureException(
          "agent-poll-timeout");
    }
  }

  public async Task<string?> ReadRequestDispositionAsync(
      CancellationToken cancellationToken)
  {
    var content = await _commands.ReadPrivilegedFileAsync(
        StatusPath,
        allowUnavailable: true,
        cancellationToken);
    var disposition = content is null
        ? null
        : ReadRequestDisposition(content);
    return disposition == "agent-broker-io-unavailable"
        ? await _brokerFailureClassifier.ClassifyAsync(
            cancellationToken)
        : disposition;
  }

  public async Task<string?> ObserveRequestFailureAsync(
      CancellationToken cancellationToken)
  {
    using var timer = new PeriodicTimer(
        TimeSpan.FromMilliseconds(500));
    try
    {
      while (await timer.WaitForNextTickAsync(cancellationToken))
      {
        var disposition = await ReadRequestDispositionAsync(
            cancellationToken);
        if (disposition is not null)
        {
          return disposition;
        }
      }
    }
    catch (OperationCanceledException)
        when (cancellationToken.IsCancellationRequested)
    {
      return null;
    }
    return null;
  }

  public async Task PrepareRequestObservationAsync(
      CancellationToken cancellationToken)
  {
    if (await _commands.RunSudoAsync(
            ["rm", "-f", "--", StatusPath],
            TimeSpan.FromSeconds(15),
            cancellationToken) != 0)
    {
      throw new CanaryScenarioFailureException(
          "linux-service-inspection-failed");
    }
  }

  private static string? ReadRequestDisposition(string content)
  {
    using var status = JsonDocument.Parse(content);
    var root = status.RootElement;
    if (root.GetProperty("phase").GetString() !=
        "request-processing")
    {
      return null;
    }
    var disposition = root
        .GetProperty("disposition")
        .GetString();
    return disposition == "completed"
        ? null
        : $"agent-{disposition}";
  }
}
