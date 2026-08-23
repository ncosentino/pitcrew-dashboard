using PitCrew.Support.Canary.Contracts;
using PitCrew.Support.Canary.Scenarios;

namespace PitCrew.Support.Canary.Tests;

public sealed class CanaryScenarioExecutorTests
{
  [Test]
  public async Task Timeout_Cancels_Cleanup_And_Returns_Bounded_Result(
      CancellationToken cancellationToken)
  {
    var scenario = new CancellingCanaryScenario();
    var runtime = CreateRuntime();
    var context = CreateContext();

    var result = await CanaryScenarioExecutor.RunAsync(
        scenario,
        runtime,
        context,
        TimeSpan.FromMilliseconds(50),
        cancellationToken);

    await Assert.That(result.Status)
        .IsEqualTo("failed");
    await Assert.That(result.FailureCategory)
        .IsEqualTo("scenario-timeout");
    await Assert.That(result.Steps)
        .Count()
        .IsEqualTo(1);
    await Assert.That(scenario.CleanupObserved.IsCompleted)
        .IsTrue()
        .Because("timeout cancellation must reach scenario cleanup before evidence is returned");
  }

  [Test]
  public async Task Unexpected_Exception_Returns_No_Exception_Content(
      CancellationToken cancellationToken)
  {
    var runtime = CreateRuntime();
    var context = CreateContext();
    var result = await CanaryScenarioExecutor.RunAsync(
        new ThrowingCanaryScenario(),
        runtime,
        context,
        TimeSpan.FromSeconds(1),
        cancellationToken);
    var root = Path.Combine(
        Path.GetTempPath(),
        "pitcrew-support-canary-tests",
        $"unexpected-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    var path = Path.Combine(root, "result.json");
    try
    {
      CanaryManifestFile.WriteScenarioResult(
          path,
          result);
      var json = await File.ReadAllTextAsync(
          path,
          cancellationToken);

      await Assert.That(result.FailureCategory)
          .IsEqualTo("scenario-unexpected-failure");
      await Assert.That(json)
          .DoesNotContain("credential-value");
      await Assert.That(json)
          .DoesNotContain("C:\\");
      await Assert.That(json)
          .DoesNotContain("stack");
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  [Test]
  public async Task Runner_Writes_Bounded_Timeout_And_Unexpected_Results(
      CancellationToken cancellationToken)
  {
    var root = Path.Combine(
        Path.GetTempPath(),
        "pitcrew-support-canary-tests",
        $"runner-failure-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    try
    {
      var runtime = CreateRuntime();
      var context = CreateContext() with
      {
        RunRoot = root,
      };
      var timeoutScenario = new CancellingCanaryScenario();
      var timeoutExit = await CanaryRunnerProgram.ExecuteScenarioAsync(
          timeoutScenario,
          runtime,
          context,
          TimeSpan.FromMilliseconds(50),
          cancellationToken);
      var unexpectedScenario = new ThrowingCanaryScenario();
      var unexpectedExit =
          await CanaryRunnerProgram.ExecuteScenarioAsync(
              unexpectedScenario,
              runtime,
              context,
              TimeSpan.FromSeconds(1),
              cancellationToken);
      var timeoutResult =
          CanaryManifestFile.ReadScenarioResult(
              Path.Combine(
                  root,
                  "evidence",
                  $"{timeoutScenario.Id}.json"));
      var unexpectedResult =
          CanaryManifestFile.ReadScenarioResult(
              Path.Combine(
                  root,
                  "evidence",
                  $"{unexpectedScenario.Id}.json"));

      await Assert.That(timeoutExit)
          .IsEqualTo(1);
      await Assert.That(unexpectedExit)
          .IsEqualTo(1);
      await Assert.That(timeoutResult.FailureCategory)
          .IsEqualTo("scenario-timeout");
      await Assert.That(unexpectedResult.FailureCategory)
          .IsEqualTo("scenario-unexpected-failure");
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  private static CanaryRuntimeManifest CreateRuntime() =>
      new(
          CanaryManifestFile.RuntimeSchemaVersion,
          "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
          CanaryTopologyProfiles.Portable,
          new CanarySourceRevision(
              "ncosentino/pitcrew-dashboard",
              new string('a', 40)),
          new CanarySourceRevision(
              "ncosentino/pitcrew",
              new string('b', 40)),
          "http://localhost:5000/",
          "http://localhost:5001/",
          [
              CanaryCapabilities.DashboardHttp,
              CanaryCapabilities.RelayHttp,
          ],
          DateTimeOffset.UnixEpoch.AddSeconds(1));

  private static CanaryScenarioContext CreateContext() =>
      new(
          Path.GetTempPath(),
          Path.GetTempPath(),
          Path.GetTempPath(),
          TimeProvider.System);
}
