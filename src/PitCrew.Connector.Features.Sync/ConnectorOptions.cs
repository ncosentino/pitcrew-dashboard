using System.ComponentModel.DataAnnotations;

using NexusLabs.Needlr.Generators;

using PitCrew.Protocol;

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
  /// Gets or sets the one-time code used for initial enrollment or explicit re-enrollment.
  /// </summary>
  public string EnrollmentCode { get; set; } = string.Empty;

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
  /// Gets or sets whether typed capacity operations may execute on this host.
  /// </summary>
  public bool OperatorModeEnabled { get; set; }

  /// <summary>
  /// Gets or sets the local PitCrew checkout used to resolve the setup script.
  /// </summary>
  public string PitCrewRoot { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the profile allowlist for capacity operations.
  /// </summary>
  public string[] AllowedCapacityProfiles { get; set; } = [];

  /// <summary>
  /// Gets or sets the local maximum accepted for any capacity command.
  /// </summary>
  [Range(1, 1_000_000)]
  public int CapacityMaximumCeiling { get; set; } = 100;

  /// <summary>
  /// Gets or sets the maximum duration of one local setup invocation.
  /// </summary>
  [Range(30, 3600)]
  public int CapacityCommandTimeoutSeconds { get; set; } = 300;

  /// <summary>
  /// Gets or sets the locally configured PowerShell executable.
  /// </summary>
  public string PowerShellExecutable { get; set; } = "pwsh";

  /// <summary>
  /// Gets or sets whether typed manager-recovery operations may execute on this host.
  /// </summary>
  public bool ManagerRecoveryEnabled { get; set; }

  /// <summary>
  /// Gets or sets the profile allowlist for manager-recovery operations.
  /// </summary>
  public string[] AllowedManagerRecoveryProfiles { get; set; } = [];

  /// <summary>
  /// Gets or sets the maximum duration of one local recovery invocation.
  /// </summary>
  [Range(30, 600)]
  public int RecoveryCommandTimeoutSeconds { get; set; } = 120;

  /// <summary>
  /// Gets or sets the longest command lifetime this connector accepts.
  /// </summary>
  [Range(60, 86400)]
  public int RecoveryCommandMaximumExpirySeconds { get; set; } = 900;

  /// <summary>
  /// Gets or sets the oldest observed state accepted for recovery.
  /// </summary>
  [Range(30, 3600)]
  public int RecoveryObservedStateMaximumAgeSeconds { get; set; } = 300;

  /// <summary>
  /// Gets or sets the protected directory holding the recovery execution ledger.
  /// </summary>
  public string RecoveryLedgerPath { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets whether typed profile-image rollout operations may execute
  /// on this host.
  /// </summary>
  public bool ImageRolloutEnabled { get; set; }

  /// <summary>
  /// Gets or sets the profile allowlist for profile-image rollout operations.
  /// </summary>
  public string[] AllowedImageRolloutProfiles { get; set; } = [];

  /// <summary>
  /// Gets or sets the closed recipe-to-registry-repository policy. Entries are
  /// approved recipe identifiers paired with strict registry repositories
  /// (no scheme, credentials, tag, digest, whitespace, or control characters).
  /// Modeled as an indexed collection so hyphenated recipe identifiers survive
  /// Linux systemd environment variable naming, which forbids hyphens in keys.
  /// </summary>
  public IList<ImageRolloutRecipePolicyEntry> ImageRolloutRecipes { get; set; } =
      new List<ImageRolloutRecipePolicyEntry>();

  /// <summary>
  /// Gets or sets the protected directory holding reconstructed rollout
  /// manifests and the rollout execution ledger.
  /// </summary>
  public string ImageRolloutStatePath { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the maximum duration of one local image rollout invocation.
  /// </summary>
  [Range(60, 3600)]
  public int ImageRolloutCommandTimeoutSeconds { get; set; } = 600;

  /// <summary>
  /// Gets or sets the longest rollout command lifetime this connector accepts.
  /// </summary>
  [Range(60, 86400)]
  public int ImageRolloutCommandMaximumExpirySeconds { get; set; } = 1800;

  /// <summary>
  /// Gets or sets the oldest observed state accepted for rollout evidence.
  /// </summary>
  [Range(30, 3600)]
  public int ImageRolloutObservedStateMaximumAgeSeconds { get; set; } = 300;

  /// <summary>
  /// Gets or sets the maximum reconstructed local manifests retained under
  /// the rollout state path.
  /// </summary>
  [Range(4, 128)]
  public int ImageRolloutRetainedManifests { get; set; } = 16;

  /// <summary>
  /// Validates relationships between connector polling, heartbeat, and local
  /// operator policy settings.
  /// </summary>
  /// <returns>Cross-property validation failures.</returns>
  public IEnumerable<ValidationError> Validate()
  {
    if (PollSeconds > HeartbeatSeconds)
    {
      yield return
          "HeartbeatSeconds must be greater than or equal to PollSeconds.";
    }
    if (OperatorModeEnabled)
    {
      foreach (var error in ValidateLocalOperationPolicy(
          "OperatorModeEnabled",
          "AllowedCapacityProfiles",
          AllowedCapacityProfiles))
      {
        yield return error;
      }
    }
    if (ManagerRecoveryEnabled)
    {
      foreach (var error in ValidateLocalOperationPolicy(
          "ManagerRecoveryEnabled",
          "AllowedManagerRecoveryProfiles",
          AllowedManagerRecoveryProfiles))
      {
        yield return error;
      }
      if (string.IsNullOrWhiteSpace(RecoveryLedgerPath))
      {
        yield return
            "RecoveryLedgerPath is required when ManagerRecoveryEnabled is true.";
      }
    }
    if (!ImageRolloutEnabled)
    {
      yield break;
    }
    foreach (var error in ValidateLocalOperationPolicy(
        "ImageRolloutEnabled",
        "AllowedImageRolloutProfiles",
        AllowedImageRolloutProfiles))
    {
      yield return error;
    }
    if (string.IsNullOrWhiteSpace(ImageRolloutStatePath))
    {
      yield return
          "ImageRolloutStatePath is required when ImageRolloutEnabled is true.";
    }
    else if (!Path.IsPathFullyQualified(ImageRolloutStatePath))
    {
      // The rollout state root must never resolve against the connector's
      // current working directory at process start: a relative or drive-
      // relative value could be repositioned by the process launcher and
      // silently redirect ledger/manifest writes to an attacker-influenced
      // location.
      yield return
          "ImageRolloutStatePath must be an absolute path when ImageRolloutEnabled is true.";
    }
    if (ImageRolloutRecipes.Count == 0)
    {
      yield return
          "ImageRolloutRecipes must map every allowed recipe when ImageRolloutEnabled is true.";
    }
    var seenRecipeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var entry in ImageRolloutRecipes)
    {
      if (entry is null ||
          string.IsNullOrWhiteSpace(entry.RecipeId) ||
          !ImageRolloutRecipePolicy.IsValidRecipeId(entry.RecipeId))
      {
        yield return
            "ImageRolloutRecipes entries must expose a strict recipe identifier.";
        yield break;
      }
      if (!ImageRolloutRecipePolicy.IsValidRegistryRepository(
          entry.RegistryRepository))
      {
        yield return
            $"ImageRolloutRecipes entry for recipe '{entry.RecipeId}' must expose a strict registry repository (no scheme, credentials, tag, digest, whitespace, or control characters).";
        yield break;
      }
      if (!seenRecipeIds.Add(entry.RecipeId))
      {
        yield return
            "ImageRolloutRecipes entries must expose case-insensitively unique recipe identifiers.";
        yield break;
      }
    }
  }

  private IEnumerable<ValidationError> ValidateLocalOperationPolicy(
      string modeName,
      string allowlistName,
      string[] allowlist)
  {
    if (string.IsNullOrWhiteSpace(PitCrewRoot))
    {
      yield return $"PitCrewRoot is required when {modeName} is true.";
    }
    if (string.IsNullOrWhiteSpace(PowerShellExecutable))
    {
      yield return $"PowerShellExecutable is required when {modeName} is true.";
    }
    if (allowlist.Length == 0)
    {
      yield return
          $"{allowlistName} requires at least one profile when {modeName} is true.";
    }
    var seenProfiles = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase);
    foreach (var profile in allowlist)
    {
      if (!IsValidProfileId(profile) ||
          !seenProfiles.Add(profile))
      {
        yield return
            $"{allowlistName} must contain unique PitCrew profile identifiers.";
        yield break;
      }
    }
  }

  private static bool IsValidProfileId(string profileId)
      => PitCrewProfileId.IsValid(profileId);
}
