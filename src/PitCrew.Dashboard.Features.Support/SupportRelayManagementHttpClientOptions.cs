using NexusLabs.Needlr.Generators;

namespace PitCrew.Dashboard.Features.Support;

/// <summary>
/// Configures the internal Dashboard-to-relay management HTTP client.
/// </summary>
[HttpClientOptions("PitCrew:SupportPlane:RelayHttpClient", Name = ClientName)]
public sealed record SupportRelayManagementHttpClientOptions : IStandardHttpClientOptions
{
  /// <summary>Resolved named-client identifier.</summary>
  public const string ClientName = "SupportRelayManagement";

  /// <inheritdoc />
  public Uri BaseAddress { get; init; } = new("https://support-relay.example.com");

  /// <inheritdoc />
  public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

  /// <inheritdoc />
  public string UserAgent { get; init; } = "PitCrew-Dashboard-Support/1";

  /// <inheritdoc />
  public IReadOnlyDictionary<string, string> DefaultHeaders { get; init; } =
      new Dictionary<string, string>(StringComparer.Ordinal);
}
