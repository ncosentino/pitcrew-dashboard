using System.Reflection;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NexusLabs.Needlr;

using PitCrew.Protocol;

namespace PitCrew.Connector.Features.Sync;

[DoNotAutoRegister]
internal sealed partial class ConnectorWorker(
    ConnectorIdentityStore _identityStore,
    ConnectorApiClient _apiClient,
    ObservedStateReader _observedStateReader,
    IOptions<ConnectorOptions> _options,
    TimeProvider _timeProvider,
    ILogger<ConnectorWorker> _logger) : BackgroundService
{
  private static readonly string ConnectorVersion =
      Assembly.GetExecutingAssembly().GetName().Version?.ToString() ??
      "0.0.0";

  protected override async Task ExecuteAsync(
      CancellationToken stoppingToken)
  {
    ValidateTransport();
    var identity = await EnrollWithRetryAsync(stoppingToken);
    var successfulPollDelay = TimeSpan.FromSeconds(
        _options.Value.PollSeconds);
    var nextDelay = successfulPollDelay;
    var consecutiveFailures = 0;
    var lastSentHash = string.Empty;
    var lastSentAt = DateTimeOffset.MinValue;

    while (!stoppingToken.IsCancellationRequested)
    {
      try
      {
        var observedState = await _observedStateReader.ReadAsync(
            stoppingToken);
        if (!observedState.IsComplete)
        {
          consecutiveFailures++;
          nextDelay = CalculateBackoff(consecutiveFailures);
          LogIncompleteObservation(nextDelay);
        }
        else
        {
          var now = _timeProvider.GetUtcNow();
          var heartbeatDue =
              now - lastSentAt >=
              TimeSpan.FromSeconds(
                  _options.Value.HeartbeatSeconds);
          if (!string.Equals(
              lastSentHash,
              observedState.AggregateHash,
              StringComparison.Ordinal) ||
              heartbeatDue)
          {
            var response = await _apiClient.SyncAsync(
                identity.Credential!,
                new ConnectorSyncRequest(
                    PitCrewProtocol.Version,
                    ConnectorVersion,
                    now,
                    observedState.Profiles),
                stoppingToken);
            consecutiveFailures = 0;
            lastSentHash = observedState.AggregateHash;
            lastSentAt = now;
            successfulPollDelay = TimeSpan.FromSeconds(
                Math.Clamp(
                    response.NextPollSeconds,
                    5,
                    3600));
            nextDelay = successfulPollDelay;
            LogSynchronized(
                observedState.Profiles.Count,
                nextDelay);
          }
          else
          {
            consecutiveFailures = 0;
            nextDelay = successfulPollDelay;
          }
        }
      }
      catch (HttpRequestException exception)
          when (exception.StatusCode == System.Net.HttpStatusCode.Unauthorized)
      {
        LogCredentialRejected();
        throw new InvalidOperationException(
            "The connector credential was revoked; explicit re-enrollment is required.",
            exception);
      }
      catch (HttpRequestException exception)
          when (exception.StatusCode is not null &&
              (int)exception.StatusCode.Value is >= 400 and < 500 &&
              exception.StatusCode != System.Net.HttpStatusCode.TooManyRequests)
      {
        LogPayloadRejected(exception.Message);
        throw new InvalidOperationException(
            "The dashboard permanently rejected the connector payload.",
            exception);
      }
      catch (HttpRequestException exception)
      {
        consecutiveFailures++;
        nextDelay = CalculateBackoff(consecutiveFailures);
        LogSyncFailure(
            exception.Message,
            nextDelay);
      }
      catch (IOException exception)
      {
        consecutiveFailures++;
        nextDelay = CalculateBackoff(consecutiveFailures);
        LogSyncFailure(
            exception.Message,
            nextDelay);
      }
      catch (OperationCanceledException exception)
          when (!stoppingToken.IsCancellationRequested)
      {
        consecutiveFailures++;
        nextDelay = CalculateBackoff(consecutiveFailures);
        LogSyncFailure(
            exception.Message,
            nextDelay);
      }

      var jitterFactor = 0.8 +
          (Random.Shared.NextDouble() * 0.4);
      var jitteredDelay = TimeSpan.FromMilliseconds(
          nextDelay.TotalMilliseconds * jitterFactor);
      await Task.Delay(
          jitteredDelay,
          _timeProvider,
          stoppingToken);
    }
  }

  private async Task<ConnectorIdentity> EnrollWithRetryAsync(
      CancellationToken cancellationToken)
  {
    var failures = 0;
    while (true)
    {
      try
      {
        return await EnsureEnrolledAsync(cancellationToken);
      }
      catch (HttpRequestException exception)
          when (exception.StatusCode == System.Net.HttpStatusCode.Unauthorized)
      {
        LogEnrollmentRejected();
        throw new InvalidOperationException(
            "The dashboard rejected the connector enrollment token.",
            exception);
      }
      catch (HttpRequestException exception)
          when (exception.StatusCode is not null &&
              (int)exception.StatusCode.Value is >= 400 and < 500 &&
              exception.StatusCode != System.Net.HttpStatusCode.TooManyRequests)
      {
        LogEnrollmentRejected();
        throw new InvalidOperationException(
            "The dashboard permanently rejected connector enrollment.",
            exception);
      }
      catch (HttpRequestException exception)
      {
        failures++;
        var delay = CalculateBackoff(failures);
        LogEnrollmentFailure(exception.Message, delay);
        await Task.Delay(delay, cancellationToken);
      }
      catch (OperationCanceledException exception)
          when (!cancellationToken.IsCancellationRequested)
      {
        failures++;
        var delay = CalculateBackoff(failures);
        LogEnrollmentFailure(exception.Message, delay);
        await Task.Delay(delay, cancellationToken);
      }
    }
  }

  private async Task<ConnectorIdentity> EnsureEnrolledAsync(
      CancellationToken cancellationToken)
  {
    var identity = await _identityStore.LoadOrCreatePendingAsync(
        cancellationToken);
    if (identity.NodeId is not null &&
        !string.IsNullOrWhiteSpace(identity.Credential))
    {
      return identity;
    }
    if (string.IsNullOrWhiteSpace(_options.Value.EnrollmentToken))
    {
      throw new InvalidOperationException(
          "Connector enrollment requires PitCrew:Connector:EnrollmentToken until an identity has been issued.");
    }

    var response = await _apiClient.EnrollAsync(
        new ConnectorEnrollmentRequest(
            identity.ConnectorInstanceId,
            _options.Value.DisplayName),
        cancellationToken);
    var enrolled = identity with
    {
      NodeId = response.NodeId,
      Credential = response.Credential,
    };
    await _identityStore.SaveAsync(
        enrolled,
        cancellationToken);
    LogEnrolled(response.NodeId);
    return enrolled;
  }

  private void ValidateTransport()
  {
    if (!Uri.TryCreate(
        _options.Value.DashboardUrl,
        UriKind.Absolute,
        out var dashboardUri))
    {
      throw new InvalidOperationException(
          "Dashboard URL is not an absolute URI.");
    }
    if (!_options.Value.AllowInsecureHttp &&
        !string.Equals(
            dashboardUri.Scheme,
            Uri.UriSchemeHttps,
            StringComparison.OrdinalIgnoreCase))
    {
      throw new InvalidOperationException(
          "Dashboard URL must use HTTPS unless insecure local HTTP is explicitly enabled.");
    }
  }

  private TimeSpan CalculateBackoff(int consecutiveFailures)
  {
    var exponentialSeconds = Math.Pow(
        2,
        Math.Min(consecutiveFailures, 10));
    return TimeSpan.FromSeconds(
        Math.Min(
            exponentialSeconds,
            _options.Value.MaximumBackoffSeconds));
  }

  [LoggerMessage(
      Level = LogLevel.Information,
      Message = "Enrolled connector as node {NodeId}.")]
  private partial void LogEnrolled(Guid nodeId);

  [LoggerMessage(
      Level = LogLevel.Information,
      Message = "Synchronized {ProfileCount} profiles; next sync in {NextDelay}.")]
  private partial void LogSynchronized(
      int profileCount,
      TimeSpan nextDelay);

  [LoggerMessage(
      Level = LogLevel.Warning,
      Message = "Skipped an incomplete observed-state read; retrying in {NextDelay}.")]
  private partial void LogIncompleteObservation(TimeSpan nextDelay);

  [LoggerMessage(
      Level = LogLevel.Warning,
      Message = "Dashboard synchronization failed: {Reason}. Retrying in {NextDelay}.")]
  private partial void LogSyncFailure(
      string reason,
      TimeSpan nextDelay);

  [LoggerMessage(
      Level = LogLevel.Critical,
      Message = "Dashboard rejected this connector credential. Automatic re-enrollment is disabled.")]
  private partial void LogCredentialRejected();

  [LoggerMessage(
      Level = LogLevel.Critical,
      Message = "Dashboard permanently rejected the synchronization payload: {Reason}")]
  private partial void LogPayloadRejected(string reason);

  [LoggerMessage(
      Level = LogLevel.Critical,
      Message = "Dashboard rejected the connector enrollment token.")]
  private partial void LogEnrollmentRejected();

  [LoggerMessage(
      Level = LogLevel.Warning,
      Message = "Connector enrollment failed: {Reason}. Retrying in {NextDelay}.")]
  private partial void LogEnrollmentFailure(
      string reason,
      TimeSpan nextDelay);
}
