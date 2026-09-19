using System.Text.Json.Serialization;

namespace PitCrew.Protocol;

/// <summary>
/// Carries the bounded manager contract 12 durable operation journal.
/// </summary>
/// <remarks>
/// The journal retains failures, state transitions, retries, and recovery rather than every
/// reconciliation pass. An empty event list with a <c>current</c> status means no notable event has
/// occurred. Contract 22 keeps expected bounded eviction current, uses <c>truncated</c> only for
/// rejected evidence, and preserves pre-classification loss separately. An <c>unavailable</c>
/// status means the manager could not read or restore its journal.
/// </remarks>
/// <param name="Status">Journal availability: current, truncated, or unavailable.</param>
/// <param name="Capacity">Retention window the manager applies to the journal.</param>
/// <param name="HighestSequence">Highest retained sequence, or <see langword="null"/> when no event is retained.</param>
/// <param name="DroppedEvents">Compatibility total of evicted, rejected, and unclassified entries.</param>
/// <param name="Events">Retained events, deduplicated by profile and sequence.</param>
/// <param name="EvictedEvents">Entries deliberately evicted by bounded count or size retention, when classified.</param>
/// <param name="RejectedEvents">Malformed or unreadable entries the manager rejected, when classified.</param>
/// <param name="UnclassifiedEvents">Legacy discarded entries whose cause predates classified accounting.</param>
public sealed record ManagerOperationJournal(
    [property: JsonRequired] string Status,
    [property: JsonRequired] int Capacity,
    [property: JsonRequired] long? HighestSequence,
    [property: JsonRequired] int DroppedEvents,
    [property: JsonRequired] IReadOnlyList<ManagerEvent> Events,
    int? EvictedEvents = null,
    int? RejectedEvents = null,
    int? UnclassifiedEvents = null);
