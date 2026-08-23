namespace PitCrew.Dashboard.Features.Images.Abstractions;

/// <summary>
/// Identifies one exact Git commit in a registered repository.
/// </summary>
/// <param name="Sha">Exact lowercase 40-character commit SHA.</param>
public sealed record GitHubCommitIdentity(string Sha);
