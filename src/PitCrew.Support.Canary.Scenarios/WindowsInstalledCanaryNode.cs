using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace PitCrew.Support.Canary.Scenarios;

internal sealed class WindowsInstalledCanaryNode : IAsyncDisposable
{
  private const string CandidateVersion = "0.0.0-canary";
  private const string AgentServiceName = "PitCrewSupportAgent";
  private const string BrokerServiceName = "PitCrewSupportBroker";
  private readonly CanaryScenarioContext _context;
  private readonly string _fixtureRoot;
  private readonly string _installerPath;
  private readonly string _artifactRoot;
  private readonly string _connectorHealthRoot;
  private readonly IReadOnlyDictionary<string, string> _emptyEnvironment =
      new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
  private IReadOnlyDictionary<string, string>? _connectorSnapshot;
  private bool _installed;
  private bool _connectorFixtureCreated;

  public WindowsInstalledCanaryNode(
      CanaryScenarioContext context,
      string fixtureRoot)
  {
    if (!OperatingSystem.IsWindows())
    {
      throw new CanaryScenarioFailureException(
          "windows-installed-platform-required");
    }
    using var identity = WindowsIdentity.GetCurrent();
    var principal = new WindowsPrincipal(identity);
    if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
    {
      throw new CanaryScenarioFailureException(
          "windows-installed-administrator-required");
    }

    _context = context;
    _fixtureRoot = fixtureRoot;
    var commonData = Environment.GetFolderPath(
        Environment.SpecialFolder.CommonApplicationData);
    AgentStateRoot = Path.Combine(
        commonData,
        "PitCrew",
        "Support",
        "Agent");
    _connectorHealthRoot = Path.Combine(
        commonData,
        "PitCrew",
        "Connector",
        "health");
    _artifactRoot = Path.Combine(
        context.RunRoot,
        "artifacts",
        "support-plane");
    _installerPath = Path.Combine(
        _artifactRoot,
        "publish",
        "installer-win-x64",
        "Install-PitCrewSupportPlane.ps1");
  }

  public string AgentStateRoot { get; }

  public async Task InstallAsync(
      string dashboardUrl,
      string enrollmentCode,
      CancellationToken cancellationToken)
  {
    if (!File.Exists(_installerPath))
    {
      throw new CanaryScenarioFailureException(
          "windows-installed-artifact-missing");
    }
    if (Directory.Exists(_connectorHealthRoot))
    {
      throw new CanaryScenarioFailureException(
          "windows-installed-host-not-clean");
    }

    CreateConnectorFixture();
    var scenarioRoot = Path.Combine(
        _context.RunRoot,
        "scenario",
        "windows-installed");
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
              _fixtureRoot,
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
            "windows-installation-rejected");
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
    await VerifyAsync(cancellationToken);
  }

  public async Task DeleteKeysAndUninstallAsync(
      CancellationToken cancellationToken)
  {
    if (!_installed)
    {
      throw new CanaryScenarioFailureException(
          "windows-installation-unavailable");
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
          "windows-uninstall-rejected");
    }
    _installed = false;
    if (Directory.Exists(AgentStateRoot) ||
        await ServiceExistsAsync(
            AgentServiceName,
            cancellationToken) ||
        await ServiceExistsAsync(
            BrokerServiceName,
            cancellationToken))
    {
      throw new CanaryScenarioFailureException(
          "windows-uninstall-incomplete");
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
        TimeSpan.FromMilliseconds(250));
    try
    {
      while (await timer.WaitForNextTickAsync(timeout.Token))
      {
        if (!File.Exists(statusPath))
        {
          continue;
        }
        using var document = JsonDocument.Parse(
            await File.ReadAllTextAsync(
                statusPath,
                timeout.Token));
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
    if (_connectorSnapshot is null ||
        !SnapshotsEqual(
            _connectorSnapshot,
            SnapshotFiles(_connectorHealthRoot)))
    {
      throw new CanaryScenarioFailureException(
          "connector-health-fixture-mutated");
    }
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
              "windows-uninstall-rejected");
        }
      }
      catch (Exception exception)
      {
        cleanupFailure = exception;
      }
      _installed = false;
    }
    if (_connectorFixtureCreated &&
        Directory.Exists(_connectorHealthRoot))
    {
      Directory.Delete(
          _connectorHealthRoot,
          recursive: true);
      _connectorFixtureCreated = false;
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
          "windows-service-boundary-invalid");
    }
  }

  private async Task<int> RunInstallerAsync(
      IReadOnlyList<string> installerArguments,
      TimeSpan timeout,
      CancellationToken cancellationToken)
  {
    var arguments = new List<string>
    {
        "-NoProfile",
        "-File",
        _installerPath,
    };
    arguments.AddRange(installerArguments);
    return await CandidateProcess.RunToolAsync(
        "pwsh",
        Path.GetDirectoryName(_installerPath)!,
        _emptyEnvironment,
        arguments,
        timeout,
        cancellationToken);
  }

  private string GetArchivePath(string component) =>
      Path.Combine(
          _artifactRoot,
          "archives",
          $"pitcrew-support-{component}-{CandidateVersion}-win-x64.tar.gz");

  private string GetChecksumPath(string component) =>
      GetArchivePath(component) + ".sha256";

  private void CreateConnectorFixture()
  {
    Directory.CreateDirectory(_connectorHealthRoot);
    File.WriteAllText(
        Path.Combine(
            _connectorHealthRoot,
            "connector-health.json"),
        "{}",
        new UTF8Encoding(false));
    File.WriteAllText(
        Path.Combine(
            _connectorHealthRoot,
            "connector-events.jsonl"),
        string.Empty,
        new UTF8Encoding(false));
    File.WriteAllText(
        Path.Combine(
            _connectorHealthRoot,
            "connector-health-acknowledgement.json"),
        """
        {"schemaVersion":1,"updatedAt":"2026-01-01T00:00:00Z","eventIds":[]}
        """,
        new UTF8Encoding(false));
    _connectorFixtureCreated = true;
    _connectorSnapshot = SnapshotFiles(_connectorHealthRoot);
  }

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
          DisplayName = "Canary Windows support node",
          EnrollmentCode = enrollmentCode,
        },
      },
    };
    File.WriteAllText(
        path,
        JsonSerializer.Serialize(
            settings,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
              WriteIndented = true,
            }) + "\n",
        new UTF8Encoding(false));
  }

  private static IReadOnlyDictionary<string, string> SnapshotFiles(
      string root) =>
      Directory.EnumerateFiles(
              root,
              "*",
              SearchOption.TopDirectoryOnly)
          .ToDictionary(
              path => Path.GetFileName(path)!,
              path => Convert.ToHexString(
                  System.Security.Cryptography.SHA256.HashData(
                      File.ReadAllBytes(path))),
              StringComparer.OrdinalIgnoreCase);

  private static bool SnapshotsEqual(
      IReadOnlyDictionary<string, string> expected,
      IReadOnlyDictionary<string, string> actual) =>
      expected.Count == actual.Count &&
      expected.All(pair =>
          actual.TryGetValue(pair.Key, out var value) &&
          string.Equals(
              pair.Value,
              value,
              StringComparison.Ordinal));

  private static async Task<bool> ServiceExistsAsync(
      string serviceName,
      CancellationToken cancellationToken)
  {
    var startInfo = new ProcessStartInfo
    {
      FileName = "sc.exe",
      UseShellExecute = false,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
    };
    startInfo.ArgumentList.Add("query");
    startInfo.ArgumentList.Add(serviceName);
    using var process = Process.Start(startInfo) ??
        throw new IOException(
            "Windows service inspection did not start.");
    await process.WaitForExitAsync(cancellationToken);
    return process.ExitCode == 0;
  }

  private static void DeleteIfPresent(string path)
  {
    if (File.Exists(path))
    {
      File.Delete(path);
    }
  }
}
