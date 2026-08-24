using PitCrew.Support.Canary.Contracts;
using PitCrew.Support.Canary.Scenarios;

namespace PitCrew.Support.Canary.Tests;

public sealed class CanaryScenarioRegistryTests
{
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
