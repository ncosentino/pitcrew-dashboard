using NexusLabs.Needlr.Generators;

namespace PitCrew.Support.Agent.App;

/// <summary>
/// Configures the support-agent outbound Dashboard identity client.
/// </summary>
[HttpClientOptions("PitCrewSupport:Agent:DashboardHttpClient", Name = ClientName)]
public sealed record SupportDashboardIdentityHttpClientOptions : IStandardHttpClientOptions
{
  /// <summary>Resolved named-client identifier.</summary>
  public const string ClientName = "SupportDashboardIdentity";

  /// <inheritdoc />
  public Uri BaseAddress { get; init; } =
      new("https://pitcrew-dashboard.example.com");

  /// <inheritdoc />
  public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

  /// <inheritdoc />
  public string UserAgent { get; init; } = "PitCrew-Support-Agent/1";

  /// <inheritdoc />
  public IReadOnlyDictionary<string, string> DefaultHeaders { get; init; } =
      new Dictionary<string, string>(StringComparer.Ordinal);
}
