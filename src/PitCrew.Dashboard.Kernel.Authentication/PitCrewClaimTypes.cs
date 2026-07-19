namespace PitCrew.Dashboard.Kernel.Authentication;

/// <summary>
/// Defines claims issued by dashboard authentication schemes.
/// </summary>
public static class PitCrewClaimTypes
{
  /// <summary>
  /// Identifies the operator's immutable GitHub user identifier.
  /// </summary>
  public const string GitHubUserId =
      "https://www.devleader.ca/projects/pitcrew/claims/github-user-id";

  /// <summary>
  /// Identifies the operator's current GitHub login.
  /// </summary>
  public const string GitHubLogin =
      "https://www.devleader.ca/projects/pitcrew/claims/github-login";

  /// <summary>
  /// Carries the operator's GitHub avatar URL when available.
  /// </summary>
  public const string AvatarUrl =
      "https://www.devleader.ca/projects/pitcrew/claims/avatar-url";
}
