namespace PitCrew.Dashboard.Features.Fleet.Tests;

public sealed class FleetDashboardOptionsTests
{
  [Test]
  public async Task Default_Alert_Policy_Is_Cross_Property_Valid()
  {
    var errors = new FleetDashboardOptions().Validate().ToArray();

    await Assert.That(errors).IsEmpty();
  }

  [Test]
  public async Task Invalid_Alert_Relationships_Are_Reported()
  {
    var options = new FleetDashboardOptions
    {
      AlertManagerStaleAfterSeconds = 20,
      AlertResourceWindowMinutes = 1,
      AlertResourcePressureSamples = 10,
      AlertNetworkBytesPerSecond = -1,
      AlertBlockIoBytesPerSecond = -1,
      MaximumResolvedAlertIncidentsPerTenant = 10,
      MaximumAlertIncidentsPerQuery = 11,
    };

    var errors = options.Validate().ToArray();

    await Assert.That(errors.Length).IsEqualTo(5);
    await Assert.That(errors).Contains(
        "AlertManagerStaleAfterSeconds must be at least twice ConnectorPollSeconds.");
    await Assert.That(errors).Contains(
        "AlertResourceWindowMinutes must hold at least AlertResourcePressureSamples connector polls.");
    await Assert.That(errors).Contains(
        "AlertNetworkBytesPerSecond cannot be negative.");
    await Assert.That(errors).Contains(
        "AlertBlockIoBytesPerSecond cannot be negative.");
    await Assert.That(errors).Contains(
        "MaximumAlertIncidentsPerQuery cannot exceed MaximumResolvedAlertIncidentsPerTenant.");
  }
}
