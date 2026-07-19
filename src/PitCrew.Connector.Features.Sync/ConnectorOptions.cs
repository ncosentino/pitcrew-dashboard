using System.ComponentModel.DataAnnotations;

using NexusLabs.Needlr.Generators;

namespace PitCrew.Connector.Features.Sync;

/// <summary>
/// Configures the outbound Pitcrew dashboard connector.
/// </summary>
[Options("PitCrew:Connector", ValidateOnStart = true)]
public sealed class ConnectorOptions
{
  /// <summary>
  /// Gets or sets the dashboard base URL.
  /// </summary>
  [Required]
  [Url]
  public string DashboardUrl { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets whether unencrypted HTTP is allowed for a local private Docker network.
  /// </summary>
  public bool AllowInsecureHttp { get; set; }

  /// <summary>
  /// Gets or sets the enrollment token used only when no connector identity exists.
  /// </summary>
  public string EnrollmentToken { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the operator-facing server name.
  /// </summary>
  [Required]
  public string DisplayName { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the Pitcrew state root mounted read-only into the connector.
  /// </summary>
  [Required]
  public string StateRoot { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the persistent connector identity path.
  /// </summary>
  [Required]
  public string IdentityPath { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the initial polling interval before the dashboard provides a recommendation.
  /// </summary>
  [Range(5, 3600)]
  public int PollSeconds { get; set; } = 15;

  /// <summary>
  /// Gets or sets the maximum interval between successful heartbeats when state is unchanged.
  /// </summary>
  [Range(10, 3600)]
  public int HeartbeatSeconds { get; set; } = 30;

  /// <summary>
  /// Gets or sets the maximum accepted size of one observed-state document.
  /// </summary>
  [Range(1024, 16777216)]
  public int MaximumObservedStateBytes { get; set; } = 1048576;

  /// <summary>
  /// Gets or sets the maximum retry delay after transient synchronization failures.
  /// </summary>
  [Range(5, 3600)]
  public int MaximumBackoffSeconds { get; set; } = 300;

  /// <summary>
  /// Validates relationships between connector polling and heartbeat settings.
  /// </summary>
  /// <returns>Cross-property validation failures.</returns>
  public IEnumerable<ValidationError> Validate()
  {
    if (PollSeconds > HeartbeatSeconds)
    {
      yield return
          "HeartbeatSeconds must be greater than or equal to PollSeconds.";
    }
  }
}
