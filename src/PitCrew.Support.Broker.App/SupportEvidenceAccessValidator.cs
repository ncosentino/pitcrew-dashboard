using System.Security.Cryptography;
using System.Text;

namespace PitCrew.Support.Broker.App;

internal sealed class SupportEvidenceAccessValidator
{
  private const int MaximumEvidenceDirectoryEntries = 32;
  private readonly SupportBrokerOptions _options;
  private readonly SupportEvidencePolicyDocument _policy;

  public SupportEvidenceAccessValidator(
      SupportBrokerOptions options,
      SupportEvidencePolicyDocument policy)
  {
    _options = options;
    _policy = policy;
  }

  public SupportEvidenceValidation Validate(string? requestedProfileId)
  {
    var profileId = ResolveProfile(requestedProfileId);
    if (profileId is null)
    {
      return new SupportEvidenceValidation(
          SupportBrokerStatus.InvalidProfile,
          null,
          null,
          "Profile ID is not locally allowlisted.");
    }

    try
    {
      var pitCrewRoot = Path.GetFullPath(_options.PitCrewRoot);
      if (!Directory.Exists(pitCrewRoot) || IsLinked(pitCrewRoot))
      {
        return InvalidInstallation();
      }
      foreach (var sentinel in _policy.InstallationSentinels)
      {
        var sentinelPath = ResolveChild(pitCrewRoot, sentinel);
        if (!File.Exists(sentinelPath) || IsLinked(sentinelPath))
        {
          return InvalidInstallation();
        }
      }

      var collectorPath = ResolveChild(
          pitCrewRoot,
          _policy.CollectorRelativePath);
      if (!File.Exists(collectorPath) ||
          HasLinkedComponent(pitCrewRoot, collectorPath))
      {
        return new SupportEvidenceValidation(
            SupportBrokerStatus.ScriptMissing,
            profileId,
            null,
            "The fixed diagnostics collector is not installed.");
      }

      var stateRoot = ResolveChild(pitCrewRoot, ".pitcrew-state");
      if (!Directory.Exists(stateRoot) ||
          HasLinkedComponent(pitCrewRoot, stateRoot))
      {
        return new SupportEvidenceValidation(
            SupportBrokerStatus.InvalidProfile,
            null,
            null,
            "Profile ID is not locally configured.");
      }
      VerifyDirectoryEnumeration(stateRoot);
      var profileRoot = ResolveChild(stateRoot, profileId);
      if (!Directory.Exists(profileRoot) ||
          HasLinkedComponent(pitCrewRoot, profileRoot))
      {
        return new SupportEvidenceValidation(
            SupportBrokerStatus.InvalidProfile,
            null,
            null,
            "Profile ID is not locally configured.");
      }
      var evidenceRoot = ResolveChild(
          profileRoot,
          _policy.ProfileEvidenceDirectory);
      if (!Directory.Exists(evidenceRoot) ||
          HasLinkedComponent(profileRoot, evidenceRoot))
      {
        throw new IOException(
            "The dedicated support evidence projection is unavailable.");
      }
      VerifyDedicatedEvidenceDirectory(
          evidenceRoot,
          _policy.ProfileProjectionFiles);

      VerifyReadable(collectorPath, required: true);
      VerifyCollectorHash(collectorPath, _policy.CollectorSha256);
      foreach (var fileName in _policy.ProfileProjectionFiles)
      {
        var evidencePath = ResolveChild(evidenceRoot, fileName);
        if (HasLinkedComponent(evidenceRoot, evidencePath))
        {
          throw new IOException(
              "Linked support evidence is prohibited.");
        }
        VerifyReadable(
            evidencePath,
            required: false);
      }
      var healthRoot = OperatingSystem.IsWindows()
          ? Path.Combine(
              Environment.GetFolderPath(
                  Environment.SpecialFolder.CommonApplicationData),
              "PitCrew",
              "Connector",
              "health")
          : "/var/lib/pitcrew-connector/health";
      var healthAnchor = Path.GetPathRoot(healthRoot) ??
          throw new IOException(
              "The connector-health root is not locally anchored.");
      if (HasLinkedComponent(healthAnchor, healthRoot))
      {
        throw new IOException(
            "Linked connector-health evidence is prohibited.");
      }
      if (Directory.Exists(healthRoot))
      {
        VerifyDedicatedEvidenceDirectory(
            healthRoot,
            _policy.ConnectorHealthFiles);
      }
      foreach (var fileName in _policy.ConnectorHealthFiles)
      {
        var healthPath = ResolveChild(healthRoot, fileName);
        if (HasLinkedComponent(healthAnchor, healthPath))
        {
          throw new IOException(
              "Linked connector-health evidence is prohibited.");
        }
        VerifyReadable(
            healthPath,
            required: false);
      }
      return new SupportEvidenceValidation(
          SupportBrokerStatus.Succeeded,
          profileId,
          collectorPath,
          null);
    }
    catch (UnauthorizedAccessException)
    {
      return AccessDenied(profileId);
    }
    catch (IOException)
    {
      return AccessDenied(profileId);
    }
  }

  private string? ResolveProfile(string? requestedProfileId)
  {
    if (requestedProfileId is null)
    {
      return _options.AllowedProfiles.Count == 1
          ? _options.AllowedProfiles[0]
          : null;
    }
    return _options.AllowedProfiles.Contains(
        requestedProfileId,
        StringComparer.Ordinal)
        ? requestedProfileId
        : null;
  }

  private static SupportEvidenceValidation AccessDenied(string profileId) =>
      new(
          SupportBrokerStatus.EvidenceAccessDenied,
          profileId,
          null,
          "Support evidence ACL drift prevents the broker from reading the exact allowlist.");

  private static SupportEvidenceValidation InvalidInstallation() =>
      new(
          SupportBrokerStatus.ExecutionFailed,
          null,
          null,
          "PitCrewRoot does not match the supported v0.10.3 installation contract.");

  private static void VerifyDedicatedEvidenceDirectory(
      string root,
      IReadOnlyList<string> allowedFiles)
  {
    var comparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
    var entryCount = 0;
    foreach (var path in Directory.EnumerateFileSystemEntries(root))
    {
      entryCount++;
      if (entryCount > MaximumEvidenceDirectoryEntries)
      {
        throw new IOException(
            "A dedicated support evidence directory exceeds its entry limit.");
      }
      var name = Path.GetFileName(path);
      var temporary = name.StartsWith(
              '.',
              StringComparison.Ordinal) &&
          name.EndsWith(
              ".tmp",
              StringComparison.Ordinal);
      FileAttributes attributes;
      try
      {
        attributes = File.GetAttributes(path);
      }
      catch (FileNotFoundException) when (temporary)
      {
        continue;
      }
      catch (DirectoryNotFoundException) when (temporary)
      {
        continue;
      }
      if ((attributes & FileAttributes.ReparsePoint) != 0 ||
          (attributes & FileAttributes.Directory) != 0)
      {
        throw new IOException(
            "A dedicated support evidence directory contains an unsupported entry.");
      }
      var allowed = allowedFiles.Any(
          candidate => string.Equals(candidate, name, comparison));
      if (!allowed && !temporary)
      {
        throw new IOException(
            "A dedicated support evidence directory contains an unexpected persistent file.");
      }
    }
  }

  private static string ResolveChild(string root, string relativePath)
  {
    var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
    var candidate = Path.GetFullPath(
        relativePath.Replace('/', Path.DirectorySeparatorChar),
        fullRoot);
    var comparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
    var prefix = fullRoot + Path.DirectorySeparatorChar;
    if (!candidate.StartsWith(prefix, comparison))
    {
      throw new IOException("The support evidence path escaped its local root.");
    }
    return candidate;
  }

  private static bool HasLinkedComponent(string root, string target)
  {
    var relative = Path.GetRelativePath(root, target);
    var current = root;
    foreach (var segment in relative.Split(
        [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
        StringSplitOptions.RemoveEmptyEntries))
    {
      current = Path.Combine(current, segment);
      if ((File.Exists(current) || Directory.Exists(current)) &&
          IsLinked(current))
      {
        return true;
      }
    }
    return false;
  }

  private static bool IsLinked(string path) =>
      (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

  private static void VerifyReadable(string path, bool required)
  {
    if (!File.Exists(path))
    {
      if (required)
      {
        throw new IOException("Required support evidence is absent.");
      }
      return;
    }
    if (IsLinked(path))
    {
      throw new IOException("Linked support evidence is prohibited.");
    }
    using var stream = new FileStream(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.ReadWrite | FileShare.Delete,
        bufferSize: 1,
        FileOptions.SequentialScan);
  }

  private static void VerifyDirectoryEnumeration(string path)
  {
    foreach (var _ in Directory.EnumerateDirectories(path))
    {
    }
  }

  private static void VerifyCollectorHash(
      string collectorPath,
      string expectedSha256)
  {
    var actual = ComputeCollectorSha256(collectorPath);
    if (!string.Equals(actual, expectedSha256, StringComparison.Ordinal))
    {
      throw new IOException(
          "The fixed diagnostics collector hash does not match the packaged policy.");
    }
  }

  internal static string ComputeCollectorSha256(string collectorPath)
  {
    var text = File.ReadAllText(
        collectorPath,
        new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true));
    var canonical = text
        .Replace("\r\n", "\n", StringComparison.Ordinal)
        .Replace('\r', '\n');
    return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
        .ToLowerInvariant();
  }
}
