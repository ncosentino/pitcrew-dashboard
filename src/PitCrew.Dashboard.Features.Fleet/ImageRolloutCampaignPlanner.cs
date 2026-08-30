using PitCrew.Dashboard.Features.Fleet.Abstractions;
using PitCrew.Dashboard.Kernel.ImageRollouts;

namespace PitCrew.Dashboard.Features.Fleet;

internal static class ImageRolloutCampaignPlanner
{
  public static IReadOnlyList<ImageRolloutCampaignPlannedTarget>
      CreateForwardTargets(
          FleetResponse fleet,
          IReadOnlyList<NodeImageRolloutControls> controls,
          ImageRolloutCandidateAuthority candidate,
          DateTimeOffset plannedAt,
          int observedStateMaximumAgeSeconds)
  {
    var controlByNode = controls.ToDictionary(
        static node => node.NodeId);
    var targets = new List<ImageRolloutCampaignPlannedTarget>();
    foreach (var node in fleet.Nodes.OrderBy(static node => node.NodeId))
    {
      controlByNode.TryGetValue(node.NodeId, out var nodeControls);
      var profileIds = node.Profiles
          .Select(static profile => profile.ProfileId)
          .Concat(nodeControls?.Profiles.Select(
              static profile => profile.ProfileId) ?? [])
          .Distinct(StringComparer.OrdinalIgnoreCase)
          .Order(StringComparer.Ordinal)
          .ToArray();
      foreach (var profileId in profileIds)
      {
        var control = nodeControls?.Profiles.FirstOrDefault(profile =>
            string.Equals(
                profile.ProfileId,
                profileId,
                StringComparison.OrdinalIgnoreCase));
        var exclusion = GetExclusionCategory(
            node,
            control,
            candidate,
            plannedAt,
            observedStateMaximumAgeSeconds);
        targets.Add(new ImageRolloutCampaignPlannedTarget(
            Guid.NewGuid(),
            node.NodeId,
            node.DisplayName,
            profileId,
            candidate,
            control is null ? null : CreateFences(control),
            exclusion));
      }
    }
    return targets;
  }

  public static IReadOnlyList<ImageRolloutCampaignPlannedTarget>
      CreateRollbackTargets(
          ImageRolloutCampaignState source,
          FleetResponse fleet,
          IReadOnlyList<NodeImageRolloutControls> controls,
          DateTimeOffset plannedAt,
          int observedStateMaximumAgeSeconds)
  {
    var fleetByNode = fleet.Nodes.ToDictionary(static node => node.NodeId);
    var controlByNode = controls.ToDictionary(static node => node.NodeId);
    var targets = new List<ImageRolloutCampaignPlannedTarget>(
        source.Targets.Count);
    foreach (var sourceTarget in source.Targets
        .OrderBy(static target => target.NodeId)
        .ThenBy(static target => target.ProfileId, StringComparer.Ordinal))
    {
      fleetByNode.TryGetValue(sourceTarget.NodeId, out var node);
      controlByNode.TryGetValue(sourceTarget.NodeId, out var nodeControls);
      var control = nodeControls?.Profiles.FirstOrDefault(profile =>
          string.Equals(
              profile.ProfileId,
              sourceTarget.ProfileId,
              StringComparison.OrdinalIgnoreCase));
      var candidate = CreateRollbackCandidateOrNull(sourceTarget);
      var exclusion = candidate is null ||
          sourceTarget.Status != ImageRolloutCampaignTargetStatus.Complete
          ? "rollback-authority-unavailable"
          : GetRollbackExclusionCategory(
              node,
              control,
              sourceTarget,
              candidate,
              plannedAt,
              observedStateMaximumAgeSeconds);
      targets.Add(new ImageRolloutCampaignPlannedTarget(
          Guid.NewGuid(),
          sourceTarget.NodeId,
          node?.DisplayName ?? sourceTarget.NodeDisplayName,
          sourceTarget.ProfileId,
          candidate,
          control is null ? null : CreateFences(control),
          exclusion));
    }
    return targets;
  }

  private static string? GetRollbackExclusionCategory(
      FleetNode? node,
      ImageRolloutControlState? control,
      ImageRolloutCampaignTargetState sourceTarget,
      ImageRolloutCandidateAuthority candidate,
      DateTimeOffset plannedAt,
      int observedStateMaximumAgeSeconds)
  {
    var common = GetExclusionCategory(
        node,
        control,
        candidate,
        plannedAt,
        observedStateMaximumAgeSeconds);
    if (common is not null)
    {
      return common;
    }
    if (!string.Equals(
            control!.CurrentImageDigest,
            sourceTarget.Candidate?.TargetDigest,
            StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(
            control.CurrentWorkerRevision,
            sourceTarget.TargetWorkerRevision,
            StringComparison.OrdinalIgnoreCase))
    {
      return "stale-observed-state";
    }
    return null;
  }

  private static string? GetExclusionCategory(
      FleetNode? node,
      ImageRolloutControlState? control,
      ImageRolloutCandidateAuthority candidate,
      DateTimeOffset plannedAt,
      int observedStateMaximumAgeSeconds)
  {
    if (node is null)
    {
      return "capability-unavailable";
    }
    if (node.IsRevoked)
    {
      return "node-revoked";
    }
    if (!node.IsOnline)
    {
      return "node-offline";
    }
    if (control is null)
    {
      return "capability-unavailable";
    }
    var elapsedSeconds = Math.Max(
        0,
        (plannedAt - control.CapabilityObservedAt).TotalSeconds);
    if (control.ObservedStateAgeSeconds + elapsedSeconds >
        observedStateMaximumAgeSeconds)
    {
      return "stale-observed-state";
    }
    if (control.LocalFailureCategory is not null)
    {
      return NormalizeLocalFailure(control.LocalFailureCategory);
    }
    if (!control.LocalSchemaSupported)
    {
      return "unsupported-schema";
    }
    if (!control.RolloutAllowed)
    {
      return "policy-disabled";
    }
    if (!control.AllowedRecipeIds.Any(recipeId =>
            string.Equals(
                recipeId,
                candidate.RecipeId,
                StringComparison.OrdinalIgnoreCase)))
    {
      return "recipe-not-allowed";
    }
    if (!string.Equals(
            control.Architecture,
            candidate.TargetPlatform,
            StringComparison.Ordinal))
    {
      return "unsupported-architecture";
    }
    if (control.OperationActive)
    {
      return "operation-active";
    }
    if (string.Equals(
            control.CurrentImageDigest,
            candidate.TargetDigest,
            StringComparison.OrdinalIgnoreCase) ||
        string.Equals(
            control.CurrentLocalImageId,
            candidate.TargetDigest,
            StringComparison.OrdinalIgnoreCase))
    {
      return "already-current";
    }
    return null;
  }

  private static string NormalizeLocalFailure(string category) =>
      category switch
      {
        "unsupported-schema" => "unsupported-schema",
        "unsupported-manager" => "unsupported-manager",
        "unsupported-topology" => "unsupported-topology",
        "unsupported-architecture" => "unsupported-architecture",
        "recipe-not-allowed" => "recipe-not-allowed",
        "registry-not-allowed" => "registry-not-allowed",
        "stale-observed-state" => "stale-observed-state",
        _ => "policy-disabled",
      };

  private static ImageRolloutCandidateAuthority?
      CreateRollbackCandidateOrNull(
          ImageRolloutCampaignTargetState sourceTarget)
  {
    if (sourceTarget.PreviousCandidateId is null ||
        sourceTarget.PreviousRecipeId is null ||
        sourceTarget.PreviousImageDigest is null ||
        sourceTarget.PreviousWorkerRevision is null ||
        sourceTarget.Candidate is null)
    {
      return null;
    }
    return new ImageRolloutCandidateAuthority(
        sourceTarget.PreviousCandidateId.Value,
        sourceTarget.PreviousRecipeId,
        sourceTarget.PreviousImageDigest,
        sourceTarget.Candidate.TargetPlatform);
  }

  private static ImageRolloutCommandFences CreateFences(
      ImageRolloutControlState control) =>
      new(
          control.CurrentImageReference,
          control.CurrentImageDigest,
          control.CurrentLocalImageId,
          control.CurrentWorkerRevision,
          control.StaticFingerprint,
          control.PreservedConfigurationFingerprint,
          control.RoutingFingerprint,
          control.DesiredGeneration,
          control.DesiredStateHash);
}
