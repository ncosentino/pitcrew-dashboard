namespace PitCrew.Dashboard.Kernel.ImageRollouts;

/// <summary>
/// Exposes the bounded observed-state freshness window used to reject stale
/// rollout capability evidence.
/// </summary>
public interface IImageRolloutObservedStatePolicy
{
  /// <summary>
  /// Gets the maximum observed-state age in seconds that still counts as
  /// fresh.
  /// </summary>
  int ObservedStateMaximumAgeSeconds { get; }

  /// <summary>
  /// Gets the maximum terminal command history returned for one profile.
  /// </summary>
  int HistoryPerProfile { get; }
}
