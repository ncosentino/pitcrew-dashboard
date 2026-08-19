using System.Reflection;
using System.Text.Json;

namespace PitCrew.Support.Broker.App;

internal static class SupportEvidencePolicy
{
  private const string ResourceName =
      "PitCrew.Support.Broker.App.support-evidence-policy-v0.10.3.json";
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
    if (policy.SchemaVersion != 2 ||
        !string.Equals(
            policy.PitCrewVersion,
            "0.10.3",
            StringComparison.Ordinal) ||
        !string.Equals(
            policy.PitCrewCommit,
            "4fbafcafca1aa659a07b2f5deb96edc5d3eb3269",
            StringComparison.Ordinal) ||
        !string.Equals(
            policy.CollectorRelativePath,
            "plugins/pitcrew-operations/skills/pitcrew-remote-diagnostics/scripts/Collect-PitCrewDiagnostics.ps1",
            StringComparison.Ordinal) ||
        !string.Equals(
            policy.CollectorSha256,
            "18ed0cdb53e288f981bf5cc49cb404a5129b98ac14faaa5a6cbcab07b3591580",
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
