using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace PitCrew.Support.Canary.Scenarios;

internal sealed class LinuxInstalledCanaryNode : IInstalledCanaryNode
{
  private const string CandidateVersion = "0.0.0-canary";
  private const string AgentUser = "pitcrew-support-agent";
  private const string BrokerUser = "pitcrew-support-broker";
  private const string IpcGroup = "pitcrew-support-ipc";
  private const string AgentInstallRoot =
      "/opt/pitcrew-support-agent";
  private const string BrokerInstallRoot =
      "/opt/pitcrew-support-broker";
  private const string BrokerStateRoot =
      "/var/lib/pitcrew-support-broker";
  private const string InstallerStateRoot =
      "/var/lib/pitcrew-support-installer";
  private const string AgentUnitPath =
      "/etc/systemd/system/pitcrew-support-agent.service";
  private const string BrokerUnitPath =
      "/etc/systemd/system/pitcrew-support-broker.service";
  private const string RuntimeRoot = "/run/pitcrew-support";
  private static readonly JsonSerializerOptions _jsonOptions =
      new(JsonSerializerDefaults.Web)
      {
        WriteIndented = true,
      };
  private readonly string _runRoot;
  private readonly string _installerPath;
  private readonly string _artifactRoot;
  private readonly LinuxCanaryCommandRunner _commands;
  private readonly LinuxCanaryConnectorFixture _connectorFixture;
  private readonly LinuxCanaryPitCrewFixture _pitCrewFixture;
  private bool _installed;

  public LinuxInstalledCanaryNode(
      CanaryScenarioContext context,
      string fixtureRoot,
      string runId)
  {
    if (!OperatingSystem.IsLinux() ||
        RuntimeInformation.OSArchitecture != Architecture.X64)
    {
      throw new CanaryScenarioFailureException(
          "linux-installed-platform-required");
    }

    _runRoot = context.RunRoot;
    _commands = new LinuxCanaryCommandRunner(context.RunRoot);
    _connectorFixture = new LinuxCanaryConnectorFixture(
        context.RunRoot,
        _commands);
    _pitCrewFixture = new LinuxCanaryPitCrewFixture(
        fixtureRoot,
        runId,
        _commands);
    AgentStateRoot = "/var/lib/pitcrew-support-agent";
    _artifactRoot = Path.Combine(
        context.RunRoot,
        "artifacts",
        "support-plane");
    _installerPath = Path.Combine(
        _artifactRoot,
        "publish",
        "installer-linux-x64",
        "Install-PitCrewSupportPlane.ps1");
  }

  public string AgentStateRoot { get; }

  public string InstallationCategory => "linux-services-installed";

  public async Task InstallAsync(
      string dashboardUrl,
      string enrollmentCode,
      CancellationToken cancellationToken)
  {
    if (!File.Exists(_installerPath))
    {
      throw new CanaryScenarioFailureException(
          "linux-installed-artifact-missing");
    }
    if (await _commands.RunSudoAsync(
            ["true"],
            TimeSpan.FromSeconds(10),
            cancellationToken) != 0)
    {
      throw new CanaryScenarioFailureException(
          "linux-installed-sudo-required");
    }
    if (await HasExistingInstallationAsync(cancellationToken))
    {
      throw new CanaryScenarioFailureException(
          "linux-installed-host-not-clean");
    }

    await _pitCrewFixture.CreateAsync(cancellationToken);
    await _connectorFixture.CreateAsync(cancellationToken);
    var scenarioRoot = Path.Combine(
        _runRoot,
        "scenario",
        "linux-installed");
    Directory.CreateDirectory(scenarioRoot);
    var settingsPath = Path.Combine(
        scenarioRoot,
        "agent-settings.json");
    WriteAgentSettings(
        settingsPath,
        dashboardUrl,
        enrollmentCode);
    try
    {
      var exitCode = await RunInstallerAsync(
          [
              "-Action",
              "Install",
              "-Version",
              CandidateVersion,
              "-PitCrewRoot",
              _pitCrewFixture.Root,
              "-Profiles",
              "default",
              "-AgentSettingsPath",
              settingsPath,
              "-AgentArchivePath",
              GetArchivePath("agent"),
              "-AgentChecksumPath",
              GetChecksumPath("agent"),
              "-BrokerArchivePath",
              GetArchivePath("broker"),
              "-BrokerChecksumPath",
              GetChecksumPath("broker"),
              "-AllowMachineChanges",
          ],
          TimeSpan.FromMinutes(3),
          cancellationToken);
      if (exitCode != 0)
      {
        throw new CanaryScenarioFailureException(
            "linux-installation-rejected");
      }
      _installed = true;
    }
    finally
    {
      DeleteIfPresent(settingsPath);
    }

    await WaitForAcceptedPollAsync(cancellationToken);
    await VerifyAsync(cancellationToken);
  }

  public async Task FinalizeAndRestartAsync(
      CancellationToken cancellationToken)
  {
    var exitCode = await RunInstallerAsync(
        [
            "-Action",
            "FinalizeEnrollment",
            "-AllowMachineChanges",
        ],
        TimeSpan.FromMinutes(2),
        cancellationToken);
    if (exitCode != 0)
    {
      throw new CanaryScenarioFailureException(
          "bootstrap-finalization-rejected");
    }
    await VerifyBootstrapRemovedAsync(cancellationToken);
    await VerifyAsync(cancellationToken);
  }

  public async Task DeleteKeysAndUninstallAsync(
      CancellationToken cancellationToken)
  {
    if (!_installed)
    {
      throw new CanaryScenarioFailureException(
          "linux-installation-unavailable");
    }
    var exitCode = await RunInstallerAsync(
        [
            "-Action",
            "Uninstall",
            "-IdentityHandling",
            "DeleteKeys",
            "-AllowMachineChanges",
        ],
        TimeSpan.FromMinutes(3),
        cancellationToken);
    if (exitCode != 0)
    {
      throw new CanaryScenarioFailureException(
          "linux-uninstall-rejected");
    }
    _installed = false;
    if (await HasInstalledProductStateAsync(cancellationToken))
    {
      throw new CanaryScenarioFailureException(
          "linux-uninstall-incomplete");
    }
  }

  public async Task WaitForAcceptedPollAsync(
      CancellationToken cancellationToken)
  {
    var statusPath = Path.Combine(
        AgentStateRoot,
        "agent-startup-status.json");
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
            statusPath,
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

  public void AssertUnrelatedStateUnchanged()
  {
    _connectorFixture.AssertUnchanged();
    _pitCrewFixture.AssertUnchanged();
  }

  public async Task<string?> ReadRequestDispositionAsync(
      CancellationToken cancellationToken)
  {
    var content = await _commands.ReadPrivilegedFileAsync(
        Path.Combine(
            AgentStateRoot,
            "agent-startup-status.json"),
        allowUnavailable: true,
        cancellationToken);
    return content is null
        ? null
        : ReadRequestDisposition(content);
  }

  public async ValueTask DisposeAsync()
  {
    Exception? cleanupFailure = null;
    if (_installed)
    {
      try
      {
        var exitCode = await RunInstallerAsync(
            [
                "-Action",
                "Uninstall",
                "-IdentityHandling",
                "DeleteKeys",
                "-AllowMachineChanges",
            ],
            TimeSpan.FromMinutes(3),
            CancellationToken.None);
        if (exitCode != 0)
        {
          cleanupFailure = new CanaryScenarioFailureException(
              "linux-uninstall-rejected");
        }
      }
      catch (Exception exception)
      {
        cleanupFailure = exception;
      }
      _installed = false;
    }
    try
    {
      await _connectorFixture.DisposeAsync();
    }
    catch (Exception exception)
    {
      cleanupFailure ??= exception;
    }
    try
    {
      await _pitCrewFixture.DisposeAsync();
    }
    catch (Exception exception)
    {
      cleanupFailure ??= exception;
    }
    if (cleanupFailure is not null)
    {
      throw cleanupFailure;
    }
  }

  private async Task VerifyAsync(
      CancellationToken cancellationToken)
  {
    var exitCode = await RunInstallerAsync(
        [
            "-Action",
            "Verify",
        ],
        TimeSpan.FromMinutes(2),
        cancellationToken);
    if (exitCode != 0)
    {
      throw new CanaryScenarioFailureException(
          "linux-service-boundary-invalid");
    }
  }

  private async Task VerifyBootstrapRemovedAsync(
      CancellationToken cancellationToken)
  {
    var content = await _commands.ReadPrivilegedFileAsync(
        Path.Combine(AgentStateRoot, "appsettings.json"),
        allowUnavailable: false,
        cancellationToken) ??
        throw new CanaryScenarioFailureException(
            "bootstrap-material-retained");
    using var document = JsonDocument.Parse(content);
    var agent = document.RootElement
        .GetProperty("pitCrewSupport")
        .GetProperty("agent");
    foreach (var propertyName in new[]
    {
        "dashboardUrl",
        "tenantId",
        "displayName",
        "enrollmentCode",
    })
    {
      if (agent.TryGetProperty(propertyName, out _))
      {
        throw new CanaryScenarioFailureException(
            "bootstrap-material-retained");
      }
    }
  }

  private async Task<bool> HasExistingInstallationAsync(
      CancellationToken cancellationToken) =>
      Directory.Exists(AgentInstallRoot) ||
      Directory.Exists(BrokerInstallRoot) ||
      Directory.Exists(AgentStateRoot) ||
      Directory.Exists(BrokerStateRoot) ||
      Directory.Exists(InstallerStateRoot) ||
      Directory.Exists(LinuxCanaryConnectorFixture.Root) ||
      Directory.Exists(RuntimeRoot) ||
      File.Exists(AgentUnitPath) ||
      File.Exists(BrokerUnitPath) ||
      await _commands.AccountExistsAsync(
          "passwd",
          AgentUser,
          cancellationToken) ||
      await _commands.AccountExistsAsync(
          "passwd",
          BrokerUser,
          cancellationToken) ||
      await _commands.AccountExistsAsync(
          "group",
          AgentUser,
          cancellationToken) ||
      await _commands.AccountExistsAsync(
          "group",
          BrokerUser,
          cancellationToken) ||
      await _commands.AccountExistsAsync(
          "group",
          IpcGroup,
          cancellationToken);

  private async Task<bool> HasInstalledProductStateAsync(
      CancellationToken cancellationToken) =>
      Directory.Exists(AgentInstallRoot) ||
      Directory.Exists(BrokerInstallRoot) ||
      Directory.Exists(AgentStateRoot) ||
      Directory.Exists(BrokerStateRoot) ||
      Directory.Exists(InstallerStateRoot) ||
      Directory.Exists(RuntimeRoot) ||
      File.Exists(AgentUnitPath) ||
      File.Exists(BrokerUnitPath) ||
      await _commands.AccountExistsAsync(
          "passwd",
          AgentUser,
          cancellationToken) ||
      await _commands.AccountExistsAsync(
          "passwd",
          BrokerUser,
          cancellationToken) ||
      await _commands.AccountExistsAsync(
          "group",
          AgentUser,
          cancellationToken) ||
      await _commands.AccountExistsAsync(
          "group",
          BrokerUser,
          cancellationToken) ||
      await _commands.AccountExistsAsync(
          "group",
          IpcGroup,
          cancellationToken);

  private Task<int> RunInstallerAsync(
      IReadOnlyList<string> installerArguments,
      TimeSpan timeout,
      CancellationToken cancellationToken)
  {
    var arguments = new List<string>
    {
        "pwsh",
        "-NoProfile",
        "-File",
        _installerPath,
    };
    arguments.AddRange(installerArguments);
    return _commands.RunSudoAsync(
        arguments,
        timeout,
        cancellationToken);
  }

  private string GetArchivePath(string component) =>
      Path.Combine(
          _artifactRoot,
          "archives",
          $"pitcrew-support-{component}-{CandidateVersion}-linux-x64.tar.gz");

  private string GetChecksumPath(string component) =>
      GetArchivePath(component) + ".sha256";

  private void WriteAgentSettings(
      string path,
      string dashboardUrl,
      string enrollmentCode)
  {
    var settings = new
    {
      PitCrewSupport = new
      {
        Agent = new
        {
          IdentityRoot = Path.Combine(
              AgentStateRoot,
              "identity-state"),
          ReplayRoot = Path.Combine(
              AgentStateRoot,
              "replay"),
          PipeName = "pitcrew-support-broker-v1",
          SocketPath = "/run/pitcrew-support/broker.sock",
          DashboardUrl = dashboardUrl,
          TenantId = "local",
          DisplayName =
              SupportCanaryDashboardClient.EnrollmentDisplayName,
          EnrollmentCode = enrollmentCode,
        },
      },
    };
    File.WriteAllText(
        path,
        JsonSerializer.Serialize(
            settings,
            _jsonOptions) + "\n",
        new UTF8Encoding(false));
  }

  private static void DeleteIfPresent(string path)
  {
    if (File.Exists(path))
    {
      File.Delete(path);
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
