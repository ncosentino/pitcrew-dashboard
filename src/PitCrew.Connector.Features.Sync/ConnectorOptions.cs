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
    if (!ManagerRecoveryEnabled)
    {
      yield break;
    }
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
  {
    if (profileId.Length is < 1 or > 32 ||
        profileId[0] is < 'a' or > 'z')
    {
      return false;
    }
    return profileId.All(character =>
        character is >= 'a' and <= 'z' or
            >= '0' and <= '9' or
            '-');
  }
}
