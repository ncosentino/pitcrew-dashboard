using PitCrew.Dashboard.Kernel.ImageRollouts;

namespace PitCrew.Dashboard.Features.Images;

/// <summary>
/// Maps shared kernel rollout control and command state onto the dashboard
/// UX response contracts.
/// </summary>
internal static class ProfileImageRolloutResponseMapper
{
  public static ProfileImageRolloutControlResponse ToResponse(
      Guid nodeId,
      ImageRolloutControlState control,
      DateTimeOffset readAt)
  {
    var observedStateAgeSeconds = GetEffectiveObservedStateAgeSeconds(
        control,
        readAt);
    return
      new(
          nodeId,
          control.ProfileId,
          control.Architecture,
          control.CurrentImageReference,
          control.CurrentImageDigest,
          control.CurrentLocalImageId,
          control.CurrentWorkerRevision,
          control.StaticFingerprint,
          control.PreservedConfigurationFingerprint,
          control.RoutingFingerprint,
          control.DesiredGeneration,
          control.DesiredStateHash,
          control.AllowedRecipeIds,
          control.RolloutAllowed,
          control.LocalSchemaSupported,
          control.LocalFailureCategory,
          control.OperationActive,
          observedStateAgeSeconds,
          control.ObservedStateMaximumAgeSeconds,
          observedStateAgeSeconds <=
              control.ObservedStateMaximumAgeSeconds,
          control.ManagerConvergenceStatus,
          control.CurrentWorkers,
          control.StaleWorkers,
          control.LatestCommand is null
              ? null
              : ToResponse(control.LatestCommand),
          control.RecentCommands
              .Select(ToResponse)
              .ToArray());
  }

  public static ProfileImageRolloutCommandResponse ToResponse(
      ImageRolloutCommandState state) =>
      new(
          state.CommandId,
          state.CandidateId,
          state.RecipeId,
          state.TargetDigest,
          state.TargetPlatform,
          state.PreviousImageReference,
          state.PreviousImageDigest,
          state.PreviousWorkerRevision,
          state.Status,
          state.FailureCategory,
          state.RequestedByGitHubUserId,
          state.RequestedAt,
          state.ExpiresAt,
          state.DeliveredAt,
          state.ClaimedAt,
          state.StartedAt,
          state.CompletedAt,
          state.TargetWorkerRevision,
          state.ManagerConvergenceStatus,
          state.CurrentWorkers,
          state.StaleWorkers,
          state.LastError,
          state.ResultMessage,
          state.PreviousCandidateId,
          state.PreviousRecipeId);

  private static int GetEffectiveObservedStateAgeSeconds(
      ImageRolloutControlState control,
      DateTimeOffset readAt)
  {
    const int maximumWireAgeSeconds = 86_400;
    var elapsedSeconds = Math.Max(
        0,
        (readAt - control.CapabilityObservedAt).TotalSeconds);
    return (int)Math.Min(
        maximumWireAgeSeconds,
        Math.Ceiling(control.ObservedStateAgeSeconds + elapsedSeconds));
  }
}
