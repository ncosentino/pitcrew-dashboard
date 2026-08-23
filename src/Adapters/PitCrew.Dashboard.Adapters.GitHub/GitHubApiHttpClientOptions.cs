using NexusLabs.Needlr.Generators;

namespace PitCrew.Dashboard.Adapters.GitHub;

/// <summary>
/// Configures the named HTTP client used only by the GitHub App adapter.
/// </summary>
[HttpClientOptions("PitCrew:Images:GitHubApp", Name = ClientName)]
public sealed record GitHubApiHttpClientOptions :
    INamedHttpClientOptions,
    IHttpClientBaseAddress,
    IHttpClientTimeout
{
  /// <summary>Resolved named-client identifier.</summary>
  public const string ClientName = "PitCrewGitHubApp";

  /// <inheritdoc />
  public Uri BaseAddress { get; init; } = new("https://api.github.com/");

  /// <inheritdoc />
  public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

}
