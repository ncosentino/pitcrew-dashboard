using System.Globalization;

using PitCrew.Dashboard.Features.Fleet.Abstractions;
using PitCrew.Protocol;

namespace PitCrew.Dashboard.Features.Fleet;

internal static class FleetEvidenceProjector
{
  public static FleetNode ProjectNode(
      FleetNode node,
      DateTimeOffset generatedAt,
      TimeSpan connectorFreshness,
      TimeSpan managerFreshness)
  {
    var profileReceipts = node.ProfileEvidence.ToDictionary(
        profile => profile.ProfileId,
        StringComparer.OrdinalIgnoreCase);
    var projectedProfiles = node.Profiles
        .Select(profile =>
        {
          profileReceipts.TryGetValue(
              profile.ProfileId,
              out var receipt);
          return new FleetProfileEvidence(
              profile.ProfileId,
              receipt?.DashboardReceivedAt,
              ProjectProfileClaims(
                  profile,
                  receipt?.DashboardReceivedAt,
                  generatedAt,
                  managerFreshness));
        })
        .ToArray();

    return node with
    {
      EvidenceClaims =
      [
        ProjectConnectorClaim(
            node,
            generatedAt,
            connectorFreshness),
        ProjectInventoryClaim(node, generatedAt),
      ],
      ProfileEvidence = projectedProfiles,
    };
  }

  public static AlertIncident ProjectIncident(
      AlertIncident incident,
      DateTimeOffset generatedAt)
  {
    var conditionCoverage =
        incident.ConditionState is "confirmed" or "resolved"
            ? "complete"
            : "unavailable";
    var conditionFreshness = incident.ConditionState switch
    {
      "confirmed" => "triggered",
      "resolved" => "resolved",
      "waiting-for-evidence" => "waiting-for-evidence",
      "monitoring-ended" => "monitoring-ended",
      _ => "unavailable",
    };
    return incident with
    {
      EvidenceClaims =
      [
        new EvidenceClaim(
            "incident-condition",
            "dashboard-rule-evaluation",
            incident.Kind,
            null,
            incident.SourceObservedAt,
            incident.DashboardReceivedAt,
            incident.EvaluatedAt,
            null,
            generatedAt,
            null,
            conditionCoverage,
            "live",
            conditionFreshness,
            incident.ConditionState,
            conditionCoverage == "complete"
                ? null
                : "required-evidence-unavailable"),
        new EvidenceClaim(
            "incident-ownership",
            "dashboard-operator-action",
            "incident-acknowledgement",
            incident.AcknowledgedByGitHubUserId,
            incident.AcknowledgedAt,
            incident.AcknowledgedAt,
            null,
            null,
            generatedAt,
            null,
            "complete",
            "live",
            incident.OperatorState,
            incident.OperatorState,
            null),
      ],
    };
  }

  private static EvidenceClaim ProjectConnectorClaim(
      FleetNode node,
      DateTimeOffset generatedAt,
      TimeSpan freshness)
  {
    var boundary = node.LastSeenAt?.Add(freshness);
    var value = node.IsRevoked
        ? "revoked"
        : node.LastSeenAt is null
            ? "never-reported"
            : generatedAt < boundary
                ? "current"
                : "overdue";
    return new EvidenceClaim(
        "connector-contact",
        "dashboard-accepted-sync",
        "connector-synchronization",
        node.NodeId.ToString("D"),
        null,
        node.LastSeenAt,
        generatedAt,
        null,
        generatedAt,
        boundary,
        node.LastSeenAt is null ? "unavailable" : "complete",
        value == "overdue" ? "last-known" : "live",
        value,
        value == "current" ? "accepted" : null,
        value is "current" or "revoked" ? null : value);
  }

  private static EvidenceClaim ProjectInventoryClaim(
      FleetNode node,
      DateTimeOffset generatedAt)
  {
    var inventory = node.ProfileInventory;
    return new EvidenceClaim(
        "profile-inventory",
        "connector-local-acquisition",
        "profile-state",
        node.NodeId.ToString("D"),
        inventory?.ObservedAt,
        node.ProfileInventoryReceivedAt,
        generatedAt,
        null,
        generatedAt,
        null,
        inventory?.Coverage ?? "unavailable",
        inventory is null ? "last-known" : "live",
        inventory?.Coverage ?? "unavailable",
        inventory?.Coverage == "complete"
            ? node.Profiles.Count.ToString(CultureInfo.InvariantCulture)
            : null,
        inventory?.UnavailableReason ??
            (inventory is null ? "unsupported" : null));
  }

  private static IReadOnlyList<EvidenceClaim> ProjectProfileClaims(
      ManagerObservedState profile,
      DateTimeOffset? receivedAt,
      DateTimeOffset generatedAt,
      TimeSpan freshness)
  {
    if (profile.SourceObservations is null)
    {
      return
      [
        LegacyClaim(
            "manager-observation",
            "local-runtime",
            profile.ManagerStatus,
            profile.ObservedAt,
            receivedAt,
            generatedAt),
        LegacyClaim(
            "workload-evidence",
            "workload",
            WorkloadValue(profile),
            profile.ObservedAt,
            receivedAt,
            generatedAt),
      ];
    }

    var sources = profile.SourceObservations;
    return
    [
      SourceClaim(
          "manager-observation",
          sources.LocalRuntime,
          profile.ManagerStatus,
          receivedAt,
          generatedAt,
          freshness),
      SourceClaim(
          "github-scale-set",
          sources.GitHubScaleSet,
          profile.Autoscaling?.ScaleSetCount.ToString(
              CultureInfo.InvariantCulture),
          receivedAt,
          generatedAt,
          freshness),
      SourceClaim(
          "resource-telemetry",
          sources.ResourceTelemetry,
          profile.ResourceTelemetry?.Status,
          receivedAt,
          generatedAt,
          freshness),
      SourceClaim(
          "host-hardware",
          sources.HostHardware,
          profile.Host?.Hardware.Status,
          receivedAt,
          generatedAt,
          freshness),
      SourceClaim(
          "host-admission",
          sources.HostAdmission,
          profile.HostAdmission?.Status,
          receivedAt,
          generatedAt,
          freshness),
      SourceClaim(
          "subsystem-health",
          sources.SubsystemHealth,
          profile.SubsystemHealth is null
              ? null
              : $"{profile.SubsystemHealth.Docker.State}/{profile.SubsystemHealth.Github.State}",
          receivedAt,
          generatedAt,
          freshness),
      SourceClaim(
          "capacity",
          sources.Capacity,
          CapacityValue(profile),
          receivedAt,
          generatedAt,
          freshness),
      WorkloadClaim(
          "workload-evidence",
          sources.Workload,
          WorkloadValue(profile),
          receivedAt,
          generatedAt,
          freshness),
    ];
  }

  private static EvidenceClaim WorkloadClaim(
      string name,
      ManagerSourceObservation source,
      string value,
      DateTimeOffset? receivedAt,
      DateTimeOffset generatedAt,
      TimeSpan freshness)
  {
    var claim = SourceClaim(
        name,
        source,
        value,
        receivedAt,
        generatedAt,
        freshness);
    return claim.Freshness switch
    {
      "current" => claim with
      {
        Freshness = value == "0"
            ? "measured-zero"
            : "reported-active",
      },
      "stale" => claim with
      {
        Retention = "last-known",
        Freshness = "last-known",
      },
      _ => claim,
    };
  }

  private static EvidenceClaim LegacyClaim(
      string name,
      string source,
      string? value,
      DateTimeOffset observedAt,
      DateTimeOffset? receivedAt,
      DateTimeOffset generatedAt) =>
      new(
          name,
          "pitcrew-manager",
          source,
          null,
          observedAt,
          receivedAt,
          generatedAt,
          null,
          generatedAt,
          null,
          "unavailable",
          "last-known",
          "last-known",
          value,
          "unsupported");

  private static EvidenceClaim SourceClaim(
      string name,
      ManagerSourceObservation source,
      string? value,
      DateTimeOffset? receivedAt,
      DateTimeOffset generatedAt,
      TimeSpan freshness)
  {
    var boundary = source.ObservedAt?.Add(freshness);
    var state = source.Retention == "last-known"
        ? "last-known"
        : source.Coverage == "unavailable"
            ? "unavailable"
            : source.Coverage == "partial"
                ? "partial"
                : generatedAt < boundary
                    ? "current"
                    : "stale";
    return new EvidenceClaim(
        name,
        source.Authority,
        source.Source,
        source.SourceIdentity,
        source.ObservedAt,
        receivedAt,
        generatedAt,
        null,
        generatedAt,
        boundary,
        source.Coverage,
        source.Retention,
        state,
        source.Coverage == "unavailable" ? null : value,
        source.Reason);
  }

  private static string? CapacityValue(
      ManagerObservedState profile) =>
      profile.CapacityEvidence is null
          ? null
          : (profile.CapacityEvidence.Fixed is null
              ? profile.CapacityEvidence.Targets.Count
              : 1).ToString(CultureInfo.InvariantCulture);

  private static string WorkloadValue(
      ManagerObservedState profile) =>
      profile.Slots.Count(slot =>
          slot.CurrentJob is not null ||
          slot.Activity is "busy" or "draining")
          .ToString(CultureInfo.InvariantCulture);
}
