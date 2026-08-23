namespace PitCrew.Dashboard.Features.Images.Abstractions;

/// <summary>
/// Identifies one exact GitHub repository authorized to an installation.
/// </summary>
/// <param name="Id">GitHub numeric repository identity.</param>
/// <param name="Owner">Canonical repository owner.</param>
/// <param name="Name">Canonical repository name.</param>
public sealed record GitHubRepositoryIdentity(long Id, string Owner, string Name);
