namespace PitCrew.Support.Relay.App;

internal sealed record RelayOptions(
    string DatabasePath,
    string InternalBearerSecret)
{
  public static RelayOptions FromConfiguration(IConfiguration configuration) =>
      new(
          configuration["SupportRelay:DatabasePath"] ?? "data/support-relay.db",
          configuration["SupportRelay:InternalBearerSecret"] ?? string.Empty);
}
