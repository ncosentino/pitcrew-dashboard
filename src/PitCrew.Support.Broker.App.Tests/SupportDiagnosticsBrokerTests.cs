using PitCrew.Support.Broker.App;
using PitCrew.Support.Protocol;

namespace PitCrew.Support.Broker.App.Tests;

public sealed class SupportDiagnosticsBrokerTests
{
  [Test]
  public async Task Broker_Rejects_Non_Allowlisted_Mode_And_Profile(
      CancellationToken cancellationToken)
  {
    var root = CreatePitCrewRoot();
    try
    {
      Directory.CreateDirectory(Path.Combine(root, "profiles", "default"));
      WriteCollector(root);
      var broker = new SupportDiagnosticsBroker(new SupportBrokerOptions(root, "unused"));

      var invalidMode = await broker.ExecuteAsync(
          new SupportBrokerRequest("Shell", "default", "package-1"),
          cancellationToken);
      var invalidProfile = await broker.ExecuteAsync(
          new SupportBrokerRequest(SupportDiagnosticModes.Full, "..\\secret", "package-1"),
          cancellationToken);

      await Assert.That(invalidMode.Status).IsEqualTo(SupportBrokerStatus.InvalidMode);
      await Assert.That(invalidProfile.Status).IsEqualTo(SupportBrokerStatus.InvalidProfile);
    }
    finally
    {
      DeleteDirectory(root);
    }
  }

  [Test]
  public async Task Broker_Executes_Only_Fixed_FileOnly_Collector(
      CancellationToken cancellationToken)
  {
    var root = CreatePitCrewRoot();
    try
    {
      Directory.CreateDirectory(Path.Combine(root, "profiles", "default"));
      WriteCollector(root);
      var broker = new SupportDiagnosticsBroker(new SupportBrokerOptions(root, "unused"));
      var result = await broker.ExecuteAsync(
          new SupportBrokerRequest(SupportDiagnosticModes.HostPressure, "default", "package-1"),
          cancellationToken);

      await Assert.That(result.Status).IsEqualTo(SupportBrokerStatus.Succeeded);
      await Assert.That(result.Response).IsNotNull();
      await Assert.That(result.Response!.Markdown).IsEqualTo("# Diagnostics");
      await Assert.That(result.Response.Report.GetProperty("mode").GetString())
          .IsEqualTo(SupportDiagnosticModes.HostPressure);
      await Assert.That(result.Response.Report.GetProperty("fileOnly").GetBoolean())
          .IsTrue()
          .Because("the broker always supplies -FileOnly to the fixed collector");
    }
    finally
    {
      DeleteDirectory(root);
    }
  }

  [Test]
  public async Task Named_Pipe_Client_Receives_Broker_Response(
      CancellationToken cancellationToken)
  {
    var root = CreatePitCrewRoot();
    try
    {
      Directory.CreateDirectory(Path.Combine(root, "profiles", "default"));
      WriteCollector(root);
      var pipeName = $"pitcrew-support-broker-test-{Guid.NewGuid():N}";
      var options = new SupportBrokerOptions(root, pipeName);
      var broker = new SupportDiagnosticsBroker(options);
      var server = new SupportBrokerPipeServer(options, broker);
      var client = new SupportBrokerPipeClient(pipeName);
      var serverTask = server.RunOnceAsync(cancellationToken);

      var result = await client.ExecuteAsync(
          new SupportBrokerRequest(SupportDiagnosticModes.Full, "default", "package-1"),
          cancellationToken);
      await serverTask;

      await Assert.That(result.Status).IsEqualTo(SupportBrokerStatus.Succeeded);
      await Assert.That(result.Response).IsNotNull();
    }
    finally
    {
      DeleteDirectory(root);
    }
  }

  private static string CreatePitCrewRoot() =>
      Path.Combine(AppContext.BaseDirectory, $"pitcrew-root-{Guid.NewGuid():N}");

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
        param([switch]$FileOnly,[switch]$PassThruOnly,[string]$Mode,[string]$ProfileId,[string]$PackageId)
        [Console]::Out.Write('{"report":{"mode":"' + $Mode + '","fileOnly":' + $FileOnly.IsPresent.ToString().ToLowerInvariant() + '},"markdown":"# Diagnostics"}')
        """);
  }

  private static void DeleteDirectory(string root)
  {
    if (Directory.Exists(root))
    {
      Directory.Delete(root, recursive: true);
    }
  }
}
