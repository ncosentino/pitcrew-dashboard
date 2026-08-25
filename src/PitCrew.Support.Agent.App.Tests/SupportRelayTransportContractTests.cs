using System.Text.Json;

using PitCrew.Support.Protocol;

namespace PitCrew.Support.Agent.App.Tests;

public sealed class SupportRelayTransportContractTests
{
  [Test]
  public async Task Poll_Response_Preserves_Web_Default_Envelope_Json()
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
    var serializedEnvelope = JsonSerializer.Serialize(
        expected,
        new JsonSerializerOptions(
            JsonSerializerDefaults.Web));
    var response = new AgentRelayPollResponse(
        Guid.NewGuid(),
        serializedEnvelope,
        DateTimeOffset.UnixEpoch.AddMinutes(5));
    var json = JsonSerializer.Serialize(
        response,
        new JsonSerializerOptions(
            JsonSerializerDefaults.Web));

    var actual = JsonSerializer.Deserialize<AgentRelayPollResponse>(
        json,
        new JsonSerializerOptions(
            JsonSerializerDefaults.Web));

    await Assert.That(actual).IsNotNull();
    await Assert.That(actual!.RequestEnvelope)
        .IsEqualTo(serializedEnvelope);
  }
}
