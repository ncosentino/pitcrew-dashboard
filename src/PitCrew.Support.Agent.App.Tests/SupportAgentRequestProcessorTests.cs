using System.Globalization;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Time.Testing;

using PitCrew.Support.Agent.App;
using PitCrew.Support.Protocol;

namespace PitCrew.Support.Agent.App.Tests;

public sealed class SupportAgentRequestProcessorTests
{
  [Test]
  public async Task Duplicate_Request_Returns_Cached_Result_Without_Rerunning_Broker(
      CancellationToken cancellationToken)
  {
    var replayRoot = Path.Combine(AppContext.BaseDirectory, $"agent-replay-{Guid.NewGuid():N}");
    try
    {
      var now = DateTimeOffset.Parse("2026-08-01T00:00:00+00:00", CultureInfo.InvariantCulture);
      var fakeTime = new FakeTimeProvider(now);
      var dashboardKeys = SupportKeyFactory.CreateDashboardKeys();
      var nodeKeys = SupportKeyFactory.CreateNodeKeys();
      var nodeId = Guid.Parse("11111111-1111-1111-1111-111111111111", CultureInfo.InvariantCulture);
      var sessionId = Guid.Parse("22222222-2222-2222-2222-222222222222", CultureInfo.InvariantCulture);
      var broker = new CountingDiagnosticsBroker();
      var options = new SupportAgentOptions(
          "tenant-a",
          nodeId,
          new Uri("https://dashboard.example.com"),
          new Uri("https://relay.example.com"),
          "transport",
          dashboardKeys.AuthorizationSigning.PublicKeySubjectPublicKeyInfoBase64Url,
          dashboardKeys.ResultEncryption.PublicKeySubjectPublicKeyInfoBase64Url,
          replayRoot,
          "unused",
          "/unused",
          new LegacySupportNodePrivateKeySource(
              nodeKeys.Signing.PrivateKeyPkcs8Base64Url,
              nodeKeys.Encryption.PrivateKeyPkcs8Base64Url));
      var processor = new SupportAgentRequestProcessor(
          options,
          broker,
          new AgentReplayCache(replayRoot),
          fakeTime);
      var request = new SupportDiagnosticRequest(
          "support-plane-v1",
          "tenant-a",
          nodeId,
          sessionId,
          SupportCapability.DiagnosticsSnapshotV1,
          1,
          SupportDiagnosticModes.Full,
          null,
          "support-package",
          now,
          now.AddMinutes(10),
          "nonce-abcdefghijklmnopqrstuvwxyz0123456789");
      using var dashboardSigning = SupportKeyFactory.ImportEcdsaPrivateKey(
          dashboardKeys.AuthorizationSigning.PrivateKeyPkcs8Base64Url);
      using var nodeEncryption = SupportKeyFactory.ImportRsaPublicKey(
          nodeKeys.Encryption.PublicKeySubjectPublicKeyInfoBase64Url);
      var envelope = SupportEnvelopeCryptography.Seal(
          SupportCanonicalJson.SerializeRequest(request),
          nodeEncryption,
          dashboardSigning,
          "dashboard",
          "node");

      var first = await processor.ProcessAsync(sessionId, envelope, cancellationToken);
      var second = await processor.ProcessAsync(sessionId, envelope, cancellationToken);

      await Assert.That(first.Status)
          .IsEqualTo(
              SupportAgentRequestProcessingStatus.Succeeded);
      await Assert.That(second.Status)
          .IsEqualTo(
              SupportAgentRequestProcessingStatus.Cached);
      await Assert.That(first.ResultEnvelope).IsNotNull();
      await Assert.That(second.ResultEnvelope).IsNotNull();
      await Assert.That(broker.CallCount).IsEqualTo(1);
      await Assert.That(second.ResultEnvelope!.SignatureBase64Url)
          .IsEqualTo(
              first.ResultEnvelope!.SignatureBase64Url);
      using var nodeSigning = SupportKeyFactory.ImportEcdsaPublicKey(
          nodeKeys.Signing.PublicKeySubjectPublicKeyInfoBase64Url);
      using var dashboardResultKey = SupportKeyFactory.ImportRsaPrivateKey(
          dashboardKeys.ResultEncryption.PrivateKeyPkcs8Base64Url);
      var opened = SupportEnvelopeCryptography.OpenOrNull(
          first.ResultEnvelope,
          nodeSigning,
          dashboardResultKey);
      await Assert.That(opened).IsNotNull();
      var package = JsonSerializer.Deserialize<SupportSignedResultPackage>(
          opened,
          new JsonSerializerOptions(JsonSerializerDefaults.Web));
      await Assert.That(package).IsNotNull();
      await Assert.That(Encoding.UTF8.GetString(SupportBase64Url.Decode(package!.PayloadBase64Url)))
          .Contains("# Diagnostics");
    }
    finally
    {
      if (Directory.Exists(replayRoot))
      {
        Directory.Delete(replayRoot, recursive: true);
      }
    }
  }

  [Test]
  public async Task Request_Envelope_Cannot_Be_Rebound_To_Another_Relay_Session(
      CancellationToken cancellationToken)
  {
    var replayRoot = Path.Combine(AppContext.BaseDirectory, $"agent-replay-{Guid.NewGuid():N}");
    try
    {
      var now = DateTimeOffset.Parse("2026-08-01T00:00:00+00:00", CultureInfo.InvariantCulture);
      var fakeTime = new FakeTimeProvider(now);
      var dashboardKeys = SupportKeyFactory.CreateDashboardKeys();
      var nodeKeys = SupportKeyFactory.CreateNodeKeys();
      var nodeId = Guid.Parse("11111111-1111-1111-1111-111111111111", CultureInfo.InvariantCulture);
      var sessionId = Guid.Parse("22222222-2222-2222-2222-222222222222", CultureInfo.InvariantCulture);
      var broker = new CountingDiagnosticsBroker();
      var options = new SupportAgentOptions(
          "tenant-a",
          nodeId,
          new Uri("https://dashboard.example.com"),
          new Uri("https://relay.example.com"),
          "transport",
          dashboardKeys.AuthorizationSigning.PublicKeySubjectPublicKeyInfoBase64Url,
          dashboardKeys.ResultEncryption.PublicKeySubjectPublicKeyInfoBase64Url,
          replayRoot,
          "unused",
          "/unused",
          new LegacySupportNodePrivateKeySource(
              nodeKeys.Signing.PrivateKeyPkcs8Base64Url,
              nodeKeys.Encryption.PrivateKeyPkcs8Base64Url));
      var processor = new SupportAgentRequestProcessor(
          options,
          broker,
          new AgentReplayCache(replayRoot),
          fakeTime);
      var request = new SupportDiagnosticRequest(
          "support-plane-v1",
          "tenant-a",
          nodeId,
          sessionId,
          SupportCapability.DiagnosticsSnapshotV1,
          1,
          SupportDiagnosticModes.Full,
          null,
          "support-package",
          now,
          now.AddMinutes(10),
          "nonce-abcdefghijklmnopqrstuvwxyz0123456789");
      using var dashboardSigning = SupportKeyFactory.ImportEcdsaPrivateKey(
          dashboardKeys.AuthorizationSigning.PrivateKeyPkcs8Base64Url);
      using var nodeEncryption = SupportKeyFactory.ImportRsaPublicKey(
          nodeKeys.Encryption.PublicKeySubjectPublicKeyInfoBase64Url);
      var envelope = SupportEnvelopeCryptography.Seal(
          SupportCanonicalJson.SerializeRequest(request),
          nodeEncryption,
          dashboardSigning,
          "dashboard",
          "node");

      var result = await processor.ProcessAsync(Guid.NewGuid(), envelope, cancellationToken);

      await Assert.That(result.Status)
          .IsEqualTo(
              SupportAgentRequestProcessingStatus.SessionMismatch);
      await Assert.That(result.ResultEnvelope).IsNull();
      await Assert.That(broker.CallCount).IsEqualTo(0);
    }
    finally
    {
      if (Directory.Exists(replayRoot))
      {
        Directory.Delete(replayRoot, recursive: true);
      }
    }
  }

  private sealed class CountingDiagnosticsBroker : ILocalDiagnosticsBroker
  {
    public int CallCount { get; private set; }

    public Task<LocalDiagnosticsResult> ExecuteAsync(
        LocalDiagnosticsRequest request,
        CancellationToken cancellationToken)
    {
      CallCount++;
      using var document = JsonSerializer.SerializeToDocument(new
      {
        schemaVersion = 1,
        collectorVersion = "1.1.0",
        collectorSha256 = new string('a', 64),
        packageId = request.PackageId,
        diagnosticMode = request.DiagnosticMode,
        collectionScope = "file-only",
        platform = "Windows",
        platformSource = "detected",
        profile = request.ProfileId ?? "default",
        pitcrewRoot = "<pitcrew-root>",
        startedAt = "2026-08-01T00:00:00+00:00",
        completedAt = "2026-08-01T00:00:01+00:00",
        verifiedMeasurements = new { state = "bounded" },
        unavailableEvidence = Array.Empty<object>(),
        hypotheses = Array.Empty<object>(),
      });
      return Task.FromResult(new LocalDiagnosticsResult(
          document.RootElement.Clone(),
          "# Diagnostics"));
    }
  }
}
