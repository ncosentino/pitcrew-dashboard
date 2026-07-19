using System.ComponentModel.DataAnnotations;

using NexusLabs.Needlr.Generators;

namespace PitCrew.Dashboard.Kernel.Authentication;

/// <summary>
/// Configures dashboard operator authentication and persistent cookie protection.
/// </summary>
[Options("PitCrew:Authentication", ValidateOnStart = true)]
public sealed class DashboardAuthenticationOptions
{
  /// <summary>
  /// Gets or sets the authentication mechanism used by the deployment.
  /// </summary>
  public DashboardAuthenticationMode Mode { get; set; } =
      DashboardAuthenticationMode.GitHub;

  /// <summary>
  /// Gets or sets the GitHub OAuth App client identifier.
  /// </summary>
  public string GitHubClientId { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the GitHub OAuth App client secret.
  /// </summary>
  public string GitHubClientSecret { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets immutable GitHub user identifiers with deployment-wide administration access.
  /// </summary>
  public string[] SystemAdministratorGitHubIds { get; set; } = [];

  /// <summary>
  /// Gets or sets the directory used to persist ASP.NET Core data-protection keys.
  /// </summary>
  [Required]
  public string DataProtectionKeyPath { get; set; } =
      "data/data-protection-keys";

  /// <summary>
  /// Gets or sets the synthetic GitHub user identifier used by development authentication.
  /// </summary>
  [Required]
  public string DevelopmentGitHubUserId { get; set; } = "0";

  /// <summary>
  /// Gets or sets the synthetic GitHub login used by development authentication.
  /// </summary>
  [Required]
  public string DevelopmentGitHubLogin { get; set; } = "local-operator";

  /// <summary>
  /// Gets or sets the display name used by development authentication.
  /// </summary>
  [Required]
  public string DevelopmentDisplayName { get; set; } = "Local operator";

  /// <summary>
  /// Gets or sets the avatar URL used by development authentication.
  /// </summary>
  public string DevelopmentAvatarUrl { get; set; } = string.Empty;

  /// <summary>
  /// Validates mode-specific authentication requirements.
  /// </summary>
  /// <returns>Configuration validation failures.</returns>
  public IEnumerable<ValidationError> Validate()
  {
    if (Mode == DashboardAuthenticationMode.GitHub)
    {
      if (string.IsNullOrWhiteSpace(GitHubClientId))
      {
        yield return "GitHubClientId is required when Mode is GitHub.";
      }
      if (string.IsNullOrWhiteSpace(GitHubClientSecret))
      {
        yield return "GitHubClientSecret is required when Mode is GitHub.";
      }
      if (SystemAdministratorGitHubIds.Length == 0)
      {
        yield return
            "At least one SystemAdministratorGitHubIds entry is required when Mode is GitHub.";
      }
    }

    foreach (var githubUserId in SystemAdministratorGitHubIds)
    {
      if (!IsGitHubUserId(githubUserId))
      {
        yield return
            $"System administrator GitHub user ID '{githubUserId}' must contain only decimal digits.";
      }
    }
    if (!IsGitHubUserId(DevelopmentGitHubUserId))
    {
      yield return
          "DevelopmentGitHubUserId must contain only decimal digits.";
    }
  }

  private static bool IsGitHubUserId(string value) =>
      !string.IsNullOrWhiteSpace(value) &&
      value.All(character => character is >= '0' and <= '9');
}
