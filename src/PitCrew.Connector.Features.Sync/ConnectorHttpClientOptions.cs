using NexusLabs.Needlr.Generators;

namespace PitCrew.Connector.Features.Sync;

/// <summary>Configures the connector's pooled outbound Dashboard transport.</summary>
[HttpClientOptions("PitCrew:Connector:HttpClient", Name = "PitCrewConnector")]
public sealed record ConnectorHttpClientOptions :
    INamedHttpClientOptions,
    IHttpClientTimeout
{
  /// <inheritdoc />
  public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
}
