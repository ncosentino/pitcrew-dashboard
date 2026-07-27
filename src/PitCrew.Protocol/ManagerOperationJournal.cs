using System.Text.Json.Serialization;

namespace PitCrew.Protocol;

/// <summary>
/// Carries the bounded manager contract 12 durable operation journal.
/// </summary>
/// <remarks>
/// The journal retains failures, state transitions, retries, and recovery rather than every
/// reconciliation pass. An empty event list with a <c>current</c> status means no notable event has
/// occurred; a <c>truncated</c> status means older or rejected entries were discarded; an
/// <c>unavailable</c> status means the manager could not read or restore its journal.
/// </remarks>
/// <param name="Status">Journal availability: current, truncated, or unavailable.</param>
/// <param name="Capacity">Retention window the manager applies to the journal.</param>
/// <param name="HighestSequence">Highest retained sequence, or <see langword="null"/> when no event is retained.</param>
/// <param name="DroppedEvents">Number of entries the manager discarded from the retained window.</param>
/// <param name="Events">Retained events, deduplicated by profile and sequence.</param>
public sealed record ManagerOperationJournal(
    [property: JsonRequired] string Status,
    [property: JsonRequired] int Capacity,
    [property: JsonRequired] long? HighestSequence,
    [property: JsonRequired] int DroppedEvents,
    [property: JsonRequired] IReadOnlyList<ManagerEvent> Events);
