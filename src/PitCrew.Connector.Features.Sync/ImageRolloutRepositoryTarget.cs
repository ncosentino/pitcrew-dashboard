namespace PitCrew.Connector.Features.Sync;

/// <summary>
/// Represents the single exact repository routing target for a repo-scope
/// rollout as read from the current desired-capacity document. Protocol
/// v11 only supports a single-target repo shape: PowerShell parameter
/// binding rejects a second <c>-AddRepos</c> switch, and adjacent /
/// comma-joined values also cannot represent multiple targets safely, so
/// projecting more than one repository entry is refused as
/// <c>unsupported-topology</c>. Later protocol revisions may add a
/// multi-target shape.
/// </summary>
/// <param name="Url">The exact repository URL as it appears in desired-capacity.json.</param>
/// <param name="Workers">
/// The worker count for this repository target. Zero represents a fully
/// paused single-target repo scope; any positive value represents an
/// active single-target repo scope. Negative counts are rejected upstream.
/// </param>
internal sealed record ImageRolloutRepositoryTarget(
    string Url,
    int Workers);
