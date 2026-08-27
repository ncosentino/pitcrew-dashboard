using System.Reflection;
using System.Text.Json;

namespace PitCrew.Support.Broker.App;

internal static class SupportEvidencePolicy
{
  private const string ResourceName =
      "PitCrew.Support.Broker.App.support-evidence-policy-v0.10.10.json";
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
  [
      "connector-health.json",
      "connector-events.jsonl",
      "connector-health-acknowledgement.json",
  ];

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
    if (policy.SchemaVersion != 3 ||
        !string.Equals(
            policy.PitCrewVersion,
            "0.10.10",
            StringComparison.Ordinal) ||
        !string.Equals(
            policy.PitCrewCommit,
            "85dc9abfa75d6c7f596279637c3b5736931b3575",
            StringComparison.Ordinal) ||
        !string.Equals(
            policy.CollectorRelativePath,
            "plugins/pitcrew-operations/skills/pitcrew-remote-diagnostics/scripts/Collect-PitCrewDiagnostics.ps1",
            StringComparison.Ordinal) ||
        !string.Equals(
            policy.CollectorSha256,
            "898efd916da0b81dea49c62f8dd31d62ab5995620cb783d2296bb30e9132bcbf",
            StringComparison.Ordinal) ||
        !string.Equals(
            policy.CollectorHashCanonicalization,
            "utf8-lf",
            StringComparison.Ordinal) ||
        !string.Equals(
            policy.ProfileStateRootAccess,
            "enumerate-profile-directories-only",
            StringComparison.Ordinal) ||
        !string.Equals(
            policy.ProfileEvidenceDirectory,
            "support-evidence",
            StringComparison.Ordinal) ||
        policy.MaximumPersistentDirectoryEntries != 32 ||
        policy.MaximumTransientDirectoryEntries != 256 ||
        !string.Equals(
            policy.WindowsEvidenceInheritance,
            "object-inherit-read-ace",
            StringComparison.Ordinal) ||
        !string.Equals(
            policy.LinuxEvidenceInheritance,
            "directory-read-and-default-file-read-acl",
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
