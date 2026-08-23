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

  [Test]
  public async Task Scenario_Result_Rejects_Unbounded_Or_Incoherent_Evidence()
  {
    var root = Path.Combine(
        Path.GetTempPath(),
        "pitcrew-support-canary-tests",
        $"result-contract-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    var path = Path.Combine(root, "result.json");
    try
    {
      var valid = CreateFailedResult();

      await Assert.That(
              () => CanaryManifestFile.WriteScenarioResult(
                  path,
                  valid with
                  {
                    FailureCategory = "secret-value",
                    Steps =
                    [
                        valid.Steps[0] with
                          {
                            Category = "secret-value",
                          },
                    ],
                  }))
          .Throws<InvalidDataException>();
      await Assert.That(
              () => CanaryManifestFile.WriteScenarioResult(
                  path,
                  valid with
                  {
                    Steps =
                    [
                        valid.Steps[0] with
                          {
                            DurationMilliseconds =
                                1_800_001,
                          },
                    ],
                  }))
          .Throws<InvalidDataException>();
      await Assert.That(
              () => CanaryManifestFile.WriteScenarioResult(
                  path,
                  valid with
                  {
                    CompletedAt =
                        valid.StartedAt.AddSeconds(-1),
                  }))
          .Throws<InvalidDataException>();
      await Assert.That(
              () => CanaryManifestFile.WriteScenarioResult(
                  path,
                  valid with
                  {
                    Status = "succeeded",
                  }))
          .Throws<InvalidDataException>();
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  [Test]
  public async Task Manifest_Reads_Translate_Missing_And_Oversized_Data()
  {
    var root = Path.Combine(
        Path.GetTempPath(),
        "pitcrew-support-canary-tests",
        $"invalid-contract-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    var planPath = Path.Combine(root, "plan.json");
    var resultPath = Path.Combine(root, "result.json");
    try
    {
      await File.WriteAllTextAsync(
          planPath,
          """
            {
              "schemaVersion": 1,
              "runId": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
              "topologyProfile": "portable"
            }
            """);
      await File.WriteAllTextAsync(
          resultPath,
          new string('x', 32_769));

      await Assert.That(
              () => CanaryManifestFile.ReadPlan(planPath))
          .Throws<InvalidDataException>();
      await Assert.That(
              () => CanaryManifestFile.ReadScenarioResult(
                  resultPath))
          .Throws<InvalidDataException>();
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  [Test]
  public async Task Complete_Support_Result_Uses_Closed_Evidence_Vocabulary()
  {
    var timestamp = DateTimeOffset.UnixEpoch.AddSeconds(1);
    var steps = new[]
    {
        new CanaryScenarioStepResult(
            "validate-candidate-sources",
            "succeeded",
            "candidate-contract-compatible",
            1),
        new CanaryScenarioStepResult(
            "create-enrollment-authorization",
            "succeeded",
            "fresh-authorization-created",
            1),
        new CanaryScenarioStepResult(
            "start-diagnostics-broker",
            "succeeded",
            "candidate-broker-started",
            1),
        new CanaryScenarioStepResult(
            "first-accepted-poll",
            "succeeded",
            "first-poll-accepted",
            1),
        new CanaryScenarioStepResult(
            "finalize-bootstrap-and-restart",
            "succeeded",
            "second-poll-accepted",
            1),
        new CanaryScenarioStepResult(
            "complete-signed-diagnostic",
            "succeeded",
            "attestation-verified",
            1),
        new CanaryScenarioStepResult(
            "revoke-and-delete-keys",
            "succeeded",
            "revoked-and-keys-deleted",
            1),
        new CanaryScenarioStepResult(
            "prove-unrelated-state-unchanged",
            "succeeded",
            "connector-runner-and-fixture-unchanged",
            1),
    };
    var result = new CanaryScenarioResult(
        CanaryManifestFile.ScenarioResultSchemaVersion,
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
        "support-fresh-enrollment-diagnostic-v1",
        CanaryTopologyProfiles.Portable,
        "succeeded",
        null,
        steps,
        timestamp,
        timestamp.AddMilliseconds(steps.Length));
    var root = Path.Combine(
        Path.GetTempPath(),
        "pitcrew-support-canary-tests",
        $"complete-result-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    var path = Path.Combine(root, "result.json");
    try
    {
      CanaryManifestFile.WriteScenarioResult(
          path,
          result);
      var read = CanaryManifestFile.ReadScenarioResult(path);

      await Assert.That(read.Steps.Select(step => step.Name))
          .IsEquivalentTo(steps.Select(step => step.Name));
      await Assert.That(read.Status)
          .IsEqualTo("succeeded");
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  private static CanaryScenarioResult CreateFailedResult()
  {
    var timestamp = DateTimeOffset.UnixEpoch.AddSeconds(1);
    return new CanaryScenarioResult(
        CanaryManifestFile.ScenarioResultSchemaVersion,
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
        "topology-smoke-v1",
        CanaryTopologyProfiles.Portable,
        "failed",
        "scenario-timeout",
        [
            new CanaryScenarioStepResult(
                  "scenario-execution",
                  "failed",
                  "scenario-timeout",
                  100),
        ],
        timestamp,
        timestamp.AddMilliseconds(100));
  }
}
