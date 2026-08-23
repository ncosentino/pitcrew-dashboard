using PitCrew.Support.Canary.Contracts;

namespace PitCrew.Support.Canary.Tests;

public sealed class CanaryManifestContractTests
{
  [Test]
  public async Task Plan_And_Runtime_Round_Trip_Strict_Contracts()
  {
    var root = Path.Combine(
        Path.GetTempPath(),
        "pitcrew-support-canary-tests",
        $"manifest-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    try
    {
      var source = new CanarySourceRevision(
          "ncosentino/pitcrew-dashboard",
          new string('a', 40));
      var plan = new CanaryPlanManifest(
          CanaryManifestFile.PlanSchemaVersion,
          Guid.NewGuid().ToString("N"),
          CanaryTopologyProfiles.Portable,
          ["topology-smoke-v1"],
          source,
          new CanarySourceRevision(
              "ncosentino/pitcrew",
              new string('b', 40)),
          DateTimeOffset.UnixEpoch);
      var planPath = Path.Combine(root, "plan.json");
      CanaryManifestFile.WritePlan(
          planPath,
          plan);
      var readPlan = CanaryManifestFile.ReadPlan(planPath);
      var runtime = new CanaryRuntimeManifest(
          CanaryManifestFile.RuntimeSchemaVersion,
          plan.RunId,
          plan.TopologyProfile,
          plan.Dashboard,
          plan.PitCrew,
          "http://localhost:5000/",
          "http://localhost:5001/",
          [
              CanaryCapabilities.DashboardHttp,
              CanaryCapabilities.RelayHttp,
          ],
          DateTimeOffset.UnixEpoch);
      var runtimePath = Path.Combine(
          root,
          "runtime.json");
      CanaryManifestFile.WriteRuntime(
          runtimePath,
          runtime);
      var readRuntime = CanaryManifestFile.ReadRuntime(
          runtimePath);

      await Assert.That(readPlan.RunId)
          .IsEqualTo(plan.RunId);
      await Assert.That(readPlan.Dashboard)
          .IsEqualTo(plan.Dashboard);
      await Assert.That(readPlan.Scenarios)
          .IsEquivalentTo(plan.Scenarios);
      await Assert.That(readRuntime.RunId)
          .IsEqualTo(runtime.RunId);
      await Assert.That(readRuntime.DashboardUrl)
          .IsEqualTo(runtime.DashboardUrl);
      await Assert.That(readRuntime.Capabilities)
          .IsEquivalentTo(runtime.Capabilities);
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }
}
