namespace PitCrew.Dashboard.Kernel.ImageRollouts;

/// <summary>
/// Carries the immutable candidate authority the caller resolved.
/// </summary>
/// <param name="CandidateId">Approved candidate identity.</param>
/// <param name="RecipeId">Recipe identifier the immutable candidate reports.</param>
/// <param name="TargetDigest">Immutable lowercase <c>sha256:</c>-prefixed image digest.</param>
/// <param name="TargetPlatform">Closed candidate platform (<c>linux/amd64</c> or <c>linux/arm64</c>).</param>
public sealed record ImageRolloutCandidateAuthority(
    Guid CandidateId,
    string RecipeId,
    string TargetDigest,
    string TargetPlatform);
