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
      return await ClassifyStoppedBrokerAsync(
          cancellationToken);
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

  private async Task<string> ClassifyStoppedBrokerAsync(
      CancellationToken cancellationToken)
  {
    var journal = await _commands.ReadCommandOutputAsync(
        "sudo",
        [
            "-n",
            "journalctl",
            "--unit",
            BrokerServiceName,
            "--no-pager",
            "--output=cat",
            "--lines=80",
        ],
        cancellationToken);
    if (journal is null)
    {
      return "agent-broker-process-stopped";
    }
    if (journal.Contains(
        nameof(UnauthorizedAccessException),
        StringComparison.Ordinal))
    {
      return "agent-broker-crash-access-denied";
    }
    if (journal.Contains(
        nameof(DirectoryNotFoundException),
        StringComparison.Ordinal))
    {
      return "agent-broker-crash-directory-not-found";
    }
    if (journal.Contains(
        nameof(FileNotFoundException),
        StringComparison.Ordinal))
    {
      return "agent-broker-crash-file-not-found";
    }
    if (journal.Contains(
        "UnixPeerCredentialReader",
        StringComparison.Ordinal))
    {
      return "agent-broker-crash-peer-read";
    }
    if (journal.Contains(
        "SupportBrokerPipeCodec.ReadAsync",
        StringComparison.Ordinal))
    {
      return "agent-broker-crash-request-read";
    }
    if (journal.Contains(
        "SupportEvidenceAccessValidator",
        StringComparison.Ordinal))
    {
      return "agent-broker-crash-evidence-validation";
    }
    if (journal.Contains(
        "SupportDiagnosticsBroker",
        StringComparison.Ordinal))
    {
      return "agent-broker-crash-diagnostics-execution";
    }
    if (journal.Contains(
        "SupportBrokerPipeCodec.WriteAsync",
        StringComparison.Ordinal))
    {
      return "agent-broker-crash-response-write";
    }
    if (journal.Contains(
        nameof(IOException),
        StringComparison.Ordinal))
    {
      return "agent-broker-crash-io";
    }
    if (journal.Contains(
            "System.Management.Automation",
            StringComparison.Ordinal) ||
        journal.Contains(
            "PowerShell",
            StringComparison.Ordinal))
    {
      return "agent-broker-crash-powershell";
    }
    if (journal.Contains(
        nameof(InvalidOperationException),
        StringComparison.Ordinal))
    {
      return "agent-broker-crash-invalid-operation";
    }
    if (journal.Contains(
            nameof(System.Net.Sockets.SocketException),
            StringComparison.Ordinal) ||
        journal.Contains(
            nameof(System.ComponentModel.Win32Exception),
            StringComparison.Ordinal))
    {
      return "agent-broker-crash-socket";
    }
    return "agent-broker-crash-unclassified";
  }
}
