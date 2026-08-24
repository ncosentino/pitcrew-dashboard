using System.Globalization;
using System.Text.Json;

namespace PitCrew.Support.Canary.Scenarios;

internal sealed class LinuxCanaryBrokerFailureClassifier(
    LinuxCanaryCommandRunner _commands)
{
  private const string AgentServiceName =
      "pitcrew-support-agent.service";
  private const string BrokerServiceName =
      "pitcrew-support-broker.service";
  private const string BrokerStateRoot =
      "/var/lib/pitcrew-support-broker";

  public async Task<string> ClassifyAsync(
      CancellationToken cancellationToken)
  {
    var activeState = await ReadSystemdPropertyAsync(
        BrokerServiceName,
        "ActiveState",
        cancellationToken);
    if (activeState != "active")
    {
      return "agent-broker-process-stopped";
    }
    var restarts = await ReadSystemdPropertyAsync(
        BrokerServiceName,
        "NRestarts",
        cancellationToken);
    if (uint.TryParse(
            restarts,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var restartCount) &&
        restartCount > 0)
    {
      return "agent-broker-process-restarted";
    }

    var settings = await _commands.ReadPrivilegedFileAsync(
        Path.Combine(BrokerStateRoot, "appsettings.json"),
        allowUnavailable: false,
        cancellationToken);
    var mainProcessId = await ReadSystemdPropertyAsync(
        AgentServiceName,
        "MainPID",
        cancellationToken);
    if (settings is null ||
        !int.TryParse(
            mainProcessId,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var processId) ||
        processId <= 0)
    {
      return "agent-broker-transport-unavailable";
    }
    using var document = JsonDocument.Parse(settings);
    var expectedAgentUid = document.RootElement
        .GetProperty("pitCrewSupport")
        .GetProperty("broker")
        .GetProperty("expectedAgentUid")
        .GetUInt32();
    var processStatus = await _commands.ReadPrivilegedFileAsync(
        $"/proc/{processId}/status",
        allowUnavailable: false,
        cancellationToken);
    var uidLine = processStatus?
        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .FirstOrDefault(line =>
            line.StartsWith("Uid:", StringComparison.Ordinal));
    var effectiveUid = uidLine?
        .Split(
            [' ', '\t'],
            StringSplitOptions.RemoveEmptyEntries)
        .ElementAtOrDefault(2);
    return uint.TryParse(
                effectiveUid,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var actualAgentUid) &&
            actualAgentUid != expectedAgentUid
        ? "agent-broker-peer-mismatch"
        : "agent-broker-transport-unavailable";
  }

  private Task<string?> ReadSystemdPropertyAsync(
      string serviceName,
      string propertyName,
      CancellationToken cancellationToken) =>
      _commands.ReadCommandOutputAsync(
          "systemctl",
          [
              "show",
              serviceName,
              $"--property={propertyName}",
              "--value",
          ],
          cancellationToken);
}
