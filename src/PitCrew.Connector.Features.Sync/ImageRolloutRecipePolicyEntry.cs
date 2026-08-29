using System.ComponentModel.DataAnnotations;

namespace PitCrew.Connector.Features.Sync;

/// <summary>
/// Represents one closed recipe-to-registry-repository policy entry configured
/// on the local host connector for typed profile-image rollout.
/// </summary>
public sealed class ImageRolloutRecipePolicyEntry
{
  /// <summary>
  /// Gets or sets the approved recipe identifier this entry authorizes.
  /// </summary>
  [Required]
  public string RecipeId { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the strict registry repository the connector is willing to
  /// pull for this recipe (no scheme, credentials, tag, digest, whitespace,
  /// or control characters).
  /// </summary>
  [Required]
  public string RegistryRepository { get; set; } = string.Empty;
}
