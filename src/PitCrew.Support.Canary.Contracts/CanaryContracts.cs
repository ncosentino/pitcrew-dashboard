using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PitCrew.Support.Canary.Contracts;

/// <summary>
/// Names the topology profiles understood by the canary harness.
/// </summary>
public static class CanaryTopologyProfiles
{
  /// <summary>
  /// Runs Dashboard and relay under Aspire and runs the agent and broker as
  /// unprivileged candidate processes.
  /// </summary>
  public const string Portable = "portable";
}

/// <summary>
/// Names runtime capabilities that scenarios may require.
/// </summary>
public static class CanaryCapabilities
{
  /// <summary>
  /// The runtime exposes the Dashboard HTTP API.
  /// </summary>
  public const string DashboardHttp = "dashboard-http";

  /// <summary>
  /// The runtime exposes the support relay HTTP API.
  /// </summary>
  public const string RelayHttp = "relay-http";

  /// <summary>
  /// The scenario runner may launch the candidate support agent.
  /// </summary>
  public const string SupportAgentProcess = "support-agent-process";

  /// <summary>
  /// The scenario runner may launch the candidate diagnostics broker.
  /// </summary>
  public const string SupportBrokerProcess = "support-broker-process";

  /// <summary>
  /// The run contains an immutable PitCrew file-only evidence fixture.
  /// </summary>
  public const string PitCrewFileOnlyEvidence = "pitcrew-file-only-evidence";
}

/// <summary>
/// Identifies one immutable source revision used by a canary run.
/// </summary>
/// <param name="Repository">Public owner/name repository identity.</param>
/// <param name="Commit">Full lowercase Git commit SHA.</param>
public sealed record CanarySourceRevision(
    string Repository,
    string Commit);

/// <summary>
/// Describes a scaffolded canary run before topology startup.
/// </summary>
/// <param name="SchemaVersion">Plan schema version.</param>
/// <param name="RunId">Unique lowercase hexadecimal run identifier.</param>
/// <param name="TopologyProfile">Selected topology profile.</param>
/// <param name="Scenarios">Registered scenarios selected for execution.</param>
/// <param name="Dashboard">Dashboard source revision.</param>
/// <param name="PitCrew">PitCrew source revision.</param>
/// <param name="CreatedAt">UTC scaffold timestamp.</param>
public sealed record CanaryPlanManifest(
    int SchemaVersion,
    string RunId,
    string TopologyProfile,
    IReadOnlyList<string> Scenarios,
    CanarySourceRevision Dashboard,
    CanarySourceRevision PitCrew,
    DateTimeOffset CreatedAt);

/// <summary>
/// Exposes non-secret endpoints and capabilities for an active topology.
/// </summary>
/// <param name="SchemaVersion">Runtime schema version.</param>
/// <param name="RunId">Run identifier copied from the plan.</param>
/// <param name="TopologyProfile">Active topology profile.</param>
/// <param name="Dashboard">Dashboard source revision.</param>
/// <param name="PitCrew">PitCrew source revision.</param>
/// <param name="DashboardUrl">Loopback Dashboard origin.</param>
/// <param name="RelayUrl">Loopback support relay origin.</param>
/// <param name="Capabilities">Closed capability names available to scenarios.</param>
/// <param name="StartedAt">UTC topology-ready timestamp.</param>
public sealed record CanaryRuntimeManifest(
    int SchemaVersion,
    string RunId,
    string TopologyProfile,
    CanarySourceRevision Dashboard,
    CanarySourceRevision PitCrew,
    string DashboardUrl,
    string RelayUrl,
    IReadOnlyList<string> Capabilities,
    DateTimeOffset StartedAt);

/// <summary>
/// Describes one bounded scenario step.
/// </summary>
/// <param name="Name">Stable step name.</param>
/// <param name="Status">Succeeded or failed.</param>
/// <param name="Category">Closed outcome category.</param>
/// <param name="DurationMilliseconds">Measured elapsed duration.</param>
public sealed record CanaryScenarioStepResult(
    string Name,
    string Status,
    string Category,
    long DurationMilliseconds);

/// <summary>
/// Contains the redacted terminal evidence for one scenario execution.
/// </summary>
/// <param name="SchemaVersion">Scenario-result schema version.</param>
/// <param name="RunId">Run identifier copied from the runtime manifest.</param>
/// <param name="ScenarioId">Registered scenario identifier.</param>
/// <param name="TopologyProfile">Topology profile used by the scenario.</param>
/// <param name="Status">Succeeded or failed.</param>
/// <param name="FailureCategory">Bounded terminal failure category, if any.</param>
/// <param name="Steps">Ordered bounded step outcomes.</param>
/// <param name="StartedAt">UTC scenario start.</param>
/// <param name="CompletedAt">UTC scenario completion.</param>
public sealed record CanaryScenarioResult(
    int SchemaVersion,
    string RunId,
    string ScenarioId,
    string TopologyProfile,
    string Status,
    string? FailureCategory,
    IReadOnlyList<CanaryScenarioStepResult> Steps,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt);

/// <summary>
/// Reads and writes versioned canary manifests.
/// </summary>
public static class CanaryManifestFile
{
  private const int MaximumManifestBytes = 65_536;
  private const int MaximumScenarioResultBytes = 32_768;
  private const long MaximumStepDurationMilliseconds = 1_800_000;
  private static readonly FrozenSet<string> _allowedStepNames =
      new[]
      {
          "complete-signed-diagnostic",
          "create-enrollment-authorization",
          "dashboard-health",
          "finalize-bootstrap-and-restart",
          "first-accepted-poll",
          "prove-unrelated-state-unchanged",
          "relay-health",
          "revoke-and-delete-keys",
          "scenario-execution",
          "start-diagnostics-broker",
          "validate-candidate-sources",
      }.ToFrozenSet(StringComparer.Ordinal);
  private static readonly FrozenSet<string> _allowedCategories =
      new[]
      {
          "agent-broker-markdown-rejected",
          "agent-broker-report-rejected",
          "agent-credential-rejected",
          "agent-enrollment-rejected",
          "agent-envelope-payload-rejected",
          "agent-envelope-signature-rejected",
          "agent-envelope-unsupported",
          "agent-exited-before-poll",
          "agent-invalid-nonce",
          "agent-poll-timeout",
          "agent-process-missing",
          "agent-replay-pending",
          "agent-request-expired",
          "agent-request-malformed",
          "agent-request-replay",
          "agent-result-unavailable",
          "agent-session-mismatch",
          "agent-unhandled-exception",
          "agent-unsupported-capability",
          "agent-unsupported-diagnostic-mode",
          "agent-validation-rejected",
          "agent-wrong-tenant-or-node",
          "attestation-verified",
          "bootstrap-finalization-rejected",
          "bootstrap-material-retained",
          "canary-tool-timeout",
          "candidate-assembly-missing",
          "candidate-broker-started",
          "candidate-command-timeout",
          "candidate-configuration-invalid",
          "candidate-contract-compatible",
          "candidate-process-exit-timeout",
          "collector-hash-mismatch",
          "collector-policy-invalid",
          "connector-runner-and-fixture-unchanged",
          "dashboard-session-empty",
          "dashboard-session-missing",
          "dashboard-session-rejected",
          "delete-keys-unconfirmed",
          "diagnostic-credential-empty",
          "diagnostic-credential-rejected",
          "enrollment-authorization-empty",
          "enrollment-authorization-missing",
          "enrollment-authorization-rejected",
          "filesystem-forbidden",
          "filesystem-or-process-io-failed",
          "first-poll-accepted",
          "fixture-snapshot-bound-exceeded",
          "fresh-authorization-created",
          "healthy",
          "http-status-rejected",
          "http-timeout",
          "http-unavailable",
          "json-contract-invalid",
          "linux-process-identity-unavailable",
          "operating-system-unsupported",
          "pitcrew-fixture-mutated",
          "pitcrew-policy-incompatible",
          "pitcrew-verifier-incomplete",
          "pitcrew-verifier-rejected-result",
          "revocation-state-missing",
          "revoked-and-keys-deleted",
          "scenario-cancelled",
          "scenario-failed",
          "scenario-result-invalid",
          "scenario-timeout",
          "scenario-unexpected-failure",
          "second-poll-accepted",
          "step-timeout",
          "support-revocation-rejected",
          "windows-process-sid-unavailable",
      }.ToFrozenSet(StringComparer.Ordinal);

  /// <summary>
  /// Current plan schema version.
  /// </summary>
  public const int PlanSchemaVersion = 1;

  /// <summary>
  /// Current runtime schema version.
  /// </summary>
  public const int RuntimeSchemaVersion = 1;

  /// <summary>
  /// Current scenario-result schema version.
  /// </summary>
  public const int ScenarioResultSchemaVersion = 1;

  private static readonly JsonSerializerOptions _jsonOptions = new(
      JsonSerializerDefaults.Web)
  {
    WriteIndented = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
  };

  /// <summary>
  /// Reads and validates one plan manifest.
  /// </summary>
  /// <param name="path">Existing plan path.</param>
  /// <returns>The validated plan.</returns>
  /// <exception cref="InvalidDataException">
  /// The file is missing, oversized, malformed, or violates the plan contract.
  /// </exception>
  public static CanaryPlanManifest ReadPlan(string path)
  {
    var plan = Read<CanaryPlanManifest>(
        path,
        MaximumManifestBytes);
    ValidatePlan(plan);
    return plan;
  }

  /// <summary>
  /// Atomically writes a plan manifest.
  /// </summary>
  /// <param name="path">Destination path.</param>
  /// <param name="plan">Validated non-secret plan data.</param>
  public static void WritePlan(
      string path,
      CanaryPlanManifest plan)
  {
    ValidatePlan(plan);
    Write(
        path,
        plan,
        MaximumManifestBytes);
  }

  /// <summary>
  /// Reads and validates one runtime manifest.
  /// </summary>
  /// <param name="path">Existing runtime path.</param>
  /// <returns>The validated runtime manifest.</returns>
  /// <exception cref="InvalidDataException">
  /// The file is missing, oversized, malformed, or violates the runtime contract.
  /// </exception>
  public static CanaryRuntimeManifest ReadRuntime(string path)
  {
    var runtime = Read<CanaryRuntimeManifest>(
        path,
        MaximumManifestBytes);
    ValidateRuntime(runtime);
    return runtime;
  }

  /// <summary>
  /// Atomically writes a runtime manifest.
  /// </summary>
  /// <param name="path">Destination path.</param>
  /// <param name="runtime">Validated non-secret runtime data.</param>
  public static void WriteRuntime(
      string path,
      CanaryRuntimeManifest runtime)
  {
    ValidateRuntime(runtime);
    Write(
        path,
        runtime,
        MaximumManifestBytes);
  }

  /// <summary>
  /// Reads and validates one redacted scenario result.
  /// </summary>
  /// <param name="path">Existing result path.</param>
  /// <returns>The validated scenario result.</returns>
  /// <exception cref="InvalidDataException">
  /// The file is missing, oversized, malformed, or violates the result contract.
  /// </exception>
  public static CanaryScenarioResult ReadScenarioResult(
      string path)
  {
    var result = Read<CanaryScenarioResult>(
        path,
        MaximumScenarioResultBytes);
    ValidateScenarioResult(result);
    return result;
  }

  /// <summary>
  /// Atomically writes a redacted scenario result.
  /// </summary>
  /// <param name="path">Destination path.</param>
  /// <param name="result">Terminal scenario result.</param>
  public static void WriteScenarioResult(
      string path,
      CanaryScenarioResult result)
  {
    ValidateScenarioResult(result);
    Write(
        path,
        result,
        MaximumScenarioResultBytes);
  }

  private static T Read<T>(
      string path,
      long maximumBytes)
  {
    var file = new FileInfo(path);
    if (!file.Exists ||
        file.Length is <= 0 ||
        file.Length > maximumBytes ||
        (file.Attributes & FileAttributes.ReparsePoint) != 0)
    {
      throw new InvalidDataException(
          "The canary manifest file is unavailable or exceeds its bound.");
    }
    try
    {
      using var stream = new FileStream(
          file.FullName,
          FileMode.Open,
          FileAccess.Read,
          FileShare.Read,
          4096,
          FileOptions.SequentialScan);
      return JsonSerializer.Deserialize<T>(stream, _jsonOptions) ??
          throw new InvalidDataException(
              "The canary manifest file was empty.");
    }
    catch (JsonException exception)
    {
      throw new InvalidDataException(
          "The canary manifest file is malformed.",
          exception);
    }
  }

  private static void Write<T>(
      string path,
      T value,
      int maximumBytes)
  {
    var fullPath = Path.GetFullPath(path);
    var directory = Path.GetDirectoryName(fullPath) ??
        throw new InvalidDataException(
            "The canary manifest path has no parent directory.");
    Directory.CreateDirectory(directory);
    var temporaryPath = $"{fullPath}.{Guid.NewGuid():N}.tmp";
    var payload = JsonSerializer.SerializeToUtf8Bytes(
        value,
        _jsonOptions);
    if (payload.Length + 1 > maximumBytes)
    {
      throw new InvalidDataException(
          "The canary manifest exceeds its serialized size bound.");
    }
    try
    {
      using (var stream = new FileStream(
          temporaryPath,
          FileMode.CreateNew,
          FileAccess.Write,
          FileShare.None,
          4096,
          FileOptions.WriteThrough))
      {
        stream.Write(payload);
        stream.WriteByte((byte)'\n');
        stream.Flush(flushToDisk: true);
      }
      File.Move(temporaryPath, fullPath, overwrite: true);
    }
    finally
    {
      if (File.Exists(temporaryPath))
      {
        File.Delete(temporaryPath);
      }
    }
  }

  private static void ValidatePlan(CanaryPlanManifest plan)
  {
    if (plan is null ||
        plan.SchemaVersion != PlanSchemaVersion ||
        !IsRunId(plan.RunId) ||
        plan.TopologyProfile != CanaryTopologyProfiles.Portable ||
        plan.Scenarios is null ||
        plan.Scenarios.Count is < 1 or > 16 ||
        plan.Scenarios.Any(scenario => !IsIdentifier(scenario, 96)) ||
        plan.Scenarios.Distinct(StringComparer.Ordinal).Count() !=
            plan.Scenarios.Count ||
        !IsSourceRevision(plan.Dashboard) ||
        !IsSourceRevision(plan.PitCrew) ||
        !IsTimestamp(plan.CreatedAt))
    {
      throw new InvalidDataException(
          "The canary plan manifest is invalid.");
    }
  }

  private static void ValidateRuntime(CanaryRuntimeManifest runtime)
  {
    if (runtime is null ||
        runtime.SchemaVersion != RuntimeSchemaVersion ||
        !IsRunId(runtime.RunId) ||
        runtime.TopologyProfile != CanaryTopologyProfiles.Portable ||
        !IsSourceRevision(runtime.Dashboard) ||
        !IsSourceRevision(runtime.PitCrew) ||
        !IsLoopbackOrigin(runtime.DashboardUrl) ||
        !IsLoopbackOrigin(runtime.RelayUrl) ||
        runtime.Capabilities is null ||
        runtime.Capabilities.Count is < 2 or > 16 ||
        runtime.Capabilities.Any(capability => !IsIdentifier(capability, 64)) ||
        runtime.Capabilities.Distinct(StringComparer.Ordinal).Count() !=
            runtime.Capabilities.Count ||
        !IsTimestamp(runtime.StartedAt))
    {
      throw new InvalidDataException(
          "The canary runtime manifest is invalid.");
    }
  }

  private static void ValidateScenarioResult(
      CanaryScenarioResult result)
  {
    if (result is null ||
        result.SchemaVersion != ScenarioResultSchemaVersion ||
        !IsRunId(result.RunId) ||
        !IsIdentifier(result.ScenarioId, 96) ||
        result.TopologyProfile != CanaryTopologyProfiles.Portable ||
        result.Status is not ("succeeded" or "failed") ||
        result.Steps is null ||
        result.Steps.Count is < 1 or > 64 ||
        !IsTimestamp(result.StartedAt) ||
        !IsTimestamp(result.CompletedAt) ||
        result.CompletedAt < result.StartedAt ||
        result.CompletedAt - result.StartedAt > TimeSpan.FromHours(2) ||
        result.Steps.Any(step => !IsValidStep(step)) ||
        result.Steps.Sum(step => step.DurationMilliseconds) >
            (long)TimeSpan.FromHours(2).TotalMilliseconds)
    {
      throw new InvalidDataException(
          "The canary scenario result is invalid.");
    }
    if (result.Status == "succeeded")
    {
      if (result.FailureCategory is not null ||
          result.Steps.Any(step => step.Status != "succeeded"))
      {
        throw new InvalidDataException(
            "A successful canary result contains failure evidence.");
      }
      return;
    }
    if (result.FailureCategory is null ||
        !_allowedCategories.Contains(result.FailureCategory) ||
        !result.Steps.Any(step =>
            step.Status == "failed" &&
            string.Equals(
                step.Category,
                result.FailureCategory,
                StringComparison.Ordinal)))
    {
      throw new InvalidDataException(
          "A failed canary result omits its bounded terminal evidence.");
    }
  }

  private static bool IsValidStep(
      CanaryScenarioStepResult? step) =>
      step is not null &&
      _allowedStepNames.Contains(step.Name) &&
      step.Status is "succeeded" or "failed" &&
      _allowedCategories.Contains(step.Category) &&
      step.DurationMilliseconds is >= 0 and
          <= MaximumStepDurationMilliseconds;

  private static bool IsSourceRevision(
      CanarySourceRevision? revision) =>
      revision is not null &&
      revision.Repository is
          "ncosentino/pitcrew" or
          "ncosentino/pitcrew-dashboard" &&
      revision.Commit.Length == 40 &&
      revision.Commit.All(character =>
          character is >= '0' and <= '9' or >= 'a' and <= 'f');

  private static bool IsRunId(string? value) =>
      value is not null &&
      value.Length == 32 &&
      value.All(character =>
          character is >= '0' and <= '9' or >= 'a' and <= 'f');

  private static bool IsIdentifier(
      string? value,
      int maximumLength) =>
      value is not null &&
      value.Length is > 0 &&
      value.Length <= maximumLength &&
      value[0] is >= 'a' and <= 'z' &&
      value.All(character =>
          character is >= 'a' and <= 'z' or
          >= '0' and <= '9' or
          '-' or
          '.');

  private static bool IsLoopbackOrigin(string? value) =>
      value is not null &&
      Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
      uri.IsLoopback &&
      uri.Scheme == Uri.UriSchemeHttp &&
      string.IsNullOrEmpty(uri.UserInfo) &&
      string.IsNullOrEmpty(uri.Query) &&
      string.IsNullOrEmpty(uri.Fragment) &&
      uri.AbsolutePath == "/";

  private static bool IsTimestamp(
      DateTimeOffset value) =>
      value != default;
}
