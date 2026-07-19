using System.ComponentModel.DataAnnotations;

using NexusLabs.Needlr.Generators;

namespace PitCrew.Dashboard.Features.Fleet;

/// <summary>
/// Configures connector enrollment, synchronization cadence, and node freshness.
/// </summary>
[Options("PitCrew:Dashboard", ValidateOnStart = true)]
public sealed class FleetDashboardOptions
{
  /// <summary>
  /// Gets or sets the secret required to enroll a new connector installation.
  /// </summary>
  public string EnrollmentToken { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the tenant assigned to connectors enrolled by this deployment.
  /// </summary>
  [Required]
  public string TenantId { get; set; } = "local";

  /// <summary>
  /// Gets or sets the connector polling recommendation returned after successful synchronization.
  /// </summary>
  [Range(5, 3600)]
  public int ConnectorPollSeconds { get; set; } = 15;

  /// <summary>
  /// Gets or sets the maximum heartbeat age considered online.
  /// </summary>
  [Range(10, 86400)]
  public int NodeOfflineAfterSeconds { get; set; } = 60;

  /// <summary>
  /// Validates relationships between connector polling and dashboard freshness settings.
  /// </summary>
  /// <returns>Cross-property validation failures.</returns>
  public IEnumerable<ValidationError> Validate()
  {
    if (ConnectorPollSeconds * 2 > NodeOfflineAfterSeconds)
    {
      yield return
          "NodeOfflineAfterSeconds must be at least twice ConnectorPollSeconds.";
    }
  }
}
