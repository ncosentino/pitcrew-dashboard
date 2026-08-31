namespace PitCrew.Dashboard.Features.Images;

/// <summary>
/// Reads one profile's rollout control and bounded history for the
/// dashboard UX. Composes the shared kernel command store with the local
/// observed-state policy so the Carter surface only injects one narrow,
/// Images-owned reader instead of both cross-feature services.
/// </summary>
internal interface IProfileImageRolloutReader
{
  /// <summary>
  /// Reads one tenant-scoped node's rollout control for one profile.
  /// Returns <see langword="null"/> when the node has not advertised
  /// rollout capability, the profile is not known, or the node is
  /// tenant-foreign or revoked.
  /// </summary>
  Task<ProfileImageRolloutControlResponse?> GetProfileControlAsync(
      string tenantId,
      Guid nodeId,
      string profileId,
      CancellationToken cancellationToken);
}
