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
  [Arguments(503, "http-rejected-503")]
  [Arguments(200, "response-invalid")]
  public async Task Rotation_Reports_Bounded_Http_And_Response_Failures(
      int statusCode,
      string expectedDisposition,
      CancellationToken cancellationToken)
  {
    var client = new SupportDashboardIdentityClient(
        new TestHttpClientFactory(
            SupportDashboardIdentityHttpClientOptions.ClientName,
            _ => new HttpResponseMessage((HttpStatusCode)statusCode)
            {
              Content = new StringContent("{invalid-json"),
            }),
        TimeProvider.System);
    var plan = new SupportIdentityRotationPlan(
        Guid.NewGuid(), Guid.NewGuid(), "tenant-a",
        "https://dashboard.example.com/",
        "current-credential-abcdefghijklmnopqrstuvwxyz",
        "replacement-credential-abcdefghijklmnopqrstuvwxyz",
        "node-signing-key", "node-encryption-key");

    var prepared = await client.PrepareRotationWithOutcomeAsync(plan, cancellationToken);
    var finalized = await client.FinalizeRotationWithOutcomeAsync(
        new PendingSupportIdentityRotation(
            plan.RotationId, plan.NodeId, plan.TenantId, plan.DashboardUrl,
            plan.CurrentTransportCredential),
        cancellationToken);

    await Assert.That(prepared.Completion).IsNull();
    await Assert.That(prepared.FailureDisposition).IsEqualTo(expectedDisposition);
    await Assert.That(finalized.Completion).IsNull();
    await Assert.That(finalized.FailureDisposition).IsEqualTo(expectedDisposition);
  }

  [Test]
  [Arguments(409, """{"error":{"code":"support_identity_revoked","message":"private-response-canary"}}""",
      "http-rejected-409-identity-revoked")]
  [Arguments(409, """{"error":{"code":"support_identity_conflict","message":"private-response-canary"}}""",
      "http-rejected-409-identity-conflict")]
  [Arguments(400, """{"error":{"code":"invalid_support_identity_request"}}""",
      "http-rejected-400-identity-request-invalid")]
  [Arguments(409, """{"error":{"code":"private-response-canary"}}""", "http-rejected-409")]
  [Arguments(503, """{"error":{"code":"support_identity_conflict"}}""", "http-rejected-503")]
  [Arguments(409, """{"error":{"code":"support_identity_conflict","code":"support_identity_revoked"}}""",
      "http-rejected-409")]
  [Arguments(409, """{"error":{"code":"support_identity_conflict"},"error":{"code":"support_identity_revoked"}}""",
      "http-rejected-409")]
  public async Task Rotation_Classifies_Only_Closed_Status_Matched_Error_Codes(
      int statusCode,
      string body,
      string expectedDisposition,
      CancellationToken cancellationToken)
  {
    var client = new SupportDashboardIdentityClient(
        new TestHttpClientFactory(
            SupportDashboardIdentityHttpClientOptions.ClientName,
            _ => new HttpResponseMessage((HttpStatusCode)statusCode)
            {
              Content = new StringContent(body),
            }),
        TimeProvider.System);
    var plan = new SupportIdentityRotationPlan(
        Guid.NewGuid(), Guid.NewGuid(), "tenant-a", "https://dashboard.example.com/",
        "current-credential-abcdefghijklmnopqrstuvwxyz",
        "replacement-credential-abcdefghijklmnopqrstuvwxyz", "signing-key", "encryption-key");

    var prepared = await client.PrepareRotationWithOutcomeAsync(plan, cancellationToken);
    var finalized = await client.FinalizeRotationWithOutcomeAsync(
        new PendingSupportIdentityRotation(
            plan.RotationId, plan.NodeId, plan.TenantId, plan.DashboardUrl,
            plan.CurrentTransportCredential),
        cancellationToken);

    await Assert.That(prepared.Completion).IsNull();
    await Assert.That(prepared.FailureDisposition).IsEqualTo(expectedDisposition);
    await Assert.That(finalized.Completion).IsNull();
    await Assert.That(finalized.FailureDisposition).IsEqualTo(expectedDisposition);
  }

  [Test]
  public async Task Rotation_Bounds_Forward_Only_Error_Body_Without_Buffering_The_Response(
      CancellationToken cancellationToken)
  {
    var streams = new List<ForwardOnlyErrorResponseStream>();
    var client = new SupportDashboardIdentityClient(
        new TestHttpClientFactory(
            SupportDashboardIdentityHttpClientOptions.ClientName,
            _ =>
            {
              var stream = new ForwardOnlyErrorResponseStream(
                  System.Text.Encoding.UTF8.GetBytes(
                      """{"error":{"code":"support_identity_conflict"}}""" + new string(' ', 4096)));
              streams.Add(stream);
              return new HttpResponseMessage(HttpStatusCode.Conflict)
              {
                Content = new StreamContent(stream),
              };
            }),
        TimeProvider.System);
    var plan = new SupportIdentityRotationPlan(
        Guid.NewGuid(), Guid.NewGuid(), "tenant-a", "https://dashboard.example.com/",
        "current-credential-abcdefghijklmnopqrstuvwxyz",
        "replacement-credential-abcdefghijklmnopqrstuvwxyz", "signing-key", "encryption-key");

    var prepared = await client.PrepareRotationWithOutcomeAsync(plan, cancellationToken);

    await Assert.That(prepared.Completion).IsNull();
    await Assert.That(prepared.FailureDisposition).IsEqualTo("http-rejected-409");
    await Assert.That(streams).Count().IsEqualTo(1);
    await Assert.That(streams[0].BytesRead).IsEqualTo(1025);
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
              }),
          TimeProvider.System);

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
