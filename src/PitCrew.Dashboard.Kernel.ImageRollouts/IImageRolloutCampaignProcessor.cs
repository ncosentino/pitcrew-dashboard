namespace PitCrew.Dashboard.Kernel.ImageRollouts;

/// <summary>
/// Reconciles campaigns and dispatches at most one due target per invocation.
/// </summary>
public interface IImageRolloutCampaignProcessor
{
  /// <summary>
  /// Processes one bounded campaign reconciliation and dispatch cycle.
  /// </summary>
  /// <param name="cancellationToken">Token that cancels processing.</param>
  /// <returns>The number of profile commands queued, either zero or one.</returns>
  Task<int> ProcessOnceAsync(CancellationToken cancellationToken);
}
