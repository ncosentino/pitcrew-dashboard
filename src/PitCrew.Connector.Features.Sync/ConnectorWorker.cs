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
    CapacityCommandExecutor _capacityCommandExecutor,
    RecoveryCommandExecutor _recoveryCommandExecutor,
    ConnectorHealthJournal _healthJournal,
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
    await _healthJournal.RecordProcessStartedAsync(
        _timeProvider.GetUtcNow(),
        stoppingToken);
    try
    {
      await RunAsync(stoppingToken);
    }
    finally
    {
      if (stoppingToken.IsCancellationRequested)
      {
        await _healthJournal.RecordProcessStoppingAsync(
            _timeProvider.GetUtcNow(),
            CancellationToken.None);
      }
    }
  }

  private async Task RunAsync(
      CancellationToken stoppingToken)
  {
    try
    {
      ValidateTransport();
    }
    catch (InvalidOperationException)
    {
      await _healthJournal.RecordFailureAsync(
          ConnectorHealthEventKinds.Rejected,
          new ConnectorHealthFailure(
              ConnectorHealthFailureCategories.ConfigurationInvalid,
              "Connector configuration is invalid."),
          1,
          null,
          _timeProvider.GetUtcNow(),
          stoppingToken);
      throw;
    }
    var identity = await EnrollWithRetryAsync(stoppingToken);
    var successfulPollDelay = TimeSpan.FromSeconds(
        _options.Value.PollSeconds);
    var nextDelay = successfulPollDelay;
    var consecutiveFailures = 0;
    var lastSentHash = string.Empty;
    var lastSentAt = DateTimeOffset.MinValue;
    CapacityCommandOutcome? pendingCapacityOutcome = null;
    RecoveryCommandProgress? pendingRecoveryProgress = null;
    var pendingRecoveryOutcomes = new Queue<RecoveryCommandOutcome>(
        await _recoveryCommandExecutor.ResolveInterruptedAsync(stoppingToken));
    RecoveryCommandOutcome? pendingRecoveryOutcome = null;

    while (!stoppingToken.IsCancellationRequested)
    {
      var delayIsJittered = false;
      try
      {
        var observedState = await _observedStateReader.ReadAsync(
            stoppingToken);
        if (!observedState.IsComplete)
        {
          consecutiveFailures++;
          nextDelay = CalculateJitteredDelay(
              CalculateBackoff(consecutiveFailures));
          delayIsJittered = true;
          await _healthJournal.RecordFailureAsync(
              ConnectorHealthEventKinds.ObservationIncomplete,
              observedState.Failure ??
                  new ConnectorHealthFailure(
                      ConnectorHealthFailureCategories.ProfileStateUnreadable,
                      "Connector observation is incomplete."),
              consecutiveFailures,
              nextDelay,
              _timeProvider.GetUtcNow(),
              stoppingToken);
          LogIncompleteObservation(nextDelay);
        }
        else
        {
          var now = _timeProvider.GetUtcNow();
          var capacityOperator =
              await _capacityCommandExecutor.ReadCapabilityAsync(
                  stoppingToken);
          var recoveryOperator =
              await _recoveryCommandExecutor.ReadCapabilityAsync(
                  stoppingToken);
          if (pendingRecoveryOutcome is null &&
              pendingRecoveryOutcomes.Count > 0)
          {
            pendingRecoveryOutcome = pendingRecoveryOutcomes.Dequeue();
          }
          var heartbeatDue =
              now - lastSentAt >=
              TimeSpan.FromSeconds(
                  _options.Value.HeartbeatSeconds);
          if (!string.Equals(
              lastSentHash,
              observedState.AggregateHash,
              StringComparison.Ordinal) ||
              heartbeatDue ||
              pendingCapacityOutcome is not null ||
              pendingRecoveryProgress is not null ||
              pendingRecoveryOutcome is not null)
          {
            await _healthJournal.RecordSynchronizationAttemptAsync(
                now,
                stoppingToken);
            var response = await _apiClient.SyncAsync(
                identity.Credential!,
                new ConnectorSyncRequest(
                    PitCrewProtocol.Version,
                    ConnectorVersion,
                    now,
                    observedState.Profiles,
                    capacityOperator,
                    pendingCapacityOutcome,
                    recoveryOperator,
                    pendingRecoveryProgress,
                    pendingRecoveryOutcome),
                stoppingToken);
            pendingCapacityOutcome = null;
            pendingRecoveryProgress = null;
            pendingRecoveryOutcome = null;
            if (response.CredentialRotation is not null)
            {
              identity = identity with
              {
                Credential =
                    response.CredentialRotation.Credential,
              };
              await _identityStore.SaveAsync(
                  identity,
                  stoppingToken);
              LogCredentialRotated();
            }
            consecutiveFailures = 0;
            lastSentHash = observedState.AggregateHash;
            lastSentAt = now;
            successfulPollDelay = TimeSpan.FromSeconds(
                Math.Clamp(
                    response.NextPollSeconds,
                    5,
                    3600));
            nextDelay = successfulPollDelay;
            if (response.CapacityCommand is not null)
            {
              pendingCapacityOutcome =
                  await _capacityCommandExecutor.ExecuteAsync(
                      response.CapacityCommand,
                      stoppingToken);
              lastSentHash = string.Empty;
              lastSentAt = DateTimeOffset.MinValue;
              nextDelay = TimeSpan.Zero;
            }
            if (response.RecoveryCommand is not null)
            {
              var report =
                  await _recoveryCommandExecutor.ExecuteAsync(
                      response.RecoveryCommand,
                      stoppingToken);
              pendingRecoveryProgress = report.Progress;
              pendingRecoveryOutcome = report.Outcome;
              lastSentHash = string.Empty;
              lastSentAt = DateTimeOffset.MinValue;
              nextDelay = TimeSpan.Zero;
            }
            LogSynchronized(
                observedState.Profiles.Count,
                nextDelay);
            await _healthJournal.RecordSynchronizationSucceededAsync(
                _timeProvider.GetUtcNow(),
                stoppingToken);
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
        await _healthJournal.RecordFailureAsync(
            ConnectorHealthEventKinds.Rejected,
            new ConnectorHealthFailure(
                ConnectorHealthFailureCategories.CredentialRejected,
                "Dashboard rejected the connector credential."),
            consecutiveFailures + 1,
            null,
            _timeProvider.GetUtcNow(),
            stoppingToken);
        if (string.IsNullOrWhiteSpace(
            _options.Value.EnrollmentCode))
        {
          throw new InvalidOperationException(
              "The connector credential was revoked; configure a new one-time enrollment code and restart to re-enroll.",
              exception);
        }
        identity = await ReEnrollAsync(
            identity,
            stoppingToken);
        consecutiveFailures = 0;
        lastSentHash = string.Empty;
        lastSentAt = DateTimeOffset.MinValue;
        nextDelay = successfulPollDelay;
      }
      catch (HttpRequestException exception)
          when (exception.StatusCode is not null &&
              (int)exception.StatusCode.Value is >= 400 and < 500 &&
              exception.StatusCode != System.Net.HttpStatusCode.TooManyRequests)
      {
        LogPayloadRejected(exception.Message);
        await _healthJournal.RecordFailureAsync(
            ConnectorHealthEventKinds.Rejected,
            new ConnectorHealthFailure(
                ConnectorHealthFailureCategories.PayloadRejected,
                "Dashboard permanently rejected the synchronization payload."),
            consecutiveFailures + 1,
            null,
            _timeProvider.GetUtcNow(),
            stoppingToken);
        throw new InvalidOperationException(
            "The dashboard permanently rejected the connector payload.",
            exception);
      }
      catch (HttpRequestException exception)
      {
        consecutiveFailures++;
        nextDelay = CalculateJitteredDelay(
            CalculateBackoff(consecutiveFailures));
        delayIsJittered = true;
        await _healthJournal.RecordFailureAsync(
            ConnectorHealthEventKinds.SynchronizationFailed,
            ClassifyHttpFailure(
                exception,
                enrollment: false),
            consecutiveFailures,
            nextDelay,
            _timeProvider.GetUtcNow(),
            stoppingToken);
        LogSyncFailure(
            exception.Message,
            nextDelay);
      }
      catch (IOException exception)
      {
        consecutiveFailures++;
        nextDelay = CalculateJitteredDelay(
            CalculateBackoff(consecutiveFailures));
        delayIsJittered = true;
        await _healthJournal.RecordFailureAsync(
            ConnectorHealthEventKinds.SynchronizationFailed,
            new ConnectorHealthFailure(
                ConnectorHealthFailureCategories.SynchronizationIo,
                "Connector synchronization could not read or write local state."),
            consecutiveFailures,
            nextDelay,
            _timeProvider.GetUtcNow(),
            stoppingToken);
        LogSyncFailure(
            exception.Message,
            nextDelay);
      }
      catch (OperationCanceledException exception)
          when (!stoppingToken.IsCancellationRequested)
      {
        consecutiveFailures++;
        nextDelay = CalculateJitteredDelay(
            CalculateBackoff(consecutiveFailures));
        delayIsJittered = true;
        await _healthJournal.RecordFailureAsync(
            ConnectorHealthEventKinds.SynchronizationFailed,
            ClassifyTimeout(enrollment: false),
            consecutiveFailures,
            nextDelay,
            _timeProvider.GetUtcNow(),
            stoppingToken);
        LogSyncFailure(
            exception.Message,
            nextDelay);
      }

      var effectiveDelay = delayIsJittered
          ? nextDelay
          : CalculateJitteredDelay(nextDelay);
      await Task.Delay(
          effectiveDelay,
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
        await _healthJournal.RecordFailureAsync(
            ConnectorHealthEventKinds.Rejected,
            new ConnectorHealthFailure(
                ConnectorHealthFailureCategories.EnrollmentRejected,
                "Dashboard rejected connector enrollment."),
            failures + 1,
            null,
            _timeProvider.GetUtcNow(),
            cancellationToken);
        throw new InvalidOperationException(
            "The dashboard rejected the one-time connector enrollment code.",
            exception);
      }
      catch (HttpRequestException exception)
          when (exception.StatusCode is not null &&
              (int)exception.StatusCode.Value is >= 400 and < 500 &&
              exception.StatusCode != System.Net.HttpStatusCode.TooManyRequests)
      {
        LogEnrollmentRejected();
        await _healthJournal.RecordFailureAsync(
            ConnectorHealthEventKinds.Rejected,
            new ConnectorHealthFailure(
                ConnectorHealthFailureCategories.EnrollmentRejected,
                "Dashboard permanently rejected connector enrollment."),
            failures + 1,
            null,
            _timeProvider.GetUtcNow(),
            cancellationToken);
        throw new InvalidOperationException(
            "The dashboard permanently rejected connector enrollment.",
            exception);
      }
      catch (HttpRequestException exception)
      {
        failures++;
        var delay = CalculateBackoff(failures);
        await _healthJournal.RecordFailureAsync(
            ConnectorHealthEventKinds.EnrollmentFailed,
            ClassifyHttpFailure(
                exception,
                enrollment: true),
            failures,
            delay,
            _timeProvider.GetUtcNow(),
            cancellationToken);
        LogEnrollmentFailure(exception.Message, delay);
        await Task.Delay(delay, cancellationToken);
      }
      catch (OperationCanceledException exception)
          when (!cancellationToken.IsCancellationRequested)
      {
        failures++;
        var delay = CalculateBackoff(failures);
        await _healthJournal.RecordFailureAsync(
            ConnectorHealthEventKinds.EnrollmentFailed,
            ClassifyTimeout(enrollment: true),
            failures,
            delay,
            _timeProvider.GetUtcNow(),
            cancellationToken);
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
    if (string.IsNullOrWhiteSpace(_options.Value.EnrollmentCode))
    {
      await _healthJournal.RecordFailureAsync(
          ConnectorHealthEventKinds.Rejected,
          new ConnectorHealthFailure(
              ConnectorHealthFailureCategories.EnrollmentConfiguration,
              "Connector enrollment configuration is incomplete."),
          1,
          null,
          _timeProvider.GetUtcNow(),
          cancellationToken);
      throw new InvalidOperationException(
          "Connector enrollment requires PitCrew:Connector:EnrollmentCode until an identity has been issued.");
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

  private async Task<ConnectorIdentity> ReEnrollAsync(
      ConnectorIdentity identity,
      CancellationToken cancellationToken)
  {
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

  private static TimeSpan CalculateJitteredDelay(TimeSpan delay)
  {
    var jitterFactor = 0.8 +
        (Random.Shared.NextDouble() * 0.4);
    return TimeSpan.FromMilliseconds(
        delay.TotalMilliseconds * jitterFactor);
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

  internal static ConnectorHealthFailure ClassifyHttpFailure(
      HttpRequestException exception,
      bool enrollment)
  {
    if (exception.StatusCode ==
        System.Net.HttpStatusCode.TooManyRequests)
    {
      return new ConnectorHealthFailure(
          enrollment
              ? ConnectorHealthFailureCategories.EnrollmentRateLimited
              : ConnectorHealthFailureCategories.SynchronizationRateLimited,
          enrollment
              ? "Dashboard rate-limited connector enrollment."
              : "Dashboard rate-limited connector synchronization.");
    }
    if (exception.StatusCode is not null &&
        (int)exception.StatusCode.Value >= 500)
    {
      return new ConnectorHealthFailure(
          enrollment
              ? ConnectorHealthFailureCategories.EnrollmentServer
              : ConnectorHealthFailureCategories.SynchronizationServer,
          enrollment
              ? "Dashboard returned a transient server error during enrollment."
              : "Dashboard returned a transient server error during synchronization.");
    }
    return new ConnectorHealthFailure(
        enrollment
            ? ConnectorHealthFailureCategories.EnrollmentNetwork
            : ConnectorHealthFailureCategories.SynchronizationNetwork,
        enrollment
            ? "Connector enrollment could not reach Dashboard."
            : "Connector synchronization could not reach Dashboard.");
  }

  internal static ConnectorHealthFailure ClassifyTimeout(
      bool enrollment) =>
      new(
          enrollment
              ? ConnectorHealthFailureCategories.EnrollmentTimeout
              : ConnectorHealthFailureCategories.SynchronizationTimeout,
          enrollment
              ? "Connector enrollment timed out."
              : "Dashboard synchronization timed out.");

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
      Level = LogLevel.Warning,
      Message = "Dashboard rejected this connector credential.")]
  private partial void LogCredentialRejected();

  [LoggerMessage(
      Level = LogLevel.Information,
      Message = "Persisted a rotated connector credential.")]
  private partial void LogCredentialRotated();

  [LoggerMessage(
      Level = LogLevel.Critical,
      Message = "Dashboard permanently rejected the synchronization payload: {Reason}")]
  private partial void LogPayloadRejected(string reason);

  [LoggerMessage(
      Level = LogLevel.Critical,
      Message = "Dashboard rejected the one-time connector enrollment code.")]
  private partial void LogEnrollmentRejected();

  [LoggerMessage(
      Level = LogLevel.Warning,
      Message = "Connector enrollment failed: {Reason}. Retrying in {NextDelay}.")]
  private partial void LogEnrollmentFailure(
      string reason,
      TimeSpan nextDelay);
}
