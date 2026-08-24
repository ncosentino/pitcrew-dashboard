using System.Net;

using PitCrew.Support.Agent.App;
using PitCrew.Support.Protocol;

namespace PitCrew.Support.Agent.App.Tests;

public sealed class SupportRelayTransportClientTests
{
  [Test]
  public async Task Rejection_Reporting_Is_Bounded_And_Mixed_Version_Safe(
      CancellationToken cancellationToken)
  {
    var nodeId = Guid.NewGuid();
    var sessionId = Guid.NewGuid();
    var options = CreateOptions(nodeId);
    Uri? requestedUri = null;
    var succeeded = await CreateClient(
            HttpStatusCode.NoContent,
            uri => requestedUri = uri)
        .ReportRejectionAsync(
            options,
            sessionId,
            SupportRequestRejectionDispositions
                .UnsupportedCapability,
            cancellationToken);
    var olderRelay = await CreateClient(
            HttpStatusCode.NotFound,
            _ => { })
        .ReportRejectionAsync(
            options,
            sessionId,
            SupportRequestRejectionDispositions
                .UnsupportedCapability,
            cancellationToken);
    var rejectedCredential = await CreateClient(
            HttpStatusCode.Unauthorized,
            _ => { })
        .ReportRejectionAsync(
            options,
            sessionId,
            SupportRequestRejectionDispositions
                .UnsupportedCapability,
            cancellationToken);

    await Assert.That(succeeded)
        .IsEqualTo(SupportRelayOutcomeReportStatus.Succeeded);
    await Assert.That(olderRelay)
        .IsEqualTo(
            SupportRelayOutcomeReportStatus.SessionUnavailable);
    await Assert.That(rejectedCredential)
        .IsEqualTo(
            SupportRelayOutcomeReportStatus.CredentialRejected);
    await Assert.That(requestedUri).IsNotNull();
    await Assert.That(requestedUri!.AbsolutePath)
        .IsEqualTo(
            $"/api/support-relay/v1/nodes/{nodeId:D}/sessions/{sessionId:D}/outcome");
    await Assert.That(requestedUri.Query).IsEmpty();
  }

  private static SupportRelayTransportClient CreateClient(
      HttpStatusCode statusCode,
      Action<Uri?> recordUri) =>
      new(
          new TestHttpClientFactory(
              SupportRelayTransportHttpClientOptions.ClientName,
              request =>
              {
                recordUri(request.RequestUri);
                return new HttpResponseMessage(statusCode);
              }));

  private static SupportAgentOptions CreateOptions(Guid nodeId)
  {
    var dashboardKeys = SupportKeyFactory.CreateDashboardKeys();
    var nodeKeys = SupportKeyFactory.CreateNodeKeys();
    return new SupportAgentOptions(
        "tenant-a",
        nodeId,
        new Uri(
            "https://dashboard.example.com",
            UriKind.Absolute),
        new Uri(
            "https://relay.example.com",
            UriKind.Absolute),
        "transport-credential",
        dashboardKeys.AuthorizationSigning
            .PublicKeySubjectPublicKeyInfoBase64Url,
        dashboardKeys.ResultEncryption
            .PublicKeySubjectPublicKeyInfoBase64Url,
        "replay",
        "unused",
        "/unused",
        new LegacySupportNodePrivateKeySource(
            nodeKeys.Signing.PrivateKeyPkcs8Base64Url,
            nodeKeys.Encryption.PrivateKeyPkcs8Base64Url));
  }
}
