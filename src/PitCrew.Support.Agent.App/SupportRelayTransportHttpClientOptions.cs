using NexusLabs.Needlr.Generators;

namespace PitCrew.Support.Agent.App;

/// <summary>
/// Configures the support-agent outbound relay HTTP client.
/// </summary>
[HttpClientOptions("PitCrewSupport:Agent:RelayHttpClient", Name = ClientName)]
public sealed record SupportRelayTransportHttpClientOptions : IStandardHttpClientOptions
{
  /// <summary>Resolved named-client identifier.</summary>
  public const string ClientName = "SupportRelayTransport";

  /// <inheritdoc />
  public Uri BaseAddress { get; init; } = new("https://support-relay.example.com");

  /// <inheritdoc />
  public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

  /// <inheritdoc />
  public string UserAgent { get; init; } = "PitCrew-Support-Agent/1";

  /// <inheritdoc />
  public IReadOnlyDictionary<string, string> DefaultHeaders { get; init; } =
      new Dictionary<string, string>(StringComparer.Ordinal);
}
