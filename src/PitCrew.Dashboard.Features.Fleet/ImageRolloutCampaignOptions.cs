using System.ComponentModel.DataAnnotations;

using NexusLabs.Needlr.Generators;

namespace PitCrew.Dashboard.Features.Fleet;

/// <summary>
/// Configures bounded Dashboard-side image rollout campaign orchestration.
/// </summary>
[Options("PitCrew:Dashboard:ImageRolloutCampaigns", ValidateOnStart = true)]
public sealed class ImageRolloutCampaignOptions
{
  /// <summary>
  /// Gets or sets the hard target ceiling for one frozen campaign.
  /// </summary>
  [Range(1, 5000)]
  public int MaximumTargets { get; set; } = 1000;

  /// <summary>
  /// Gets or sets how often campaign state is reconciled and dispatch is attempted.
  /// </summary>
  [Range(1, 3600)]
  public int PollIntervalSeconds { get; set; } = 5;

  /// <summary>
  /// Gets or sets how long one target dispatch claim remains owned.
  /// </summary>
  [Range(10, 3600)]
  public int ClaimLeaseSeconds { get; set; } = 60;

  /// <summary>
  /// Gets or sets the maximum active targets for one campaign.
  /// </summary>
  [Range(1, 100)]
  public int MaximumConcurrentTargetsPerCampaign { get; set; } = 5;

  /// <summary>
  /// Gets or sets the maximum active campaign targets on one node.
  /// </summary>
  [Range(1, 10)]
  public int MaximumConcurrentTargetsPerNode { get; set; } = 1;

  /// <summary>
  /// Gets or sets the maximum active campaigns reconciled during one tick.
  /// </summary>
  [Range(1, 1000)]
  public int MaximumCampaignsPerReconciliation { get; set; } = 100;

  /// <summary>
  /// Gets or sets how long terminal campaigns are retained.
  /// </summary>
  [Range(1, 3650)]
  public int RetentionDays { get; set; } = 90;

  /// <summary>
  /// Gets or sets the hard retained terminal campaign ceiling per tenant.
  /// </summary>
  [Range(10, 10000)]
  public int MaximumCampaignsPerTenant { get; set; } = 500;

  /// <summary>
  /// Validates orchestration relationships.
  /// </summary>
  /// <returns>Cross-property validation failures.</returns>
  public IEnumerable<ValidationError> Validate()
  {
    if (ClaimLeaseSeconds <= PollIntervalSeconds)
    {
      yield return
          "ClaimLeaseSeconds must be greater than PollIntervalSeconds.";
    }
    if (MaximumConcurrentTargetsPerNode >
        MaximumConcurrentTargetsPerCampaign)
    {
      yield return
          "MaximumConcurrentTargetsPerNode must not exceed "
          + "MaximumConcurrentTargetsPerCampaign.";
    }
  }
}
