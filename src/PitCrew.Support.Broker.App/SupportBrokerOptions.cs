using System.Globalization;
using Microsoft.Extensions.Configuration;
using PitCrew.Protocol;

namespace PitCrew.Support.Broker.App;

internal sealed record SupportBrokerOptions(
    string PitCrewRoot,
    IReadOnlyList<string> AllowedProfiles,
    string PipeName,
    string SocketPath,
    string? ExpectedAgentSid,
    string? BrokerServiceSid,
    uint? ExpectedAgentUid,
    uint? BrokerUid,
    uint? IpcGroupGid)
{
  public static SupportBrokerOptions? FromConfiguration(IConfiguration configuration)
  {
    var pitCrewRoot = configuration["PitCrewSupport:Broker:PitCrewRoot"];
    var allowedProfiles = (
        configuration["PitCrewSupport:Broker:AllowedProfiles"] ?? string.Empty)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.Ordinal)
        .ToArray();
    if (string.IsNullOrWhiteSpace(pitCrewRoot) ||
        allowedProfiles.Length == 0 ||
        allowedProfiles.Any(profile => !PitCrewProfileId.IsValid(profile)))
    {
      return null;
    }

    var expectedAgentUid = ParseUInt32(
        configuration["PitCrewSupport:Broker:ExpectedAgentUid"]);
    var brokerUid = ParseUInt32(configuration["PitCrewSupport:Broker:BrokerUid"]);
    var ipcGroupGid = ParseUInt32(
        configuration["PitCrewSupport:Broker:IpcGroupGid"]);
    var expectedAgentSid = configuration["PitCrewSupport:Broker:ExpectedAgentSid"];
    var brokerServiceSid = configuration["PitCrewSupport:Broker:BrokerServiceSid"];
    if (OperatingSystem.IsWindows())
    {
      if (string.IsNullOrWhiteSpace(expectedAgentSid) ||
          string.IsNullOrWhiteSpace(brokerServiceSid))
      {
        return null;
      }
    }
    else if (expectedAgentUid is null || brokerUid is null || ipcGroupGid is null)
    {
      return null;
    }

    return new SupportBrokerOptions(
        pitCrewRoot,
        allowedProfiles,
        configuration["PitCrewSupport:Broker:PipeName"] ??
            "pitcrew-support-broker-v1",
        configuration["PitCrewSupport:Broker:SocketPath"] ??
            "/run/pitcrew-support/broker.sock",
        expectedAgentSid,
        brokerServiceSid,
        expectedAgentUid,
        brokerUid,
        ipcGroupGid);
  }

  private static uint? ParseUInt32(string? value) =>
      uint.TryParse(
          value,
          NumberStyles.None,
          CultureInfo.InvariantCulture,
          out var parsed)
          ? parsed
          : null;
}
