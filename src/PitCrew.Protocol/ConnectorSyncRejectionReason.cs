namespace PitCrew.Protocol;

/// <summary>Bounded, non-sensitive reasons for rejecting a connector synchronization.</summary>
public enum ConnectorSyncRejectionReason
{
  /// <summary>No supported reason was supplied by the remote endpoint.</summary>
  Unspecified,
  /// <summary>The connector protocol is unsupported.</summary>
  Protocol,
  /// <summary>The connector version is invalid.</summary>
  ConnectorVersion,
  /// <summary>The profile count is invalid.</summary>
  ProfileCount,
  /// <summary>Profile inventory coverage is invalid.</summary>
  ProfileInventory,
  /// <summary>An observation violates the shared profile contract.</summary>
  Profile,
  /// <summary>Profile identifiers are duplicated.</summary>
  DuplicateProfile,
  /// <summary>A manager contract requires a newer connector protocol.</summary>
  ManagerContract,
  /// <summary>Capacity operation state is invalid or unsupported.</summary>
  Capacity,
  /// <summary>Recovery operation state is invalid or unsupported.</summary>
  Recovery,
  /// <summary>Inventory timestamps exceed the accepted clock skew.</summary>
  InventoryClockSkew,
  /// <summary>Health replay state is invalid or unsupported.</summary>
  HealthReplay,
  /// <summary>Image rollout state is invalid or unsupported.</summary>
  ImageRollout,
}
