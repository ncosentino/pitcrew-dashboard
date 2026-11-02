using PitCrew.Dashboard.Features.Fleet.Abstractions;
using PitCrew.Protocol;

namespace PitCrew.Dashboard.Features.Fleet;

internal static class ProfileOperationalStatusProjector
{
  public static ProfileOperationalStatusResult Project(
      FleetNode node,
      string profileId,
      CapacityControlState? capacityControl,
      DateTimeOffset generatedAt,
      TimeSpan managerFreshness)
  {
    var inventoryCoverage =
        node.ProfileInventory?.Coverage ?? "unavailable";
    var inventoryAuthoritative =
        node.IsOnline &&
        string.Equals(
            inventoryCoverage,
            "complete",
            StringComparison.Ordinal) &&
        node.ProfileInventoryReceivedAt is { } inventoryReceivedAt &&
        node.LastSeenAt is { } lastSeenAt &&
        inventoryReceivedAt >= lastSeenAt;
    var profile = node.Profiles.FirstOrDefault(candidate =>
        string.Equals(
            candidate.ProfileId,
            profileId,
            StringComparison.Ordinal));
    if (profile is null)
    {
      var configurationState = inventoryAuthoritative
          ? ProfileConfigurationState.NotConfigured
          : ProfileConfigurationState.Unknown;
      return new ProfileOperationalStatusResult(
          generatedAt,
          node.NodeId,
          profileId,
          inventoryCoverage,
          inventoryAuthoritative,
          node.ProfileInventory?.ObservedAt,
          node.ProfileInventoryReceivedAt,
          node.ProfileInventory?.UnavailableReason ??
              (node.ProfileInventory is null ? "unsupported" : null),
          ConfigurationValue(configurationState),
          null,
          inventoryAuthoritative
              ? "complete-current-inventory-omits-profile"
              : "inventory-not-authoritative",
          configurationState == ProfileConfigurationState.NotConfigured
              ? "NOT CONFIGURED"
              : "CONFIGURATION UNKNOWN",
          node.IsOnline,
          node.LastSeenAt,
          null,
          null,
          null,
          null,
          null,
          null,
          null,
          null,
          null,
          null,
          null,
          null);
    }

    var explicitlyPaused =
        capacityControl is
        {
          CurrentMaximum: 0,
          PauseCommandId: not null,
          ResumeMaximum: > 0,
        };
    var runtime = Runtime(
        node,
        profile,
        explicitlyPaused,
        generatedAt,
        managerFreshness);
    var capacityMode = profile.Autoscaling is null
        ? "fixed"
        : "autoscaled";
    return new ProfileOperationalStatusResult(
        generatedAt,
        node.NodeId,
        profileId,
        inventoryCoverage,
        inventoryAuthoritative,
        node.ProfileInventory?.ObservedAt,
        node.ProfileInventoryReceivedAt,
        node.ProfileInventory?.UnavailableReason ??
            (node.ProfileInventory is null ? "unsupported" : null),
        ConfigurationValue(ProfileConfigurationState.Configured),
        RuntimeValue(runtime.State),
        runtime.ReasonCode,
        Summary(capacityMode, runtime.State),
        node.IsOnline,
        node.LastSeenAt,
        profile.ManagerStatus,
        profile.ObservedAt,
        capacityMode,
        explicitlyPaused,
        profile.Autoscaling?.MaximumSlots ??
            profile.ConfiguredSlots ??
            capacityControl?.CurrentMaximum,
        profile.Autoscaling?.MinimumIdleSlots,
        profile.Autoscaling?.TargetSlots ?? profile.DesiredSlots,
        profile.ActiveSlots,
        profile.Autoscaling?.IdleRunners,
        profile.Autoscaling?.BusyRunners,
        profile.DrainingSlots,
        profile.Autoscaling?.Status);
  }

  private static (ProfileRuntimeState State, string ReasonCode) Runtime(
      FleetNode node,
      ManagerObservedState profile,
      bool explicitlyPaused,
      DateTimeOffset generatedAt,
      TimeSpan managerFreshness)
  {
    if (!node.IsOnline ||
        generatedAt >= profile.ObservedAt.Add(managerFreshness))
    {
      return (
          ProfileRuntimeState.Stale,
          "manager-evidence-stale");
    }
    if (explicitlyPaused)
    {
      return (
          ProfileRuntimeState.Paused,
          "explicit-capacity-pause");
    }
    if (profile.ManagerStatus is "stopping" or "stopped")
    {
      return (
          ProfileRuntimeState.ManagerStopped,
          "manager-not-running");
    }

    var autoscaling = profile.Autoscaling;
    if (profile.DesiredStateStatus is "invalid" or "stale" or "conflict" ||
        autoscaling?.Status == "degraded" ||
        autoscaling?.LastError is not null)
    {
      return (
          ProfileRuntimeState.Degraded,
          "manager-reported-degraded-state");
    }
    if (profile.ManagerStatus == "starting" ||
        autoscaling?.Status == "starting")
    {
      return (
          ProfileRuntimeState.ScalingUp,
          "manager-starting");
    }

    if (autoscaling is null)
    {
      return FixedRuntime(profile);
    }
    if (profile.ActiveSlots < autoscaling.TargetSlots)
    {
      return (
          ProfileRuntimeState.ScalingUp,
          "active-capacity-below-target");
    }
    if (profile.DrainingSlots > 0 ||
        profile.ActiveSlots > autoscaling.TargetSlots ||
        autoscaling.ScaleDownAt is not null ||
        autoscaling.Status == "stopping")
    {
      return (
          ProfileRuntimeState.ScalingDown,
          "active-capacity-above-target");
    }
    if (autoscaling.MaximumSlots > 0 &&
        autoscaling.MinimumIdleSlots == 0 &&
        autoscaling.TargetSlots == 0 &&
        profile.ActiveSlots == 0)
    {
      return (
          ProfileRuntimeState.IdleScaleToZero,
          "healthy-scale-to-zero");
    }
    if (profile.ActiveSlots > 0)
    {
      return (
          ProfileRuntimeState.Active,
          "active-capacity-present");
    }
    return (
        ProfileRuntimeState.Degraded,
        autoscaling.MaximumSlots == 0
            ? "zero-maximum-without-acknowledged-pause"
            : "autoscaling-target-not-converged");
  }

  private static (ProfileRuntimeState State, string ReasonCode) FixedRuntime(
      ManagerObservedState profile)
  {
    if (profile.ActiveSlots < profile.DesiredSlots)
    {
      return (
          ProfileRuntimeState.ScalingUp,
          "active-capacity-below-target");
    }
    if (profile.DrainingSlots > 0 ||
        profile.ActiveSlots > profile.DesiredSlots)
    {
      return (
          ProfileRuntimeState.ScalingDown,
          "active-capacity-above-target");
    }
    if (profile.ActiveSlots > 0)
    {
      return (
          ProfileRuntimeState.Active,
          "active-capacity-present");
    }
    if (profile.ConfiguredSlots == 0)
    {
      return (
          ProfileRuntimeState.Degraded,
          "zero-maximum-without-acknowledged-pause");
    }
    return (
        ProfileRuntimeState.Idle,
        "fixed-capacity-idle");
  }

  private static string ConfigurationValue(
      ProfileConfigurationState state) =>
      state switch
      {
        ProfileConfigurationState.Configured => "configured",
        ProfileConfigurationState.NotConfigured => "not-configured",
        _ => "unknown",
      };

  private static string RuntimeValue(ProfileRuntimeState state) =>
      state switch
      {
        ProfileRuntimeState.Active => "active",
        ProfileRuntimeState.Idle => "idle",
        ProfileRuntimeState.IdleScaleToZero => "idle-scale-to-zero",
        ProfileRuntimeState.ScalingUp => "scaling-up",
        ProfileRuntimeState.ScalingDown => "scaling-down",
        ProfileRuntimeState.Paused => "paused",
        ProfileRuntimeState.ManagerStopped => "manager-stopped",
        ProfileRuntimeState.Degraded => "degraded",
        _ => "stale",
      };

  private static string Summary(
      string capacityMode,
      ProfileRuntimeState runtimeState)
  {
    var mode = capacityMode == "autoscaled"
        ? "AUTOSCALED"
        : "FIXED";
    var runtime = runtimeState switch
    {
      ProfileRuntimeState.Active => "ACTIVE",
      ProfileRuntimeState.Idle => "IDLE",
      ProfileRuntimeState.IdleScaleToZero => "IDLE (SCALE-TO-ZERO)",
      ProfileRuntimeState.ScalingUp => "SCALING UP",
      ProfileRuntimeState.ScalingDown => "SCALING DOWN",
      ProfileRuntimeState.Paused => "PAUSED",
      ProfileRuntimeState.ManagerStopped => "MANAGER STOPPED",
      ProfileRuntimeState.Degraded => "DEGRADED",
      _ => "STATUS STALE",
    };
    return $"CONFIGURED · {mode} · {runtime}";
  }
}
