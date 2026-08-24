namespace PitCrew.Support.Agent.App;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using PitCrew.Support.Protocol;

internal sealed partial class SupportAgentWorker(
    SupportNodeIdentityProvisioner _identityProvisioner,
    SupportNodeIdentityStore _identityStore,
    SupportAgentBootstrapOptions _bootstrapOptions,
    SupportRelayTransportClient _relayClient,
    SupportAgentStartupStatusWriter _startupStatus,
    TimeProvider _timeProvider,
    ILogger<SupportAgentWorker> _logger) : BackgroundService
{
  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    const string identityPhase = "identity-provisioning";
    var phase = identityPhase;
    _startupStatus.Clear();
    try
    {
      SupportAgentProvisioningOutcome provisioning;
      try
      {
        provisioning =
            await _identityProvisioner.GetRuntimeOptionsAsync(stoppingToken);
      }
      catch (HttpRequestException exception)
      {
        _startupStatus.Write(
            identityPhase,
            "dashboard-unavailable",
            exception.GetType());
        LogRelayUnavailable(_logger);
        return;
      }
      if (provisioning.Options is not { } options)
      {
        _startupStatus.Write(
            identityPhase,
            GetProvisioningDisposition(provisioning.Status),
            exceptionType: null);
        SupportAgentIdentityLog.IdentityUnavailable(_logger);
        return;
      }
      using var timer = new PeriodicTimer(
          TimeSpan.FromSeconds(15),
          _timeProvider);
      var firstPollAccepted = false;
      do
      {
        phase = "relay-poll";
        try
        {
          await using var operationLock =
              await _identityStore.AcquireOperationLockAsync(stoppingToken);
          var current = await _identityStore.LoadActiveAsync(stoppingToken);
          if (current is null)
          {
            _startupStatus.Write(
                "local-identity",
                "identity-unavailable",
                exceptionType: null);
            SupportAgentIdentityLog.IdentityUnavailable(_logger);
            return;
          }
          options = SupportAgentOptions.FromStoredIdentity(
              current,
              _bootstrapOptions.SocketPath);
          var processor = new SupportAgentRequestProcessor(
              options,
              new PlatformDiagnosticsBroker(options),
              new AgentReplayCache(options.ReplayRoot),
              _timeProvider);
          if (!await PollOnceAsync(options, processor, stoppingToken))
          {
            _startupStatus.Write(
                phase,
                "credential-rejected",
                exceptionType: null);
            return;
          }
          if (!firstPollAccepted)
          {
            _startupStatus.Write(
                phase,
                "accepted",
                exceptionType: null);
            firstPollAccepted = true;
          }
          phase = "running";
        }
        catch (HttpRequestException)
        {
          LogRelayUnavailable(_logger);
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
          LogRelayUnavailable(_logger);
        }
        catch (IOException exception)
        {
          _startupStatus.Write(
              "request-processing",
              "broker-io-unavailable",
              exception.GetType());
          LogBrokerUnavailable(_logger);
        }
        catch (TimeoutException exception)
        {
          _startupStatus.Write(
              "request-processing",
              "broker-timeout",
              exception.GetType());
          LogBrokerUnavailable(_logger);
        }
        catch (System.Text.Json.JsonException)
        {
          LogRelayResponseInvalid(_logger);
        }
      }
      while (await timer.WaitForNextTickAsync(stoppingToken));
    }
    catch (Exception exception)
        when (exception is not OperationCanceledException ||
              !stoppingToken.IsCancellationRequested)
    {
      _startupStatus.Write(
          phase,
          "unhandled-exception",
          exception.GetType());
      throw;
    }
  }

  private static string GetProvisioningDisposition(
      SupportAgentProvisioningStatus status) =>
      status switch
      {
        SupportAgentProvisioningStatus.ActiveIdentityUnavailable =>
            "active-identity-unavailable",
        SupportAgentProvisioningStatus.IdentityLifecycleUnavailable =>
            "identity-lifecycle-unavailable",
        SupportAgentProvisioningStatus.EnrollmentMaterialUnavailable =>
            "enrollment-material-unavailable",
        SupportAgentProvisioningStatus.PendingIdentityUnavailable =>
            "pending-identity-unavailable",
        SupportAgentProvisioningStatus.EnrollmentRejected =>
            "enrollment-rejected",
        SupportAgentProvisioningStatus.LocalEnrollmentCommitFailed =>
            "local-enrollment-commit-failed",
        SupportAgentProvisioningStatus.LegacyConfigurationUnavailable =>
            "legacy-configuration-unavailable",
        _ => throw new InvalidOperationException(
            "A ready provisioning result did not include runtime options."),
      };

  private async Task<bool> PollOnceAsync(
      SupportAgentOptions options,
      SupportAgentRequestProcessor processor,
      CancellationToken cancellationToken)
  {
    var poll = await _relayClient.PollAsync(options, cancellationToken);
    if (!poll.CredentialAccepted)
    {
      await _identityStore.MarkAuthorizationRejectedAsync(cancellationToken);
      SupportAgentIdentityLog.CredentialRejected(_logger);
      return false;
    }
    var requestEnvelope = poll.Response?.GetRequestEnvelopeOrNull();
    if (poll.Response is null || requestEnvelope is null)
    {
      return true;
    }
    var result = await processor.ProcessAsync(
        poll.Response.SessionId,
        requestEnvelope,
        cancellationToken);
    if (result.ResultEnvelope is null)
    {
      var disposition = GetProcessingDisposition(result);
      _startupStatus.Write(
          "request-processing",
          disposition,
          exceptionType: null);
      LogRequestRejected(
          _logger,
          poll.Response.SessionId,
          disposition);
      var report = await _relayClient.ReportRejectionAsync(
          options,
          poll.Response.SessionId,
          disposition,
          cancellationToken);
      if (report ==
          SupportRelayOutcomeReportStatus.CredentialRejected)
      {
        await _identityStore.MarkAuthorizationRejectedAsync(
            cancellationToken);
        SupportAgentIdentityLog.CredentialRejected(_logger);
        return false;
      }
      return true;
    }
    var upload = await _relayClient.UploadResultAsync(
        options,
        poll.Response.SessionId,
        result.ResultEnvelope,
        cancellationToken);
    if (upload == SupportRelayUploadOutcome.CredentialRejected)
    {
      await _identityStore.MarkAuthorizationRejectedAsync(cancellationToken);
      SupportAgentIdentityLog.CredentialRejected(_logger);
      return false;
    }
    if (upload != SupportRelayUploadOutcome.Succeeded)
    {
      throw new HttpRequestException(
          "The support relay rejected the result upload.");
    }
    _startupStatus.Write(
        "request-processing",
        "completed",
        exceptionType: null);
    return true;
  }

  private static string GetProcessingDisposition(
      SupportAgentRequestProcessingResult result) =>
      result.Status switch
      {
        SupportAgentRequestProcessingStatus.EnvelopeUnsupported =>
            SupportRequestRejectionDispositions
                .EnvelopeUnsupported,
        SupportAgentRequestProcessingStatus.EnvelopeSignatureRejected =>
            SupportRequestRejectionDispositions
                .EnvelopeSignatureRejected,
        SupportAgentRequestProcessingStatus.EnvelopePayloadRejected =>
            SupportRequestRejectionDispositions
                .EnvelopePayloadRejected,
        SupportAgentRequestProcessingStatus.RequestMalformed =>
            SupportRequestRejectionDispositions
                .RequestMalformed,
        SupportAgentRequestProcessingStatus.SessionMismatch =>
            SupportRequestRejectionDispositions
                .SessionMismatch,
        SupportAgentRequestProcessingStatus.ValidationRejected =>
            result.ValidationStatus switch
            {
              SupportRequestValidationStatus.WrongTenantOrNode =>
                  SupportRequestRejectionDispositions
                      .WrongTenantOrNode,
              SupportRequestValidationStatus.UnsupportedCapability =>
                  SupportRequestRejectionDispositions
                      .UnsupportedCapability,
              SupportRequestValidationStatus.UnsupportedDiagnosticMode =>
                  SupportRequestRejectionDispositions
                      .UnsupportedDiagnosticMode,
              SupportRequestValidationStatus.Expired =>
                  SupportRequestRejectionDispositions
                      .RequestExpired,
              SupportRequestValidationStatus.InvalidNonce =>
                  SupportRequestRejectionDispositions
                      .InvalidNonce,
              SupportRequestValidationStatus.Replay =>
                  SupportRequestRejectionDispositions
                      .RequestReplay,
              _ => SupportRequestRejectionDispositions
                  .ValidationRejected,
            },
        SupportAgentRequestProcessingStatus.ReplayPending =>
            SupportRequestRejectionDispositions.ReplayPending,
        SupportAgentRequestProcessingStatus.BrokerMarkdownRejected =>
            SupportRequestRejectionDispositions
                .BrokerMarkdownRejected,
        SupportAgentRequestProcessingStatus.BrokerReportRejected =>
            SupportRequestRejectionDispositions
                .BrokerReportRejected,
        _ => SupportRequestRejectionDispositions
            .ResultUnavailable,
      };

  [LoggerMessage(
      EventId = 1,
      Level = LogLevel.Warning,
      Message = "The support relay is temporarily unavailable; polling will retry.")]
  private static partial void LogRelayUnavailable(ILogger logger);

  [LoggerMessage(
      EventId = 2,
      Level = LogLevel.Warning,
      Message = "The local support diagnostics broker is temporarily unavailable; polling will retry.")]
  private static partial void LogBrokerUnavailable(ILogger logger);

  [LoggerMessage(
      EventId = 3,
      Level = LogLevel.Warning,
      Message = "The support relay returned an invalid response; polling will retry.")]
  private static partial void LogRelayResponseInvalid(ILogger logger);

  [LoggerMessage(
      EventId = 4,
      Level = LogLevel.Warning,
      Message = "Support request {SessionId} was rejected with disposition {Disposition}.")]
  private static partial void LogRequestRejected(
      ILogger logger,
      Guid sessionId,
      string disposition);
}
