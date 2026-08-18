using System.Reflection;
using System.Text.Json;

namespace PitCrew.Support.Broker.App;

internal static class SupportEvidencePolicy
{
  private const string ResourceName =
      "PitCrew.Support.Broker.App.support-evidence-policy-v0.10.1.json";
  private static readonly string[] _installationSentinels =
      ["Setup-Runner.ps1", "RunnerProfiles.Functions.ps1", "docker-compose.yml"];
  private static readonly string[] _profileProjectionFiles =
  [
      "desired-capacity.json",
      "acknowledged-capacity.json",
      "static-profile.json",
      "observed-state.json",
  ];
  private static readonly string[] _connectorHealthFiles =
      ["connector-health.json", "connector-events.jsonl"];

  public static SupportEvidencePolicyDocument Load()
  {
    using var stream = Assembly.GetExecutingAssembly()
        .GetManifestResourceStream(ResourceName) ??
        throw new InvalidOperationException(
            "The embedded support evidence policy is unavailable.");
    var policy = JsonSerializer.Deserialize<SupportEvidencePolicyDocument>(
        stream,
        new JsonSerializerOptions(JsonSerializerDefaults.Web)) ??
        throw new InvalidOperationException(
            "The embedded support evidence policy is invalid.");
    if (policy.SchemaVersion != 1 ||
        !string.Equals(
            policy.PitCrewVersion,
            "0.10.1",
            StringComparison.Ordinal) ||
        !string.Equals(
            policy.PitCrewCommit,
            "0672c34c",
            StringComparison.Ordinal) ||
        !string.Equals(
            policy.CollectorRelativePath,
            "plugins/pitcrew-operations/skills/pitcrew-remote-diagnostics/scripts/Collect-PitCrewDiagnostics.ps1",
            StringComparison.Ordinal) ||
        !string.Equals(
            policy.CollectorSha256,
            "01e8fbcb54ec7f79d8403284d521c0d98956be2f4a617aa881d490b28f88e0a3",
            StringComparison.Ordinal) ||
        !string.Equals(
            policy.CollectorHashCanonicalization,
            "utf8-lf",
            StringComparison.Ordinal) ||
        !string.Equals(
            policy.ProfileStateRootAccess,
            "enumerate-profile-directories-only",
            StringComparison.Ordinal) ||
        !policy.InstallationSentinels.SequenceEqual(
            _installationSentinels,
            StringComparer.Ordinal) ||
        !policy.ProfileProjectionFiles.SequenceEqual(
            _profileProjectionFiles,
            StringComparer.Ordinal) ||
        !policy.ConnectorHealthFiles.SequenceEqual(
            _connectorHealthFiles,
            StringComparer.Ordinal))
    {
      throw new InvalidOperationException(
          "The embedded support evidence policy has an unsupported contract.");
    }
    return policy;
  }
}
