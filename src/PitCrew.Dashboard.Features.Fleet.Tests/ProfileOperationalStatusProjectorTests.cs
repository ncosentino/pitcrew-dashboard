using PitCrew.Dashboard.Features.Fleet.Abstractions;

namespace PitCrew.Dashboard.Features.Fleet.Tests;

public sealed class ProfileOperationalStatusProjectorTests
{
  private static readonly DateTimeOffset ObservedAt = new(
      2026,
      10,
      1,
      12,
      0,
      0,
      TimeSpan.Zero);

  [Test]
  public async Task Healthy_Zero_Demand_Is_Configured_Scale_To_Zero()
  {
    var profile =
        ProfileOperationalStatusTestData.CreateAutoscaledProfile(
            ObservedAt);
    var node = ProfileOperationalStatusTestData.CreateNode(
        profile,
        ObservedAt);

    var status = ProfileOperationalStatusProjector.Project(
        node,
        "default",
        null,
        ObservedAt.AddSeconds(30),
        TimeSpan.FromMinutes(1));

    await Assert.That(status.ConfigurationState)
        .IsEqualTo("configured");
    await Assert.That(status.RuntimeState)
        .IsEqualTo("idle-scale-to-zero");
    await Assert.That(status.ReasonCode)
        .IsEqualTo("healthy-scale-to-zero");
    await Assert.That(status.Summary)
        .IsEqualTo(
            "CONFIGURED · AUTOSCALED · IDLE (SCALE-TO-ZERO)");
    await Assert.That(status.ConfiguredMaximum).IsEqualTo(4);
    await Assert.That(status.TargetSlots).IsEqualTo(0);
    await Assert.That(status.ActiveSlots).IsEqualTo(0);
  }

  [Test]
  public async Task Complete_Current_Inventory_Proves_Profile_Is_Not_Configured()
  {
    var node = ProfileOperationalStatusTestData.CreateNode(
        null,
        ObservedAt);

    var status = ProfileOperationalStatusProjector.Project(
        node,
        "missing-profile",
        null,
        ObservedAt.AddSeconds(30),
        TimeSpan.FromMinutes(1));

    await Assert.That(status.ConfigurationState)
        .IsEqualTo("not-configured");
    await Assert.That(status.RuntimeState).IsNull();
    await Assert.That(status.InventoryAuthoritative).IsTrue()
        .Because("online complete inventory is exhaustive");
    await Assert.That(status.ReasonCode)
        .IsEqualTo("complete-current-inventory-omits-profile");
  }

  [Test]
  public async Task Partial_Inventory_Cannot_Prove_Profile_Is_Not_Configured()
  {
    var node = ProfileOperationalStatusTestData.CreateNode(
        null,
        ObservedAt,
        "partial");

    var status = ProfileOperationalStatusProjector.Project(
        node,
        "missing-profile",
        null,
        ObservedAt.AddSeconds(30),
        TimeSpan.FromMinutes(1));

    await Assert.That(status.ConfigurationState)
        .IsEqualTo("unknown");
    await Assert.That(status.RuntimeState).IsNull();
    await Assert.That(status.InventoryAuthoritative).IsFalse()
        .Because("partial inventory cannot prove profile absence");
    await Assert.That(status.ReasonCode)
        .IsEqualTo("inventory-not-authoritative");
  }

  [Test]
  public async Task Acknowledged_Pause_Is_Not_Reported_As_Scale_To_Zero()
  {
    var profile =
        ProfileOperationalStatusTestData.CreateAutoscaledProfile(
            ObservedAt,
            maximumSlots: 0);
    var node = ProfileOperationalStatusTestData.CreateNode(
        profile,
        ObservedAt);
    var control = new CapacityControlState(
        "default",
        1,
        0,
        4,
        true,
        null,
        new Guid("22222222-2222-4222-8222-222222222222"),
        4);

    var status = ProfileOperationalStatusProjector.Project(
        node,
        "default",
        control,
        ObservedAt.AddSeconds(30),
        TimeSpan.FromMinutes(1));

    await Assert.That(status.ConfigurationState)
        .IsEqualTo("configured");
    await Assert.That(status.RuntimeState).IsEqualTo("paused");
    await Assert.That(status.ExplicitlyPaused).IsTrue()
        .Because("the pause has a fenced resume maximum");
    await Assert.That(status.ReasonCode)
        .IsEqualTo("explicit-capacity-pause");
  }

  [Test]
  public async Task Stale_Manager_Evidence_Is_Not_Reported_As_Healthy_Idle()
  {
    var profile =
        ProfileOperationalStatusTestData.CreateAutoscaledProfile(
            ObservedAt);
    var node = ProfileOperationalStatusTestData.CreateNode(
        profile,
        ObservedAt);

    var status = ProfileOperationalStatusProjector.Project(
        node,
        "default",
        null,
        ObservedAt.AddMinutes(2),
        TimeSpan.FromMinutes(1));

    await Assert.That(status.ConfigurationState)
        .IsEqualTo("configured");
    await Assert.That(status.RuntimeState).IsEqualTo("stale");
    await Assert.That(status.ReasonCode)
        .IsEqualTo("manager-evidence-stale");
  }
}
