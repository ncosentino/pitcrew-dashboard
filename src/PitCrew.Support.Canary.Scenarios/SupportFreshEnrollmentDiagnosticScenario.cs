using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

using PitCrew.Support.Canary.Contracts;

namespace PitCrew.Support.Canary.Scenarios;

/// <summary>
/// Exercises fresh enrollment, polling, bootstrap finalization, signed
/// diagnostics, revocation, and local key deletion with candidate components.
/// </summary>
public sealed class SupportFreshEnrollmentDiagnosticScenario :
    ICanaryScenario
{
  private const string TenantId = "local";
  private const string ScenarioId =
      "support-fresh-enrollment-diagnostic-v1";
  private static readonly JsonSerializerOptions _jsonOptions =
      new(JsonSerializerDefaults.Web)
      {
        WriteIndented = true,
      };

  /// <inheritdoc />
  public string Id => ScenarioId;

  /// <inheritdoc />
  public IReadOnlySet<string> RequiredCapabilities { get; } =
      new HashSet<string>(
      [
          CanaryCapabilities.DashboardHttp,
          CanaryCapabilities.RelayHttp,
          CanaryCapabilities.SupportAgentProcess,
          CanaryCapabilities.SupportBrokerProcess,
          CanaryCapabilities.PitCrewFileOnlyEvidence,
      ],
      StringComparer.OrdinalIgnoreCase);

  /// <inheritdoc />
  public async Task<CanaryScenarioResult> RunAsync(
      CanaryRuntimeManifest runtime,
      CanaryScenarioContext context,
      CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(runtime);
    ArgumentNullException.ThrowIfNull(context);
    var execution = new CanaryScenarioExecution(
        runtime,
        Id,
        context.TimeProvider);
    var fixtureRoot = Path.Combine(
        context.RunRoot,
        "fixture",
        "pitcrew");
    var agentStateRoot = Path.Combine(
        context.RunRoot,
        "services",
        "agent");
    var brokerStateRoot = Path.Combine(
        context.RunRoot,
        "services",
        "broker");
    Directory.CreateDirectory(agentStateRoot);
    Directory.CreateDirectory(brokerStateRoot);
    var agentAssembly = CandidatePaths.ResolveAssembly(
        context.DashboardSourceRoot,
        "PitCrew.Support.Agent.App");
    var brokerAssembly = CandidatePaths.ResolveAssembly(
        context.DashboardSourceRoot,
        "PitCrew.Support.Broker.App");
    CandidateProcess? agent = null;
    CandidateProcess? broker = null;
    SupportCanaryDashboardClient? dashboard = null;
    string? antiforgeryToken = null;
    string? enrollmentCode = null;
    string? diagnosticCredential = null;
    Guid nodeId = default;
    IReadOnlyDictionary<string, string>? fixtureSnapshot = null;
    var pipeName = $"pitcrew-canary-{runtime.RunId}";
    var socketPath = Path.Combine(
        brokerStateRoot,
        "broker.sock");
    try
    {
      await execution.RunStepAsync(
          "validate-candidate-sources",
          _ =>
          {
            ValidateCandidateSources(
                runtime,
                context,
                fixtureRoot);
            fixtureSnapshot = SnapshotFixture(fixtureRoot);
            return Task.FromResult("candidate-contract-compatible");
          },
          cancellationToken);
      await execution.RunStepAsync(
          "create-enrollment-authorization",
          async token =>
          {
#pragma warning disable IDISP003 // The nullable slot is owned and disposed by the scenario finally block.
            dashboard = new SupportCanaryDashboardClient(
                runtime.DashboardUrl);
#pragma warning restore IDISP003
            antiforgeryToken =
                await dashboard.GetAntiforgeryTokenAsync(token);
            var authorization =
                await dashboard.CreateEnrollmentAuthorizationAsync(
                    antiforgeryToken,
                    token);
            enrollmentCode = authorization.EnrollmentCode;
            return "fresh-authorization-created";
          },
          cancellationToken);
      await execution.RunStepAsync(
          "start-diagnostics-broker",
          _ =>
          {
#pragma warning disable IDISP003 // The nullable slot is owned and disposed by the scenario finally block.
            broker = CandidateProcess.Start(
                brokerAssembly,
                brokerStateRoot,
                CreateBrokerEnvironment(
                    fixtureRoot,
                    pipeName,
                    socketPath));
#pragma warning restore IDISP003
            return Task.FromResult("candidate-broker-started");
          },
          cancellationToken);
      await execution.RunStepAsync(
          "first-accepted-poll",
          async token =>
          {
            if (string.IsNullOrWhiteSpace(enrollmentCode))
            {
              throw new CanaryScenarioFailureException(
                  "enrollment-authorization-missing");
            }
            WriteAgentSettings(
                agentStateRoot,
                runtime.DashboardUrl,
                enrollmentCode,
                pipeName,
                socketPath);
            agent = CandidateProcess.Start(
                agentAssembly,
                agentStateRoot,
                new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase));
            await WaitForAcceptedPollAsync(
                agent,
                agentStateRoot,
                token);
            nodeId = ReadNodeId(agentStateRoot);
            return "first-poll-accepted";
          },
          cancellationToken);
      await execution.RunStepAsync(
          "finalize-bootstrap-and-restart",
          async token =>
          {
            if (agent is null)
            {
              throw new CanaryScenarioFailureException(
                  "agent-process-missing");
            }
            await agent.DisposeAsync();
            agent = null;
            var exitCode = await CandidateProcess.RunAsync(
                agentAssembly,
                agentStateRoot,
                new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase),
                TimeSpan.FromSeconds(30),
                token,
                "finalize-enrollment");
            if (exitCode != 0)
            {
              throw new CanaryScenarioFailureException(
                  "bootstrap-finalization-rejected");
            }
            VerifyBootstrapRemoved(agentStateRoot);
            DeleteIfPresent(
                Path.Combine(
                    agentStateRoot,
                    "agent-startup-status.json"));
            agent = CandidateProcess.Start(
                agentAssembly,
                agentStateRoot,
                new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase));
            await WaitForAcceptedPollAsync(
                agent,
                agentStateRoot,
                token);
            return "second-poll-accepted";
          },
          cancellationToken);
      await execution.RunStepAsync(
          "complete-signed-diagnostic",
          async token =>
          {
            if (dashboard is null ||
                string.IsNullOrWhiteSpace(antiforgeryToken))
            {
              throw new CanaryScenarioFailureException(
                  "dashboard-session-missing");
            }
            var credential =
                await dashboard.CreateDiagnosticCredentialAsync(
                    antiforgeryToken,
                    context.TimeProvider.GetUtcNow().AddHours(1),
                    token);
            diagnosticCredential = credential.Value;
            await InvokePitCrewVerifierAsync(
                runtime,
                context,
                nodeId,
                diagnosticCredential,
                token);
            return "attestation-verified";
          },
          cancellationToken);
      await execution.RunStepAsync(
          "revoke-and-delete-keys",
          async token =>
          {
            if (dashboard is null ||
                string.IsNullOrWhiteSpace(antiforgeryToken) ||
                agent is null)
            {
              throw new CanaryScenarioFailureException(
                  "revocation-state-missing");
            }
            await dashboard.RevokeAsync(
                antiforgeryToken,
                nodeId,
                token);
            await agent.DisposeAsync();
            agent = null;
            WriteDeletionRequest(agentStateRoot);
            agent = CandidateProcess.Start(
                agentAssembly,
                agentStateRoot,
                new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase));
            await agent.WaitForExitAsync(
                TimeSpan.FromSeconds(30),
                token);
            VerifyDeletion(agentStateRoot);
            return "revoked-and-keys-deleted";
          },
          cancellationToken);
      await execution.RunStepAsync(
          "prove-unrelated-state-unchanged",
          _ =>
          {
            if (fixtureSnapshot is null ||
                !SnapshotsEqual(
                    fixtureSnapshot,
                    SnapshotFixture(fixtureRoot)))
            {
              throw new CanaryScenarioFailureException(
                  "pitcrew-fixture-mutated");
            }
            return Task.FromResult(
                "connector-runner-and-fixture-unchanged");
          },
          cancellationToken);
    }
    finally
    {
      dashboard?.Dispose();
      if (agent is not null)
      {
        await agent.DisposeAsync();
      }
      if (broker is not null)
      {
        await broker.DisposeAsync();
      }
    }
    return execution.Complete();
  }

  private static void ValidateCandidateSources(
      CanaryRuntimeManifest runtime,
      CanaryScenarioContext context,
      string fixtureRoot)
  {
    var policyPath = Path.Combine(
        context.DashboardSourceRoot,
        "assets",
        "support-plane",
        "support-evidence-policy-v0.10.3.json");
    using var policy = JsonDocument.Parse(
        File.ReadAllText(policyPath));
    var root = policy.RootElement;
    if (!string.Equals(
            root.GetProperty("pitCrewCommit").GetString(),
            runtime.PitCrew.Commit,
            StringComparison.Ordinal) ||
        !Directory.Exists(fixtureRoot))
    {
      throw new CanaryScenarioFailureException(
          "pitcrew-policy-incompatible");
    }
    var collectorRelativePath = root
        .GetProperty("collectorRelativePath")
        .GetString() ??
        throw new CanaryScenarioFailureException(
            "collector-policy-invalid");
    var expectedHash = root
        .GetProperty("collectorSha256")
        .GetString();
    var collectorPath = Path.Combine(
        fixtureRoot,
        collectorRelativePath.Replace(
            '/',
            Path.DirectorySeparatorChar));
    var canonical = File.ReadAllText(
            collectorPath,
            new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true))
        .Replace("\r\n", "\n", StringComparison.Ordinal)
        .Replace('\r', '\n');
    var actualHash = Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(canonical)))
        .ToLowerInvariant();
    if (!string.Equals(
        expectedHash,
        actualHash,
        StringComparison.Ordinal))
    {
      throw new CanaryScenarioFailureException(
          "collector-hash-mismatch");
    }
  }

  private static IReadOnlyDictionary<string, string>
      CreateBrokerEnvironment(
          string fixtureRoot,
          string pipeName,
          string socketPath)
  {
    var environment = new Dictionary<string, string>(
        StringComparer.OrdinalIgnoreCase)
    {
      ["PitCrewSupport__Broker__PitCrewRoot"] = fixtureRoot,
      ["PitCrewSupport__Broker__AllowedProfiles"] = "default",
      ["PitCrewSupport__Broker__PipeName"] = pipeName,
      ["PitCrewSupport__Broker__SocketPath"] = socketPath,
    };
    if (OperatingSystem.IsWindows())
    {
      using var identity = WindowsIdentity.GetCurrent();
      var sid = identity.User?.Value ??
          throw new CanaryScenarioFailureException(
              "windows-process-sid-unavailable");
      environment[
          "PitCrewSupport__Broker__ExpectedAgentSid"] = sid;
      environment[
          "PitCrewSupport__Broker__BrokerServiceSid"] = sid;
    }
    else if (OperatingSystem.IsLinux())
    {
      var (uid, gid) = ReadLinuxIdentity();
      environment[
          "PitCrewSupport__Broker__ExpectedAgentUid"] = uid;
      environment[
          "PitCrewSupport__Broker__BrokerUid"] = uid;
      environment[
          "PitCrewSupport__Broker__IpcGroupGid"] = gid;
    }
    else
    {
      throw new CanaryScenarioFailureException(
          "operating-system-unsupported");
    }
    return environment;
  }

  private static (string Uid, string Gid) ReadLinuxIdentity()
  {
    var lines = File.ReadAllLines("/proc/self/status");
    var uid = ReadLinuxIdentityValue(lines, "Uid:");
    var gid = ReadLinuxIdentityValue(lines, "Gid:");
    return (uid, gid);
  }

  private static string ReadLinuxIdentityValue(
      IEnumerable<string> lines,
      string prefix)
  {
    var line = lines.FirstOrDefault(
        candidate => candidate.StartsWith(
            prefix,
            StringComparison.Ordinal));
    var value = line?
        .Split(
            [' ', '\t'],
            StringSplitOptions.RemoveEmptyEntries)
        .ElementAtOrDefault(1);
    return uint.TryParse(
        value,
        NumberStyles.None,
        CultureInfo.InvariantCulture,
        out _)
        ? value!
        : throw new CanaryScenarioFailureException(
            "linux-process-identity-unavailable");
  }

  private static void WriteAgentSettings(
      string agentStateRoot,
      string dashboardUrl,
      string enrollmentCode,
      string pipeName,
      string socketPath)
  {
    var identityRoot = Path.Combine(
        agentStateRoot,
        "identity-state");
    var replayRoot = Path.Combine(
        agentStateRoot,
        "replay");
    var settings = new
    {
      PitCrewSupport = new
      {
        Agent = new
        {
          IdentityRoot = identityRoot,
          ReplayRoot = replayRoot,
          PipeName = pipeName,
          SocketPath = socketPath,
          DashboardUrl = dashboardUrl,
          TenantId,
          DisplayName = "Canary support node",
          EnrollmentCode = enrollmentCode,
        },
      },
    };
    var path = Path.Combine(
        agentStateRoot,
        "appsettings.json");
    File.WriteAllText(
        path,
        JsonSerializer.Serialize(
            settings,
            _jsonOptions) + "\n",
        new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true));
    if (!OperatingSystem.IsWindows())
    {
      File.SetUnixFileMode(
          path,
          UnixFileMode.UserRead |
          UnixFileMode.UserWrite);
    }
  }

  private static async Task WaitForAcceptedPollAsync(
      CandidateProcess agent,
      string agentStateRoot,
      CancellationToken cancellationToken)
  {
    var statusPath = Path.Combine(
        agentStateRoot,
        "agent-startup-status.json");
    using var timeout =
        CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
    timeout.CancelAfter(TimeSpan.FromSeconds(45));
    using var timer = new PeriodicTimer(
        TimeSpan.FromMilliseconds(200));
    try
    {
      while (await timer.WaitForNextTickAsync(timeout.Token))
      {
        if (agent.HasExited)
        {
          throw new CanaryScenarioFailureException(
              "agent-exited-before-poll");
        }
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
        if (disposition is "unhandled-exception" or
            "credential-rejected" or
            "enrollment-rejected")
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

  private static Guid ReadNodeId(string agentStateRoot)
  {
    var manifestPath = Path.Combine(
        agentStateRoot,
        "identity-state",
        "identity",
        "identity.json");
    using var document = JsonDocument.Parse(
        File.ReadAllText(manifestPath));
    return document.RootElement
        .GetProperty("nodeId")
        .GetGuid();
  }

  private static void VerifyBootstrapRemoved(
      string agentStateRoot)
  {
    using var document = JsonDocument.Parse(
        File.ReadAllText(
            Path.Combine(
                agentStateRoot,
                "appsettings.json")));
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

  private static async Task InvokePitCrewVerifierAsync(
      CanaryRuntimeManifest runtime,
      CanaryScenarioContext context,
      Guid nodeId,
      string diagnosticCredential,
      CancellationToken cancellationToken)
  {
    var scenarioRoot = Path.Combine(
        context.RunRoot,
        "scenario",
        ScenarioId);
    Directory.CreateDirectory(scenarioRoot);
    var preflightPath = Path.Combine(
        scenarioRoot,
        "preflight.json");
    await File.WriteAllTextAsync(
        preflightPath,
        JsonSerializer.Serialize(
            new
            {
              schemaVersion = 1,
              capturedAt =
                  context.TimeProvider.GetUtcNow(),
              diagnosticMode = "ConnectorOffline",
              unavailableEvidence =
                  Array.Empty<object>(),
            },
            _jsonOptions),
        cancellationToken);
    var resultPath = Path.Combine(
        scenarioRoot,
        "relay-result.json");
    var outputRoot = Path.Combine(
        scenarioRoot,
        "output");
    Directory.CreateDirectory(outputRoot);
    var wrapperPath = Path.Combine(
        context.DashboardSourceRoot,
        "scripts",
        "canary",
        "Invoke-SupportRelayScenario.ps1");
    var supportRelayScriptPath = Path.Combine(
        context.PitCrewSourceRoot,
        "plugins",
        "pitcrew-operations",
        "skills",
        "pitcrew-remote-diagnostics",
        "scripts",
        "Invoke-PitCrewSupportRelay.ps1");
    var exitCode = await CandidateProcess.RunToolAsync(
        "pwsh",
        scenarioRoot,
        new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase)
        {
          ["PITCREW_DIAGNOSTICS_CREDENTIAL"] =
              diagnosticCredential,
        },
        [
            "-NoProfile",
            "-NonInteractive",
            "-File",
            wrapperPath,
            "-SupportRelayScriptPath",
            supportRelayScriptPath,
            "-DashboardUrl",
            runtime.DashboardUrl,
            "-TenantId",
            TenantId,
            "-DashboardNodeId",
            nodeId.ToString("D"),
            "-PreflightPath",
            preflightPath,
            "-OutputDirectory",
            outputRoot,
            "-ResultPath",
            resultPath,
            "-TimeoutSeconds",
            "120",
        ],
        TimeSpan.FromSeconds(180),
        cancellationToken);
    if (exitCode != 0 ||
        !File.Exists(resultPath))
    {
      throw new CanaryScenarioFailureException(
          "pitcrew-verifier-rejected-result");
    }
    using var result = JsonDocument.Parse(
        await File.ReadAllTextAsync(
            resultPath,
            cancellationToken));
    if (!result.RootElement
            .GetProperty("completed")
            .GetBoolean() ||
        result.RootElement
            .GetProperty("status")
            .GetString() != "completed")
    {
      throw new CanaryScenarioFailureException(
          "pitcrew-verifier-incomplete");
    }
  }

  private static void WriteDeletionRequest(
      string agentStateRoot)
  {
    DeleteIfPresent(
        Path.Combine(
            agentStateRoot,
            "agent-startup-status.json"));
    File.WriteAllText(
        Path.Combine(
            agentStateRoot,
            "identity-delete-request.json"),
        "{\"schemaVersion\":1,\"operation\":\"delete-keys\"}",
        new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false));
  }

  private static void VerifyDeletion(
      string agentStateRoot)
  {
    using var document = JsonDocument.Parse(
        File.ReadAllText(
            Path.Combine(
                agentStateRoot,
                "agent-startup-status.json")));
    var status = document.RootElement;
    if (status.GetProperty("phase").GetString() !=
            "identity-removal" ||
        status.GetProperty("disposition").GetString() !=
            "delete-keys-succeeded" ||
        Directory.Exists(
            Path.Combine(
                agentStateRoot,
                "identity-state",
                "identity")))
    {
      throw new CanaryScenarioFailureException(
          "delete-keys-unconfirmed");
    }
  }

  private static IReadOnlyDictionary<string, string>
      SnapshotFixture(string root)
  {
    var snapshot = new Dictionary<string, string>(
        StringComparer.OrdinalIgnoreCase);
    var count = 0;
    foreach (var path in Directory.EnumerateFiles(
        root,
        "*",
        SearchOption.AllDirectories))
    {
      count++;
      var file = new FileInfo(path);
      if (count > 256 ||
          file.Length > 4_194_304 ||
          (file.Attributes & FileAttributes.ReparsePoint) != 0)
      {
        throw new CanaryScenarioFailureException(
            "fixture-snapshot-bound-exceeded");
      }
      snapshot[Path.GetRelativePath(root, path)] =
          Convert.ToHexString(
              SHA256.HashData(
                  File.ReadAllBytes(path)));
    }
    return snapshot;
  }

  private static bool SnapshotsEqual(
      IReadOnlyDictionary<string, string> expected,
      IReadOnlyDictionary<string, string> actual) =>
      expected.Count == actual.Count &&
      expected.All(pair =>
          actual.TryGetValue(
              pair.Key,
              out var value) &&
          string.Equals(
              pair.Value,
              value,
              StringComparison.Ordinal));

  private static void DeleteIfPresent(string path)
  {
    if (File.Exists(path))
    {
      File.Delete(path);
    }
  }
}
