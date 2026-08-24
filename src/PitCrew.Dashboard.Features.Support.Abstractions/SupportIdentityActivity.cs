namespace PitCrew.Dashboard.Features.Support.Abstractions;

/// <summary>
/// Projects bounded relay-observed activity for one support identity.
/// </summary>
/// <param name="NodeId">Support node identifier.</param>
/// <param name="LastPollAt">Most recent accepted relay poll.</param>
/// <param name="LastResultAt">Most recent accepted relay result upload.</param>
public sealed record SupportIdentityActivity(
    Guid NodeId,
    DateTimeOffset? LastPollAt,
    DateTimeOffset? LastResultAt);
