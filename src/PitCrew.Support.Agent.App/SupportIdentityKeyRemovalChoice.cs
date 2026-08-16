namespace PitCrew.Support.Agent.App;

/// <summary>
/// Controls whether local support private keys are retained during explicit removal.
/// </summary>
public enum SupportIdentityKeyRemovalChoice
{
  /// <summary>Retain private keys while deleting transport and enrollment state.</summary>
  PreserveKeys,

  /// <summary>Delete both enrollment state and private keys.</summary>
  DeleteKeys,
}
