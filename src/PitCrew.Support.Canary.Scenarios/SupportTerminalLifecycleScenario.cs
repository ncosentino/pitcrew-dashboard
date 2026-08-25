using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

using PitCrew.Dashboard.Features.Support;
using PitCrew.Support.Canary.Contracts;
using PitCrew.Support.Protocol;

namespace PitCrew.Support.Canary.Scenarios;

/// <summary>
/// Exercises queued, dispatched, completed, rejected, cancelled, and expired
/// Dashboard session lifecycle through candidate components.
/// </summary>
public sealed class SupportTerminalLifecycleScenario : ICanaryScenario
{
  private const string ScenarioId =
      "support-terminal-lifecycle-v1";
  private const string DormantDisplayName =
      "Canary dormant lifecycle node";
  private const string UnconfiguredProfileId =
      "canary-unconfigured";
  private static readonly JsonSerializerOptions _jsonOptions =
      new(JsonSerializerDefaults.Web);
  private readonly SupportFreshEnrollmentDiagnosticScenario _inner =
      new(
          ScenarioId,
          [],
          afterFirstAcceptedPoll: null,
          diagnosticModes: null,
          afterBootstrapFinalization:
              ExerciseLifecycleAsync,
          afterBootstrapStepName:
              "verify-terminal-session-lifecycle");

  /// <inheritdoc />
  public string Id => _inner.Id;

  /// <inheritdoc />
  public IReadOnlySet<string> RequiredCapabilities =>
      _inner.RequiredCapabilities;

  /// <inheritdoc />
  public Task<CanaryScenarioResult> RunAsync(
      CanaryRuntimeManifest runtime,
      CanaryScenarioContext context,
      CancellationToken cancellationToken) =>
      _inner.RunAsync(
          runtime,
          context,
          cancellationToken);

  private static async Task<string> ExerciseLifecycleAsync(
      CanaryRuntimeManifest runtime,
      CanaryScenarioContext context,
      Guid activeNodeId,
      string agentStateRoot,
      CancellationToken cancellationToken)
  {
    _ = agentStateRoot;
    using var dashboard =
        new SupportCanaryDashboardClient(
            runtime.DashboardUrl);
    var antiforgeryToken =
        await dashboard.GetAntiforgeryTokenAsync(
            cancellationToken);
    var dormant = await EnrollDormantNodeAsync(
        dashboard,
        antiforgeryToken,
        cancellationToken);

    var queued = await dashboard.CreateSupportSessionAsync(
        antiforgeryToken,
        dormant.NodeId,
        cancellationToken);
    RequireStatus(
        queued,
        "Queued",
        requireDispatch: false,
        requireResult: false);
    await dashboard.CancelSupportSessionAsync(
        antiforgeryToken,
        ParseSessionId(queued),
        cancellationToken);
    var cancelled = await dashboard.GetSupportSessionAsync(
        ParseSessionId(queued),
        cancellationToken);
    RequireStatus(
        cancelled,
        "Cancelled",
        requireDispatch: false,
        requireResult: false);

    var expiring = await dashboard.CreateSupportSessionAsync(
        antiforgeryToken,
        dormant.NodeId,
        cancellationToken);
    await Task.Delay(
        TimeSpan.FromSeconds(31),
        context.TimeProvider,
        cancellationToken);
    var expired = await dashboard.GetSupportSessionAsync(
        ParseSessionId(expiring),
        cancellationToken);
    RequireStatus(
        expired,
        "Expired",
        requireDispatch: false,
        requireResult: false);

    var rejecting = await dashboard.CreateSupportSessionAsync(
        antiforgeryToken,
        dormant.NodeId,
        cancellationToken);
    await ReportDormantRejectionAsync(
        dormant,
        ParseSessionId(rejecting),
        cancellationToken);
    var rejected = await WaitForStatusAsync(
        dashboard,
        ParseSessionId(rejecting),
        "Rejected",
        context.TimeProvider,
        cancellationToken);
    RequireStatus(
        rejected,
        "Rejected",
        requireDispatch: true,
        requireResult: false);
    if (rejected.RejectionDisposition !=
        SupportRequestRejectionDispositions
            .UnsupportedCapability)
    {
      throw new CanaryScenarioFailureException(
          "terminal-lifecycle-matrix-mismatch");
    }

    var brokerRejecting =
        await dashboard.CreateSupportSessionAsync(
            antiforgeryToken,
            activeNodeId,
            SupportDiagnosticModes.ConnectorOffline,
            UnconfiguredProfileId,
            cancellationToken);
    var brokerRejected = await WaitForStatusAsync(
        dashboard,
        ParseSessionId(brokerRejecting),
        "Rejected",
        context.TimeProvider,
        cancellationToken);
    RequireStatus(
        brokerRejected,
        "Rejected",
        requireDispatch: true,
        requireResult: false);
    if (brokerRejected.RejectionDisposition !=
        SupportRequestRejectionDispositions
            .BrokerInvalidProfile)
    {
      throw new CanaryScenarioFailureException(
          "terminal-lifecycle-matrix-mismatch");
    }

    var completing = await dashboard.CreateSupportSessionAsync(
        antiforgeryToken,
        activeNodeId,
        cancellationToken);
    var completed = await WaitForStatusAsync(
        dashboard,
        ParseSessionId(completing),
        "Completed",
        context.TimeProvider,
        cancellationToken);
    RequireStatus(
        completed,
        "Completed",
        requireDispatch: true,
        requireResult: true);
    await dashboard.RevokeAsync(
        antiforgeryToken,
        dormant.NodeId,
        cancellationToken);
    return "terminal-session-lifecycle-verified";
  }

  private static async Task<DormantSupportNode>
      EnrollDormantNodeAsync(
          SupportCanaryDashboardClient dashboard,
          string antiforgeryToken,
          CancellationToken cancellationToken)
  {
    var authorization =
        await dashboard.CreateEnrollmentAuthorizationAsync(
            antiforgeryToken,
            DormantDisplayName,
            cancellationToken);
    var completionId = Guid.NewGuid();
    var nodeKeys = SupportKeyFactory.CreateNodeKeys();
    var completion = await dashboard.CompleteEnrollmentAsync(
        authorization.EnrollmentCode,
        completionId,
        nodeKeys.Signing
            .PublicKeySubjectPublicKeyInfoBase64Url,
        nodeKeys.Encryption
            .PublicKeySubjectPublicKeyInfoBase64Url,
        cancellationToken);
    if (!Guid.TryParse(
            completion.NodeId,
            CultureInfo.InvariantCulture,
            out var nodeId) ||
        nodeId == Guid.Empty)
    {
      throw new CanaryScenarioFailureException(
          "support-identity-inventory-invalid");
    }
    byte[]? payload = null;
    try
    {
      using var dashboardSigning =
          SupportKeyFactory.ImportEcdsaPublicKey(
              completion
                  .AuthorizationSigningPublicKeySpki);
      using var nodeEncryption =
          SupportKeyFactory.ImportRsaPrivateKey(
              nodeKeys.Encryption
                  .PrivateKeyPkcs8Base64Url);
      payload = SupportEnvelopeCryptography.OpenOrNull(
          completion.TransportCredentialEnvelope,
          dashboardSigning,
          nodeEncryption);
      var credential = payload is null
          ? null
          : JsonSerializer.Deserialize<
              EnrollmentCredentialPayload>(
                  payload,
                  _jsonOptions);
      if (credential is null ||
          credential.Schema !=
              "support-enrollment-credential-v1" ||
          credential.NodeId != nodeId ||
          credential.CompletionId != completionId ||
          credential.TransportCredential.Length
              is < 32 or > 256)
      {
        throw new CanaryScenarioFailureException(
            "support-identity-inventory-invalid");
      }
      return new DormantSupportNode(
          nodeId,
          credential.TransportCredential,
          completion.RelayUrl);
    }
    finally
    {
      if (payload is not null)
      {
        CryptographicOperations.ZeroMemory(payload);
      }
    }
  }

  private static async Task ReportDormantRejectionAsync(
      DormantSupportNode dormant,
      Guid expectedSessionId,
      CancellationToken cancellationToken)
  {
    using var client = new HttpClient
    {
      BaseAddress = new Uri(
          dormant.RelayUrl,
          UriKind.Absolute),
      Timeout = TimeSpan.FromSeconds(30),
    };
    client.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue(
            "Bearer",
            dormant.TransportCredential);
    using var poll = await client.GetAsync(
        $"/api/support-relay/v1/nodes/{dormant.NodeId:D}/poll",
        cancellationToken);
    if (poll.StatusCode != HttpStatusCode.OK)
    {
      throw new CanaryScenarioFailureException(
          "terminal-lifecycle-matrix-mismatch");
    }
    var dispatched = await poll.Content
        .ReadFromJsonAsync<DormantRelayPollResponse>(
            _jsonOptions,
            cancellationToken) ??
        throw new CanaryScenarioFailureException(
            "terminal-lifecycle-matrix-mismatch");
    if (dispatched.SessionId != expectedSessionId)
    {
      throw new CanaryScenarioFailureException(
          "terminal-lifecycle-matrix-mismatch");
    }
    using var outcome = await client.PostAsJsonAsync(
        $"/api/support-relay/v1/nodes/{dormant.NodeId:D}/sessions/{expectedSessionId:D}/outcome",
        new SupportRelayRequestOutcomeRequest(
            SupportRequestRejectionDispositions
                .UnsupportedCapability),
        _jsonOptions,
        cancellationToken);
    if (outcome.StatusCode != HttpStatusCode.NoContent)
    {
      throw new CanaryScenarioFailureException(
          "terminal-lifecycle-matrix-mismatch");
    }
  }

  private static async Task<SupportDiagnosticSessionResponse>
      WaitForStatusAsync(
          SupportCanaryDashboardClient dashboard,
          Guid sessionId,
          string expectedStatus,
          TimeProvider timeProvider,
          CancellationToken cancellationToken)
  {
    using var timeout = new CancellationTokenSource(
        TimeSpan.FromSeconds(60),
        timeProvider);
    using var linked =
        CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);
    using var timer = new PeriodicTimer(
        TimeSpan.FromMilliseconds(500),
        timeProvider);
    try
    {
      while (await timer.WaitForNextTickAsync(linked.Token))
      {
        var session = await dashboard.GetSupportSessionAsync(
            sessionId,
            linked.Token);
        if (session.Status == expectedStatus)
        {
          return session;
        }
        if (session.Status is
            "Completed" or
            "Rejected" or
            "Cancelled" or
            "Expired")
        {
          throw new CanaryScenarioFailureException(
              "terminal-lifecycle-matrix-mismatch");
        }
      }
    }
    catch (OperationCanceledException)
        when (!cancellationToken.IsCancellationRequested)
    {
      throw new CanaryScenarioFailureException(
          "terminal-lifecycle-matrix-timeout");
    }
    throw new CanaryScenarioFailureException(
        "terminal-lifecycle-matrix-timeout");
  }

  private static void RequireStatus(
      SupportDiagnosticSessionResponse session,
      string expectedStatus,
      bool requireDispatch,
      bool requireResult)
  {
    if (session.Status != expectedStatus ||
        requireDispatch !=
            (session.DispatchedAt is not null) ||
        requireResult != (session.Result is not null))
    {
      throw new CanaryScenarioFailureException(
          "terminal-lifecycle-matrix-mismatch");
    }
  }

  private static Guid ParseSessionId(
      SupportDiagnosticSessionResponse session) =>
      Guid.TryParse(
          session.SessionId,
          CultureInfo.InvariantCulture,
          out var sessionId) &&
      sessionId != Guid.Empty
          ? sessionId
          : throw new CanaryScenarioFailureException(
              "terminal-lifecycle-matrix-mismatch");

  private sealed record DormantSupportNode(
      Guid NodeId,
      string TransportCredential,
      string RelayUrl);

  private sealed record DormantRelayPollResponse(
      Guid SessionId,
      string RequestEnvelope,
      DateTimeOffset ExpiresAt);

  private sealed record EnrollmentCredentialPayload(
      string Schema,
      Guid NodeId,
      Guid CompletionId,
      string TransportCredential);
}
