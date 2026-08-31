using PitCrew.Dashboard.Kernel.ImageRollouts;

namespace PitCrew.Dashboard.Features.Images;

/// <summary>
/// Composes the shared kernel <see cref="IImageRolloutCommandStore"/> and
/// the local <see cref="IImageRolloutObservedStatePolicy"/> to project a
/// single profile's rollout control for the dashboard UX.
/// </summary>
internal sealed class ProfileImageRolloutReader(
    IImageRolloutCommandStore _rolloutStore,
    IImageRolloutObservedStatePolicy _observedStatePolicy,
    TimeProvider _timeProvider)
    : IProfileImageRolloutReader
{
  public async Task<ProfileImageRolloutControlResponse?> GetProfileControlAsync(
      string tenantId,
      Guid nodeId,
      string profileId,
      CancellationToken cancellationToken)
  {
    var control = await _rolloutStore.GetProfileControlOrNullAsync(
        tenantId,
        nodeId,
        profileId,
        _observedStatePolicy.ObservedStateMaximumAgeSeconds,
        cancellationToken,
        _observedStatePolicy.HistoryPerProfile);
    return control is null
        ? null
        : ProfileImageRolloutResponseMapper.ToResponse(
            nodeId,
            control,
            _timeProvider.GetUtcNow());
  }
}
