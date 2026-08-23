using System.Net;
using System.Net.Http.Json;

using PitCrew.Support.Protocol;

namespace PitCrew.Support.Agent.App.Tests;

public sealed class SupportDashboardIdentityClientTests
{
  [Test]
  public async Task Enrollment_Consumes_Shared_Wire_Response(
      CancellationToken cancellationToken)
  {
    var nodeId = Guid.NewGuid();
    var envelope = CreateEnvelope(nodeId);
    var client = CreateClient(new SupportEnrollmentCompletionResponse(
        nodeId.ToString("D"),
        "Support node",
        envelope,
        "https://relay.example.com/",
        "authorization-key",
        "result-key"));

    var completion = await client.CompleteEnrollmentAsync(
        new Uri("https://dashboard.example.com/"),
        "tenant-a",
        "pcs_enroll_fixture-code-abcdefghijklmnopqrstuvwxyz",
        Guid.NewGuid(),
        CreateNodeKeys(),
        cancellationToken);

    await Assert.That(completion).IsNotNull();
    await Assert.That(completion!.NodeId).IsEqualTo(nodeId);
    await Assert.That(completion.DashboardAuthorizationSigningPublicKeySpki)
        .IsEqualTo("authorization-key");
    await Assert.That(completion.DashboardResultEncryptionPublicKeySpki)
        .IsEqualTo("result-key");
    await Assert.That(completion.TransportCredentialEnvelope)
        .IsEqualTo(envelope);
  }

  [Test]
  public async Task Rotation_Consumes_Shared_Wire_Response(
      CancellationToken cancellationToken)
  {
    var nodeId = Guid.NewGuid();
    var client = CreateClient(new SupportIdentityCompletionResponse(
        nodeId.ToString("D"),
        "Support node",
        "replacement-credential-abcdefghijklmnopqrstuvwxyz",
        "https://relay.example.com/",
        "authorization-key",
        "result-key"));
    var plan = new SupportIdentityRotationPlan(
        Guid.NewGuid(),
        nodeId,
        "tenant-a",
        "https://dashboard.example.com/",
        "current-credential-abcdefghijklmnopqrstuvwxyz",
        "replacement-credential-abcdefghijklmnopqrstuvwxyz",
        "node-signing-key",
        "node-encryption-key");

    var completion = await client.PrepareRotationAsync(
        plan,
        cancellationToken);

    await Assert.That(completion).IsNotNull();
    await Assert.That(completion!.NodeId).IsEqualTo(nodeId);
    await Assert.That(completion.DashboardAuthorizationSigningPublicKeySpki)
        .IsEqualTo("authorization-key");
    await Assert.That(completion.DashboardResultEncryptionPublicKeySpki)
        .IsEqualTo("result-key");
  }

  [Test]
  public async Task Enrollment_Rejects_Incomplete_Shared_Response(
      CancellationToken cancellationToken)
  {
    var nodeId = Guid.NewGuid();
    var client = CreateClient(new
    {
      NodeId = nodeId.ToString("D"),
      DisplayName = "Support node",
      TransportCredentialEnvelope = CreateEnvelope(nodeId),
      RelayUrl = "https://relay.example.com/",
      AuthorizationSigningPublicKeySpki = (string?)null,
      ResultEncryptionPublicKeySpki = "result-key",
    });

    var completion = await client.CompleteEnrollmentAsync(
        new Uri("https://dashboard.example.com/"),
        "tenant-a",
        "pcs_enroll_fixture-code-abcdefghijklmnopqrstuvwxyz",
        Guid.NewGuid(),
        CreateNodeKeys(),
        cancellationToken);

    await Assert.That(completion).IsNull();
  }

  private static SupportDashboardIdentityClient CreateClient(
      object response) =>
      new(
          new TestHttpClientFactory(
              SupportDashboardIdentityHttpClientOptions.ClientName,
              _ => new HttpResponseMessage(HttpStatusCode.OK)
              {
                Content = JsonContent.Create(response),
              }));

  private static SupportNodeKeyDescriptor CreateNodeKeys() =>
      new(
          "test",
          "keys",
          "signing-reference",
          "encryption-reference",
          "signing-public-key",
          "encryption-public-key");

  private static SupportEnvelope CreateEnvelope(Guid nodeId) =>
      new(
          "1",
          "A256GCM",
          "RSA-OAEP-256",
          "ES256",
          "dashboard-support-auth-v1",
          nodeId.ToString("N"),
          "wrapped-key",
          "nonce",
          "ciphertext",
          "tag",
          "signature");
}
