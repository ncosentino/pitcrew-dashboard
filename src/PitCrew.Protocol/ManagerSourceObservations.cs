namespace PitCrew.Protocol;

/// <summary>
/// Groups manager contract 21 provenance by independent source family.
/// </summary>
public sealed record ManagerSourceObservations(
    ManagerSourceObservation LocalRuntime,
    ManagerSourceObservation GitHubScaleSet,
    ManagerSourceObservation ResourceTelemetry,
    ManagerSourceObservation HostHardware,
    ManagerSourceObservation HostAdmission,
    ManagerSourceObservation SubsystemHealth,
    ManagerSourceObservation Capacity,
    ManagerSourceObservation Workload);
