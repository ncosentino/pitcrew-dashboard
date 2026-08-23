namespace PitCrew.Dashboard.Features.Images.Abstractions;

/// <summary>
/// Describes the closed GitHub workflow activation state relevant to image registration.
/// </summary>
public enum GitHubWorkflowState
{
  /// <summary>The workflow is active.</summary>
  Active,

  /// <summary>The workflow was deleted.</summary>
  Deleted,

  /// <summary>The workflow is disabled for a fork.</summary>
  DisabledFork,

  /// <summary>The workflow is disabled because of inactivity.</summary>
  DisabledInactivity,

  /// <summary>The workflow was disabled manually.</summary>
  DisabledManually,

  /// <summary>GitHub returned an unrecognized state.</summary>
  Unknown,
}
