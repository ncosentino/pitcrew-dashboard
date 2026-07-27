using System.Text.Json.Serialization;

namespace PitCrew.Protocol;

/// <summary>
/// Carries manager contract 12 capacity-deficit evidence for one profile.
/// </summary>
/// <param name="Fixed">Fixed-profile deficit evidence, or <see langword="null"/> for an autoscaled profile.</param>
/// <param name="Targets">Per-target deficit evidence, which is empty for a fixed-capacity profile.</param>
public sealed record ManagerCapacityEvidence(
    [property: JsonRequired] CapacityDeficitEvidence? Fixed,
    [property: JsonRequired] IReadOnlyList<TargetCapacityDeficitEvidence> Targets);
