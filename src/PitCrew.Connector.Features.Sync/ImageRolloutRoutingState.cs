namespace PitCrew.Connector.Features.Sync;

/// <summary>
/// Represents the locally read routing/capacity/pause values needed to build
/// a Setup-Runner invocation that preserves the currently applied routing
/// state.
/// </summary>
/// <param name="Scope">Local routing scope: <c>repo</c>, <c>org</c>, or <c>ent</c>.</param>
/// <param name="Paused">
/// True if the current desired document represents a fully paused profile
/// (repo scope with all target counts zero, or org/ent with replicas zero).
/// </param>
/// <param name="RepositoryTargets">
/// Ordered exact list of repository routing targets when <see cref="Scope"/>
/// is <c>repo</c>; empty for other scopes. Protocol v11 requires exactly
/// one entry for repo scope (positive-count for active, zero-count for
/// paused); multi-repository shapes cannot be represented safely on the
/// Setup-Runner PowerShell CLI (repeated <c>-AddRepos</c> is rejected as
/// "specified more than once", adjacent values bind only the first,
/// and comma-joined values are treated as a single string), so they are
/// projected as <c>unsupported-topology</c> at the routing boundary. This
/// mirrors the existing capacity protocol's single-target invariant.
/// </param>
/// <param name="Organization">
/// The local organization identity when <see cref="Scope"/> is <c>org</c>;
/// otherwise empty.
/// </param>
/// <param name="Enterprise">
/// The local enterprise identity when <see cref="Scope"/> is <c>ent</c>;
/// otherwise empty.
/// </param>
/// <param name="Replicas">
/// The local replicas count for org/ent scope; null for repo scope or when
/// the current desired document is paused.
/// </param>
internal sealed record ImageRolloutRoutingState(
    string Scope,
    bool Paused,
    IReadOnlyList<ImageRolloutRepositoryTarget> RepositoryTargets,
    string Organization,
    string Enterprise,
    int? Replicas);
