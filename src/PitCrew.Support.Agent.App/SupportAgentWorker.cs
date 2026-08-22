namespace PitCrew.Support.Agent.App;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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
            _startupStatus.Clear();
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
        catch (IOException)
        {
          LogBrokerUnavailable(_logger);
        }
        catch (TimeoutException)
        {
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
    if (result is null)
    {
      return true;
    }
    var upload = await _relayClient.UploadResultAsync(
        options,
        poll.Response.SessionId,
        result,
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
    return true;
  }

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
}
