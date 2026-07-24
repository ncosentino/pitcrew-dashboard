using System.Text.Json;

using PitCrew.Protocol;

namespace PitCrew.Connector.Features.Sync.Tests;

public sealed class ProtocolCompatibilityTests
{
  [Test]
  public async Task Protocol_Two_Payloads_Remain_Readable_Without_Capacity_Fields()
  {
    var request = JsonSerializer.Deserialize(
        """
        {
          "protocolVersion": 2,
          "connectorVersion": "2.0.0",
          "sentAt": "2026-07-24T12:00:00+00:00",
          "profiles": []
        }
        """,
        PitCrewProtocolJsonContext.Default.ConnectorSyncRequest);
    var response = JsonSerializer.Deserialize(
        """
        {
          "acceptedAt": "2026-07-24T12:00:00+00:00",
          "nextPollSeconds": 15,
          "credentialRotation": null
        }
        """,
        PitCrewProtocolJsonContext.Default.ConnectorSyncResponse);

    await Assert.That(request).IsNotNull();
    await Assert.That(request!.CapacityOperator).IsNull();
    await Assert.That(request.CapacityCommandOutcome).IsNull();
    await Assert.That(response).IsNotNull();
    await Assert.That(response!.CapacityCommand).IsNull();
  }
}
