namespace PitCrew.Support.Agent.App;

/// <summary>
/// Describes the local support identity lifecycle without exposing credentials or private keys.
/// </summary>
public enum SupportNodeIdentityLifecycle
{
  /// <summary>No support identity exists.</summary>
  Missing,

  /// <summary>Local keys exist and are waiting for one-time Dashboard enrollment.</summary>
  PendingEnrollment,

  /// <summary>The identity is enrolled and may poll the relay.</summary>
  Active,

  /// <summary>The operator disabled the enrolled identity locally.</summary>
  Disabled,

  /// <summary>The relay rejected the enrolled credential, including after revocation.</summary>
  AuthorizationRejected,

  /// <summary>Private keys were explicitly preserved while enrollment state was removed.</summary>
  KeysPreserved,

  /// <summary>Local state exists but failed integrity or permission validation.</summary>
  Invalid,

  /// <summary>A replacement key set is staged while the current identity remains active.</summary>
  RotationStaged,
}
