using System.Text.Json.Serialization;

namespace PitCrew.Protocol;

/// <summary>
/// Carries manager contract 12 subsystem health for the operations one manager performed.
/// </summary>
/// <param name="Docker">Health of the manager's local Docker operations.</param>
/// <param name="Github">Health of the manager's GitHub control-plane operations.</param>
public sealed record ManagerSubsystemHealth(
    [property: JsonRequired] SubsystemHealthSummary Docker,
    [property: JsonRequired] SubsystemHealthSummary Github);
