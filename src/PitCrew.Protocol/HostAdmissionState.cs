using System.Text.Json.Serialization;

namespace PitCrew.Protocol;

/// <summary>
/// Describes one profile's credential-free view of host-local admission.
/// </summary>
/// <param name="Status">Evidence state: disabled, available, degraded, or unavailable.</param>
/// <param name="Namespace">Configured admission namespace, or <see langword="null"/> when disabled.</param>
/// <param name="Epoch">Coordinator policy epoch when measured.</param>
/// <param name="DecisionSequence">Latest coordinator decision sequence when measured.</param>
/// <param name="CapacityUnits">Configured host capacity before the safety margin.</param>
/// <param name="SafetyMarginUnits">Configured units withheld as a safety margin.</param>
/// <param name="EffectiveTotalUnits">Effective host admission budget.</param>
/// <param name="AvailableUnits">Host-wide units not currently held.</param>
/// <param name="HostPolicyFingerprint">Opaque identity of the applied host policy.</param>
/// <param name="Accounting">This profile's accounting, or <see langword="null"/> when unavailable.</param>
/// <param name="LastDecision">Latest bounded decision for this profile, when reported.</param>
public sealed record HostAdmissionState(
    [property: JsonRequired] string Status,
    [property: JsonRequired] string? Namespace,
    [property: JsonRequired] long? Epoch,
    [property: JsonRequired] long? DecisionSequence,
    [property: JsonRequired] int? CapacityUnits,
    [property: JsonRequired] int? SafetyMarginUnits,
    [property: JsonRequired] int? EffectiveTotalUnits,
    [property: JsonRequired] int? AvailableUnits,
    [property: JsonRequired] string? HostPolicyFingerprint,
    [property: JsonRequired] HostAdmissionAccounting? Accounting,
    [property: JsonRequired] HostAdmissionDecision? LastDecision);
