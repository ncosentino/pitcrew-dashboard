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
  public async Task Broker_Failure_Remains_Stable_After_Redelivery_And_Restart(
      CancellationToken cancellationToken)
  {
    var cases = new (Exception Failure, string Disposition)[]
    {
      (
        LocalDiagnosticsBrokerRejectedException.FromStatus(
            "EvidenceAccessDenied"),
        SupportRequestRejectionDispositions
            .BrokerEvidenceAccessDenied),
      (
        new IOException("fixture"),
        SupportRequestRejectionDispositions.BrokerIoUnavailable),
      (
        new TimeoutException("fixture"),
        SupportRequestRejectionDispositions.BrokerTimeout),
    };
    foreach (var testCase in cases)
    {
      var replayRoot = Path.Combine(
          AppContext.BaseDirectory,
          $"agent-rejection-{Guid.NewGuid():N}");
      try
      {
        var now = DateTimeOffset.Parse(
            "2026-08-01T00:00:00+00:00",
            CultureInfo.InvariantCulture);
        var dashboardKeys =
            SupportKeyFactory.CreateDashboardKeys();
        var nodeKeys = SupportKeyFactory.CreateNodeKeys();
        var nodeId = Guid.NewGuid();
        var request = CreateRequest(
            nodeId,
            Guid.NewGuid(),
            now,
            $"nonce-{Guid.NewGuid():N}");
        var broker = new ThrowingDiagnosticsBroker(
            testCase.Failure);
        var envelope = CreateEnvelope(
            SupportCanonicalJson.SerializeRequest(request),
            dashboardKeys,
            nodeKeys);
        var first = await new SupportAgentRequestProcessor(
            CreateOptions(
                dashboardKeys,
                nodeKeys,
                replayRoot,
                nodeId),
            broker,
            new AgentReplayCache(replayRoot),
            new FakeTimeProvider(now)).ProcessAsync(
                request.SessionId,
                envelope,
                cancellationToken);
        var redelivered =
            await new SupportAgentRequestProcessor(
                CreateOptions(
                    dashboardKeys,
                    nodeKeys,
                    replayRoot,
                    nodeId),
                broker,
                new AgentReplayCache(replayRoot),
                new FakeTimeProvider(now)).ProcessAsync(
                    request.SessionId,
                    envelope,
                    cancellationToken);

        await Assert.That(first.RejectionDisposition)
            .IsEqualTo(testCase.Disposition);
        await Assert.That(redelivered.Status)
            .IsEqualTo(
                SupportAgentRequestProcessingStatus
                    .CachedRejection);
        await Assert.That(redelivered.RejectionDisposition)
            .IsEqualTo(testCase.Disposition);
        await Assert.That(redelivered.ResultEnvelope).IsNull();
        await Assert.That(broker.CallCount).IsEqualTo(1);
      }
      finally
      {
        if (Directory.Exists(replayRoot))
        {
          Directory.Delete(replayRoot, recursive: true);
        }
      }
    }
  }

  [Test]
  public async Task Broker_Deadline_Preserves_Timeout_Across_Restart(
      CancellationToken cancellationToken)
  {
    var replayRoot = Path.Combine(
        AppContext.BaseDirectory,
        $"agent-deadline-{Guid.NewGuid():N}");
    try
    {
      var now = DateTimeOffset.Parse(
          "2026-08-01T00:00:00+00:00",
          CultureInfo.InvariantCulture);
      var fakeTime = new FakeTimeProvider(now);
      var dashboardKeys =
          SupportKeyFactory.CreateDashboardKeys();
      var nodeKeys = SupportKeyFactory.CreateNodeKeys();
      var nodeId = Guid.NewGuid();
      var request = CreateRequest(
          nodeId,
          Guid.NewGuid(),
          now,
          $"nonce-{Guid.NewGuid():N}");
      var broker = new PendingDiagnosticsBroker();
      var envelope = CreateEnvelope(
          SupportCanonicalJson.SerializeRequest(request),
          dashboardKeys,
          nodeKeys);
      var processing = new SupportAgentRequestProcessor(
          CreateOptions(
              dashboardKeys,
              nodeKeys,
              replayRoot,
              nodeId),
          broker,
          new AgentReplayCache(replayRoot),
          fakeTime).ProcessAsync(
              request.SessionId,
              envelope,
              cancellationToken);

      fakeTime.Advance(TimeSpan.FromMinutes(2));
      var timedOut = await processing;
      var redelivered =
          await new SupportAgentRequestProcessor(
              CreateOptions(
                  dashboardKeys,
                  nodeKeys,
                  replayRoot,
                  nodeId),
              broker,
              new AgentReplayCache(replayRoot),
              fakeTime).ProcessAsync(
                  request.SessionId,
                  envelope,
                  cancellationToken);

      await Assert.That(timedOut.Status)
          .IsEqualTo(
              SupportAgentRequestProcessingStatus.BrokerTimeout);
      await Assert.That(timedOut.RejectionDisposition)
          .IsEqualTo(
              SupportRequestRejectionDispositions.BrokerTimeout);
      await Assert.That(redelivered.Status)
          .IsEqualTo(
              SupportAgentRequestProcessingStatus.CachedRejection);
      await Assert.That(redelivered.RejectionDisposition)
          .IsEqualTo(
              SupportRequestRejectionDispositions.BrokerTimeout);
      await Assert.That(broker.CallCount).IsEqualTo(1);
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
  public async Task Short_Request_Window_Remains_Executable(
      CancellationToken cancellationToken)
  {
    var replayRoot = Path.Combine(
        AppContext.BaseDirectory,
        $"agent-short-window-{Guid.NewGuid():N}");
    try
    {
      var now = DateTimeOffset.Parse(
          "2026-08-01T00:00:00+00:00",
          CultureInfo.InvariantCulture);
      var dashboardKeys =
          SupportKeyFactory.CreateDashboardKeys();
      var nodeKeys = SupportKeyFactory.CreateNodeKeys();
      var nodeId = Guid.NewGuid();
      var request = CreateRequest(
          nodeId,
          Guid.NewGuid(),
          now,
          $"nonce-{Guid.NewGuid():N}") with
      {
        ExpiresAt = now.AddSeconds(30),
      };
      var broker = new CountingDiagnosticsBroker();
      var result = await new SupportAgentRequestProcessor(
          CreateOptions(
              dashboardKeys,
              nodeKeys,
              replayRoot,
              nodeId),
          broker,
          new AgentReplayCache(replayRoot),
          new FakeTimeProvider(now)).ProcessAsync(
              request.SessionId,
              CreateEnvelope(
                  SupportCanonicalJson.SerializeRequest(request),
                  dashboardKeys,
                  nodeKeys),
              cancellationToken);

      await Assert.That(result.Status)
          .IsEqualTo(
              SupportAgentRequestProcessingStatus.Succeeded);
      await Assert.That(result.ResultEnvelope).IsNotNull();
      await Assert.That(broker.CallCount).IsEqualTo(1);
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
  public async Task Service_Stop_Cancellation_Does_Not_Create_Request_Outcome()
  {
    var replayRoot = Path.Combine(
        AppContext.BaseDirectory,
        $"agent-stop-{Guid.NewGuid():N}");
    try
    {
      var now = DateTimeOffset.Parse(
          "2026-08-01T00:00:00+00:00",
          CultureInfo.InvariantCulture);
      var dashboardKeys =
          SupportKeyFactory.CreateDashboardKeys();
      var nodeKeys = SupportKeyFactory.CreateNodeKeys();
      var nodeId = Guid.NewGuid();
      var request = CreateRequest(
          nodeId,
          Guid.NewGuid(),
          now,
          $"nonce-{Guid.NewGuid():N}");
      var replayCache = new AgentReplayCache(replayRoot);
      using var stopping = new CancellationTokenSource();
      var broker = new StoppingDiagnosticsBroker(stopping);

      await Assert.That(
              async () => await new SupportAgentRequestProcessor(
                  CreateOptions(
                      dashboardKeys,
                      nodeKeys,
                      replayRoot,
                      nodeId),
                  broker,
                  replayCache,
                  new FakeTimeProvider(now)).ProcessAsync(
                      request.SessionId,
                      CreateEnvelope(
                          SupportCanonicalJson.SerializeRequest(
                              request),
                          dashboardKeys,
                          nodeKeys),
                      stopping.Token))
          .Throws<OperationCanceledException>();
      await Assert.That(
              replayCache.GetRejectionOrNull(
                  request.SessionId))
          .IsNull();
      await Assert.That(broker.CallCount).IsEqualTo(1);
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
  public async Task Insufficient_Reporting_Window_Is_Rejected_Before_Nonce_Claim(
      CancellationToken cancellationToken)
  {
    var replayRoot = Path.Combine(
        AppContext.BaseDirectory,
        $"agent-window-{Guid.NewGuid():N}");
    try
    {
      var now = DateTimeOffset.Parse(
          "2026-08-01T00:00:00+00:00",
          CultureInfo.InvariantCulture);
      var dashboardKeys =
          SupportKeyFactory.CreateDashboardKeys();
      var nodeKeys = SupportKeyFactory.CreateNodeKeys();
      var nodeId = Guid.NewGuid();
      var request = CreateRequest(
          nodeId,
          Guid.NewGuid(),
          now,
          $"nonce-{Guid.NewGuid():N}") with
      {
        ExpiresAt = now.AddSeconds(5),
      };
      var replayCache = new AgentReplayCache(replayRoot);
      var broker = new CountingDiagnosticsBroker();
      var result = await new SupportAgentRequestProcessor(
          CreateOptions(
              dashboardKeys,
              nodeKeys,
              replayRoot,
              nodeId),
          broker,
          replayCache,
          new FakeTimeProvider(now)).ProcessAsync(
              request.SessionId,
              CreateEnvelope(
                  SupportCanonicalJson.SerializeRequest(request),
                  dashboardKeys,
                  nodeKeys),
              cancellationToken);

      await Assert.That(result.Status)
          .IsEqualTo(
              SupportAgentRequestProcessingStatus.ValidationRejected);
      await Assert.That(result.ValidationStatus)
          .IsEqualTo(SupportRequestValidationStatus.Expired);
      await Assert.That(replayCache.HasNonce(request.Nonce))
          .IsFalse();
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

  [Test]
  public async Task Malformed_Request_Returns_Bounded_Result(
      CancellationToken cancellationToken)
  {
    var replayRoot = Path.Combine(
        AppContext.BaseDirectory,
        $"agent-replay-{Guid.NewGuid():N}");
    try
    {
      var now = DateTimeOffset.Parse(
          "2026-08-01T00:00:00+00:00",
          CultureInfo.InvariantCulture);
      var dashboardKeys = SupportKeyFactory.CreateDashboardKeys();
      var nodeKeys = SupportKeyFactory.CreateNodeKeys();
      var nodeId = Guid.NewGuid();
      var sessionId = Guid.NewGuid();
      var broker = new CountingDiagnosticsBroker();
      var processor = new SupportAgentRequestProcessor(
          CreateOptions(
              dashboardKeys,
              nodeKeys,
              replayRoot,
              nodeId),
          broker,
          new AgentReplayCache(replayRoot),
          new FakeTimeProvider(now));
      var envelope = CreateEnvelope(
          Encoding.UTF8.GetBytes("{]"),
          dashboardKeys,
          nodeKeys);

      var result = await processor.ProcessAsync(
          sessionId,
          envelope,
          cancellationToken);

      await Assert.That(result.Status)
          .IsEqualTo(
              SupportAgentRequestProcessingStatus.RequestMalformed);
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

  [Test]
  public async Task Invalid_Broker_Output_Returns_Bounded_Rejections(
      CancellationToken cancellationToken)
  {
    var root = Path.Combine(
        AppContext.BaseDirectory,
        $"agent-rejections-{Guid.NewGuid():N}");
    var markdownReplayRoot = Path.Combine(root, "markdown");
    var reportReplayRoot = Path.Combine(root, "report");
    try
    {
      var now = DateTimeOffset.Parse(
          "2026-08-01T00:00:00+00:00",
          CultureInfo.InvariantCulture);
      var dashboardKeys = SupportKeyFactory.CreateDashboardKeys();
      var nodeKeys = SupportKeyFactory.CreateNodeKeys();
      var nodeId = Guid.NewGuid();
      var markdownRequest = CreateRequest(
          nodeId,
          Guid.NewGuid(),
          now,
          "nonce-markdown-abcdefghijklmnopqrstuvwxyz");
      var reportRequest = CreateRequest(
          nodeId,
          Guid.NewGuid(),
          now,
          "nonce-report-abcdefghijklmnopqrstuvwxyz12");
      var markdownProcessor = new SupportAgentRequestProcessor(
          CreateOptions(
              dashboardKeys,
              nodeKeys,
              markdownReplayRoot,
              nodeId),
          new InvalidDiagnosticsBroker(
              true),
          new AgentReplayCache(markdownReplayRoot),
          new FakeTimeProvider(now));
      var reportProcessor = new SupportAgentRequestProcessor(
          CreateOptions(
              dashboardKeys,
              nodeKeys,
              reportReplayRoot,
              nodeId),
          new InvalidDiagnosticsBroker(
              false),
          new AgentReplayCache(reportReplayRoot),
          new FakeTimeProvider(now));

      var markdownResult =
          await markdownProcessor.ProcessAsync(
              markdownRequest.SessionId,
              CreateEnvelope(
                  SupportCanonicalJson.SerializeRequest(
                      markdownRequest),
                  dashboardKeys,
                  nodeKeys),
              cancellationToken);
      var reportResult = await reportProcessor.ProcessAsync(
          reportRequest.SessionId,
          CreateEnvelope(
              SupportCanonicalJson.SerializeRequest(
                  reportRequest),
              dashboardKeys,
              nodeKeys),
          cancellationToken);

      await Assert.That(markdownResult.Status)
          .IsEqualTo(
              SupportAgentRequestProcessingStatus
                  .BrokerMarkdownRejected);
      await Assert.That(markdownResult.ResultEnvelope)
          .IsNull();
      await Assert.That(reportResult.Status)
          .IsEqualTo(
              SupportAgentRequestProcessingStatus
                  .BrokerReportRejected);
      await Assert.That(reportResult.ResultEnvelope)
          .IsNull();
    }
    finally
    {
      if (Directory.Exists(root))
      {
        Directory.Delete(root, recursive: true);
      }
    }
  }

  private static SupportAgentOptions CreateOptions(
      SupportDashboardKeySet dashboardKeys,
      SupportNodeKeySet nodeKeys,
      string replayRoot,
      Guid nodeId) =>
      new(
          "tenant-a",
          nodeId,
          new Uri("https://dashboard.example.com"),
          new Uri("https://relay.example.com"),
          "transport",
          dashboardKeys.AuthorizationSigning
              .PublicKeySubjectPublicKeyInfoBase64Url,
          dashboardKeys.ResultEncryption
              .PublicKeySubjectPublicKeyInfoBase64Url,
          replayRoot,
          "unused",
          "/unused",
          new LegacySupportNodePrivateKeySource(
              nodeKeys.Signing.PrivateKeyPkcs8Base64Url,
              nodeKeys.Encryption.PrivateKeyPkcs8Base64Url));

  private static SupportDiagnosticRequest CreateRequest(
      Guid nodeId,
      Guid sessionId,
      DateTimeOffset now,
      string nonce) =>
      new(
          "support-plane-v1",
          "tenant-a",
          nodeId,
          sessionId,
          SupportCapability.DiagnosticsSnapshotV1,
          1,
          SupportDiagnosticModes.ConnectorOffline,
          null,
          new string('a', 32),
          now,
          now.AddMinutes(10),
          nonce);

  private static SupportEnvelope CreateEnvelope(
      byte[] payload,
      SupportDashboardKeySet dashboardKeys,
      SupportNodeKeySet nodeKeys)
  {
    using var dashboardSigning =
        SupportKeyFactory.ImportEcdsaPrivateKey(
            dashboardKeys.AuthorizationSigning
                .PrivateKeyPkcs8Base64Url);
    using var nodeEncryption =
        SupportKeyFactory.ImportRsaPublicKey(
            nodeKeys.Encryption
                .PublicKeySubjectPublicKeyInfoBase64Url);
    return SupportEnvelopeCryptography.Seal(
        payload,
        nodeEncryption,
        dashboardSigning,
        "dashboard",
        "node");
  }

  private static LocalDiagnosticsResult CreateDiagnosticsResult(
      LocalDiagnosticsRequest request,
      string markdown,
      string? diagnosticMode = null)
  {
    using var document = JsonSerializer.SerializeToDocument(new
    {
      schemaVersion = 1,
      collectorVersion = "1.1.0",
      collectorSha256 = new string('a', 64),
      packageId = request.PackageId,
      diagnosticMode =
          diagnosticMode ?? request.DiagnosticMode,
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
    return new LocalDiagnosticsResult(
        document.RootElement.Clone(),
        markdown);
  }

  private sealed class CountingDiagnosticsBroker : ILocalDiagnosticsBroker
  {
    public int CallCount { get; private set; }

    public Task<LocalDiagnosticsResult> ExecuteAsync(
        LocalDiagnosticsRequest request,
        CancellationToken cancellationToken)
    {
      CallCount++;
      return Task.FromResult(
          CreateDiagnosticsResult(
              request,
              "# Diagnostics"));
    }
  }

  private sealed class InvalidDiagnosticsBroker(
      bool _rejectMarkdown) : ILocalDiagnosticsBroker
  {
    public Task<LocalDiagnosticsResult> ExecuteAsync(
        LocalDiagnosticsRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(
            CreateDiagnosticsResult(
                request,
                _rejectMarkdown
                    ? "https://example.com/?secret=value"
                    : "# Diagnostics",
                _rejectMarkdown
                    ? null
                    : SupportDiagnosticModes.Full));
  }

  private sealed class ThrowingDiagnosticsBroker(
      Exception _failure) : ILocalDiagnosticsBroker
  {
    public int CallCount { get; private set; }

    public Task<LocalDiagnosticsResult> ExecuteAsync(
        LocalDiagnosticsRequest request,
        CancellationToken cancellationToken)
    {
      CallCount++;
      return Task.FromException<LocalDiagnosticsResult>(
          _failure);
    }
  }

  private sealed class PendingDiagnosticsBroker :
      ILocalDiagnosticsBroker
  {
    public int CallCount { get; private set; }

    public async Task<LocalDiagnosticsResult> ExecuteAsync(
        LocalDiagnosticsRequest request,
        CancellationToken cancellationToken)
    {
      CallCount++;
      await Task.Delay(
          Timeout.InfiniteTimeSpan,
          cancellationToken);
      throw new InvalidOperationException(
          "The pending broker completed unexpectedly.");
    }
  }

  private sealed class StoppingDiagnosticsBroker(
      CancellationTokenSource _stopping) :
      ILocalDiagnosticsBroker
  {
    public int CallCount { get; private set; }

    public async Task<LocalDiagnosticsResult> ExecuteAsync(
        LocalDiagnosticsRequest request,
        CancellationToken cancellationToken)
    {
      CallCount++;
      await _stopping.CancelAsync();
      cancellationToken.ThrowIfCancellationRequested();
      throw new InvalidOperationException(
          "The stopping broker completed unexpectedly.");
    }
  }
}
