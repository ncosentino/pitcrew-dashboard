using PitCrew.Dashboard.Features.Fleet.Abstractions;
using PitCrew.Protocol;

namespace PitCrew.Dashboard.Features.Fleet.Tests;

internal static class ProfileOperationalStatusTestData
{
  public static FleetNode CreateNode(
      ManagerObservedState? profile,
      DateTimeOffset observedAt,
      string inventoryCoverage = "complete",
      bool isOnline = true)
  {
    var profiles = profile is null
        ? Array.Empty<ManagerObservedState>()
        : [profile];
    return new FleetNode(
        new Guid("11111111-1111-4111-8111-111111111111"),
        "Example node",
        "1.0.0",
        observedAt.AddDays(-1),
        observedAt.AddSeconds(1),
        isOnline,
        false,
        false,
        profiles,
        [],
        [])
    {
      ProfileInventory = new ConnectorProfileInventory(
          inventoryCoverage,
          observedAt,
          inventoryCoverage == "complete"
              ? null
              : "inventory-partial"),
      ProfileInventoryReceivedAt = observedAt.AddSeconds(1),
    };
  }

  public static ManagerObservedState CreateAutoscaledProfile(
      DateTimeOffset observedAt,
      int maximumSlots = 4,
      int minimumIdleSlots = 0,
      int targetSlots = 0,
      int activeSlots = 0) =>
      new(
          1,
          10,
          "default",
          "manager-instance",
          "running",
          observedAt,
          "repo",
          1,
          new string('a', 64),
          "accepted",
          targetSlots,
          activeSlots,
          0,
          [],
          null,
          maximumSlots,
          new ManagerAutoscalingState(
              "scale-set",
              "running",
              minimumIdleSlots,
              maximumSlots,
              targetSlots,
              0,
              0,
              0,
              0,
              0,
              30,
              1,
              null,
              null),
          activeSlots);
}
