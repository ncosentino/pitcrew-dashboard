using System.Text.Json;

using PitCrew.Support.Protocol;

namespace PitCrew.Support.Agent.App.Tests;

public sealed class SupportRelayTransportContractTests
{
  [Test]
  public async Task Poll_Response_Opens_Web_Default_Envelope_Json()
  {
    var expected = new SupportEnvelope(
        SupportEnvelopeCryptography.EnvelopeVersion,
        SupportEnvelopeCryptography.ContentEncryptionAlgorithm,
        SupportEnvelopeCryptography.KeyWrapAlgorithm,
        SupportEnvelopeCryptography.SignatureAlgorithm,
        "dashboard",
        "node",
        "wrapped",
        "nonce",
        "ciphertext",
        "tag",
        "signature");
    var response = new AgentRelayPollResponse(
        Guid.NewGuid(),
        JsonSerializer.Serialize(
            expected,
            new JsonSerializerOptions(
                JsonSerializerDefaults.Web)),
        DateTimeOffset.UnixEpoch.AddMinutes(5));

    var actual = response.GetRequestEnvelopeOrNull();

    await Assert.That(actual)
        .IsEqualTo(expected);
  }
}
