using PitCrew.Support.Canary.Contracts;
using PitCrew.Support.Canary.Scenarios;

namespace PitCrew.Support.Canary.Tests;

public sealed class CanaryScenarioRegistryTests
{
  [Test]
  public async Task Terminal_Lifecycle_Scenario_Is_Additive_And_Profile_Neutral()
  {
    const string scenarioId =
        "support-terminal-lifecycle-v1";

    var scenario = CanaryScenarioRegistry.ResolveOrNull(
        scenarioId);

    await Assert.That(scenario).IsNotNull();
    await Assert.That(scenario!.Id).IsEqualTo(scenarioId);
    await Assert.That(scenario.RequiredCapabilities)
        .Contains(CanaryCapabilities.PitCrewFileOnlyEvidence);
    await Assert.That(scenario.RequiredCapabilities)
        .DoesNotContain(
            CanaryCapabilities.RejectedRequestInjection);
    await Assert.That(scenario.RequiredCapabilities)
        .DoesNotContain(
            CanaryCapabilities.WindowsInstalledServices);
    await Assert.That(scenario.RequiredCapabilities)
        .DoesNotContain(
            CanaryCapabilities.LinuxInstalledServices);
    await Assert.That(CanaryScenarioRegistry.ScenarioIds)
        .Contains(scenarioId);
  }

  [Test]
  public async Task Request_Rejection_Matrix_Is_Additive_And_Process_Bound()
  {
    const string scenarioId =
        "support-request-rejection-matrix-v1";

    var scenario = CanaryScenarioRegistry.ResolveOrNull(
        scenarioId);

    await Assert.That(scenario).IsNotNull();
    await Assert.That(scenario!.Id).IsEqualTo(scenarioId);
    await Assert.That(scenario.RequiredCapabilities)
        .Contains(CanaryCapabilities.RejectedRequestInjection);
    await Assert.That(scenario.RequiredCapabilities)
        .Contains(CanaryCapabilities.SupportAgentProcess);
    await Assert.That(scenario.RequiredCapabilities)
        .DoesNotContain(
            CanaryCapabilities.WindowsInstalledServices);
    await Assert.That(scenario.RequiredCapabilities)
        .DoesNotContain(
            CanaryCapabilities.LinuxInstalledServices);
    await Assert.That(
            SupportRejectedRequestMatrixScenario
                .ExpectedDispositions.Count)
        .IsEqualTo(CanaryRejectedRequestCases.All.Count);
    await Assert.That(
            SupportRejectedRequestMatrixScenario
                .ExpectedDispositions
                .Select(expectation =>
                    expectation.InjectionCase))
        .IsEquivalentTo(CanaryRejectedRequestCases.All);
    await Assert.That(CanaryScenarioRegistry.ScenarioIds)
        .Contains(scenarioId);
  }

  [Test]
  public async Task Diagnostic_Mode_Matrix_Is_Additive_And_Uses_Base_Capabilities()
  {
    const string scenarioId =
        "support-diagnostic-mode-matrix-v1";

    var scenario = CanaryScenarioRegistry.ResolveOrNull(
        scenarioId);

    await Assert.That(scenario).IsNotNull();
    await Assert.That(scenario!.Id).IsEqualTo(scenarioId);
    await Assert.That(scenario.RequiredCapabilities)
        .Contains(CanaryCapabilities.PitCrewFileOnlyEvidence);
    await Assert.That(scenario.RequiredCapabilities)
        .DoesNotContain(CanaryCapabilities.RelayRestartControl);
    await Assert.That(CanaryScenarioRegistry.ScenarioIds)
        .Contains(scenarioId);
  }

  [Test]
  public async Task Relay_Restart_Scenario_Is_Additive_And_Capability_Bound()
  {
    const string scenarioId =
        "support-relay-restart-recovery-v1";

    var scenario = CanaryScenarioRegistry.ResolveOrNull(
        scenarioId);

    await Assert.That(scenario).IsNotNull();
    await Assert.That(scenario!.Id).IsEqualTo(scenarioId);
    await Assert.That(scenario.RequiredCapabilities)
        .Contains(CanaryCapabilities.RelayRestartControl);
    await Assert.That(CanaryScenarioRegistry.ScenarioIds)
        .Contains(scenarioId);
  }
}
