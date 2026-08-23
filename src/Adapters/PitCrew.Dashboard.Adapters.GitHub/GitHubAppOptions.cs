using NexusLabs.Needlr.Generators;

namespace PitCrew.Dashboard.Adapters.GitHub;

/// <summary>
/// Configures the GitHub App boundary used for trusted image workflows.
/// </summary>
[Options(
    "PitCrew:Images:GitHubApp",
    ValidateOnStart = true,
    Validator = typeof(GitHubAppOptionsValidator))]
public sealed class GitHubAppOptions
{
  /// <summary>
  /// Gets or sets whether the optional GitHub App image-workflow integration is enabled.
  /// </summary>
  public bool Enabled { get; set; }

  /// <summary>Gets or sets the positive GitHub App identifier.</summary>
  public long AppId { get; set; }

  /// <summary>Gets or sets the exact existing local PEM private-key path.</summary>
  public string PrivateKeyPath { get; set; } = string.Empty;

  /// <summary>Gets or sets the HTTPS GitHub API base URI.</summary>
  public Uri BaseAddress { get; set; } = new("https://api.github.com/");

  /// <summary>Gets or sets the bounded timeout for one GitHub HTTP request.</summary>
  public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}
