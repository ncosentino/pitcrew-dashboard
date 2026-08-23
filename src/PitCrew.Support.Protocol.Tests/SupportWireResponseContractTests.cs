using System.Text.Json;

using PitCrew.Support.Protocol;

namespace PitCrew.Support.Protocol.Tests;

public sealed class SupportWireResponseContractTests
{
  private static readonly JsonSerializerOptions _jsonOptions =
      new(JsonSerializerDefaults.Web);

  [Test]
  public async Task Enrollment_Response_Uses_Pinned_V1_Property_Names()
  {
    var response = new SupportEnrollmentCompletionResponse(
        "11111111-1111-4111-8111-111111111111",
        "Support node",
        CreateEnvelope(),
        "https://relay.example.com/",
        "authorization-key",
        "result-key");

    var propertyNames = JsonSerializer.SerializeToElement(
        response,
        _jsonOptions)
        .EnumerateObject()
        .Select(property => property.Name)
        .ToArray();

    await Assert.That(propertyNames).IsEquivalentTo([
      "nodeId",
      "displayName",
      "transportCredentialEnvelope",
      "relayUrl",
      "authorizationSigningPublicKeySpki",
      "resultEncryptionPublicKeySpki",
    ]);
  }

  [Test]
  public async Task Identity_Response_Uses_Pinned_V1_Property_Names()
  {
    var response = new SupportIdentityCompletionResponse(
        "11111111-1111-4111-8111-111111111111",
        "Support node",
        "transport-credential",
        "https://relay.example.com/",
        "authorization-key",
        "result-key");

    var propertyNames = JsonSerializer.SerializeToElement(
        response,
        _jsonOptions)
        .EnumerateObject()
        .Select(property => property.Name)
        .ToArray();

    await Assert.That(propertyNames).IsEquivalentTo([
      "nodeId",
      "displayName",
      "transportCredential",
      "relayUrl",
      "authorizationSigningPublicKeySpki",
      "resultEncryptionPublicKeySpki",
    ]);
  }

  private static SupportEnvelope CreateEnvelope() =>
      new(
          "1",
          "A256GCM",
          "RSA-OAEP-256",
          "ES256",
          "dashboard-support-auth-v1",
          "11111111111141118111111111111111",
          "wrapped-key",
          "nonce",
          "ciphertext",
          "tag",
          "signature");
}
