namespace PitCrew.Connector.Features.Sync;

/// <summary>
/// Describes the locally observed recovery state of one profile.
/// </summary>
/// <param name="ProfileId">Locally resolved profile identifier.</param>
/// <param name="ManagerContractVersion">Runtime compatibility contract implemented by the manager.</param>
/// <param name="ManagerContractSupported">Whether local recovery supports that contract.</param>
/// <param name="ManagerInstanceId">Manager instance currently owning the profile.</param>
/// <param name="Generation">Accepted desired-capacity generation.</param>
/// <param name="DesiredStateHash">Accepted desired-state hash, or <see langword="null"/>.</param>
/// <param name="ObservedStateAgeSeconds">Age of the locally readable observed state.</param>
/// <param name="RecoveryAllowed">Whether local policy allows recovery for the profile.</param>
/// <param name="SingleManagerResolved">Whether exactly one running manager is locally resolvable.</param>
/// <param name="OperationActive">Whether another local profile operation is running.</param>
internal sealed record RecoveryProfileState(
    string ProfileId,
    int ManagerContractVersion,
    bool ManagerContractSupported,
    string ManagerInstanceId,
    int Generation,
    string? DesiredStateHash,
    int ObservedStateAgeSeconds,
    bool RecoveryAllowed,
    bool SingleManagerResolved,
    bool OperationActive);
