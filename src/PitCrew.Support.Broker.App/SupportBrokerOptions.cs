using Microsoft.Extensions.Configuration;

namespace PitCrew.Support.Broker.App;

internal sealed record SupportBrokerOptions(
    string PitCrewRoot,
    string PipeName)
{
  public static SupportBrokerOptions FromConfiguration(IConfiguration configuration) =>
      new(
          configuration["PitCrewSupport:Broker:PitCrewRoot"] ?? string.Empty,
          configuration["PitCrewSupport:Broker:PipeName"] ?? "pitcrew-support-broker-v1");
}

