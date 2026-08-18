using System.Security.Principal;

using PitCrew.Support.Broker.App;

namespace PitCrew.Support.Broker.App.Tests;

internal static class SupportBrokerTestHost
{
  public static string CreatePitCrewRoot()
  {
    var root = Path.Combine(
        AppContext.BaseDirectory,
        $"pitcrew-root-{Guid.NewGuid():N}");
    Directory.CreateDirectory(
        Path.Combine(root, ".pitcrew-state", "default"));
    foreach (var sentinel in new[]
    {
        "Setup-Runner.ps1",
        "RunnerProfiles.Functions.ps1",
        "docker-compose.yml",
    })
    {
      File.WriteAllText(Path.Combine(root, sentinel), string.Empty);
    }
    WriteCollector(root);
    return root;
  }

  public static SupportBrokerOptions CreateOptions(
      string root,
      string endpoint,
      string? expectedAgentSid = null)
  {
    if (OperatingSystem.IsWindows())
    {
      using var identity = WindowsIdentity.GetCurrent();
      var sid = identity.User?.Value ??
          throw new InvalidOperationException(
              "The test process has no Windows SID.");
      return new SupportBrokerOptions(
          root,
          ["default"],
          endpoint,
          "/unused",
          expectedAgentSid ?? sid,
          sid,
          null,
          null,
          null);
    }

    if (OperatingSystem.IsLinux())
    {
      var uid = UnixProcessIdentity.GetEffectiveUserId();
      var gid = UnixProcessIdentity.GetEffectiveGroupId();
      return new SupportBrokerOptions(
          root,
          ["default"],
          "unused",
          endpoint,
          null,
          null,
          uid,
          uid,
          gid);
    }
    throw new PlatformNotSupportedException();
  }

  public static SupportDiagnosticsBroker CreateBroker(
      SupportBrokerOptions options)
  {
    var collectorPath = Path.Combine(
        options.PitCrewRoot,
        SupportEvidencePolicy.Load().CollectorRelativePath.Replace(
            '/',
            Path.DirectorySeparatorChar));
    var collectorHash =
        SupportEvidenceAccessValidator.ComputeCollectorSha256(collectorPath);
    var policy = SupportEvidencePolicy.Load() with
    {
      CollectorSha256 = collectorHash,
    };
    return new SupportDiagnosticsBroker(options, policy);
  }

  public static string CreateSocketDirectory()
  {
    var directory = Path.Combine(
        FindRepositoryRoot(),
        $".support-socket-{Guid.NewGuid():N}"[..24]);
    Directory.CreateDirectory(directory);
    return directory;
  }

  public static void DeleteDirectory(string root)
  {
    if (Directory.Exists(root))
    {
      Directory.Delete(root, recursive: true);
    }
  }

  public static void WriteProjection(string root, string name)
  {
    File.WriteAllText(
        Path.Combine(root, ".pitcrew-state", "default", name),
        "{}");
  }

  private static void WriteCollector(string root)
  {
    var scriptPath = Path.Combine(
        root,
        "plugins",
        "pitcrew-operations",
        "skills",
        "pitcrew-remote-diagnostics",
        "scripts",
        "Collect-PitCrewDiagnostics.ps1");
    Directory.CreateDirectory(Path.GetDirectoryName(scriptPath) ?? root);
    File.WriteAllText(
        scriptPath,
        """
        param([string]$PitCrewRoot,[switch]$FileOnly,[switch]$PassThruOnly,[string]$DiagnosticMode,[string]$Profile,[string]$PackageId)
        [PSCustomObject][ordered]@{
          report = [PSCustomObject][ordered]@{
            mode = $DiagnosticMode
            profile = $Profile
            fileOnly = $FileOnly.IsPresent
          }
          markdown = '# Diagnostics'
        }
        """);
  }

  private static string FindRepositoryRoot()
  {
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null)
    {
      if (Directory.Exists(Path.Combine(directory.FullName, ".git")) ||
          File.Exists(Path.Combine(directory.FullName, ".git")))
      {
        return directory.FullName;
      }
      directory = directory.Parent;
    }
    return Environment.CurrentDirectory;
  }
}
