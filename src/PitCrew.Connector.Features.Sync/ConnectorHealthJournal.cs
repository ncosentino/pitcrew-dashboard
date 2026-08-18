using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NexusLabs.Needlr;

using PitCrew.Protocol;

namespace PitCrew.Connector.Features.Sync;

[DoNotAutoRegister]
internal sealed partial class ConnectorHealthJournal(
    IOptions<ConnectorOptions> _options,
    TimeProvider _timeProvider,
    ILogger<ConnectorHealthJournal> _logger)
{
  private const int SchemaVersion = 1;
  private const int MaximumEvents = 256;
  private const int MaximumEventLineCharacters = 4096;
  private const int MaximumSnapshotBytes = 65_536;
  private const int AttemptUpdatePriority = 0;
  private const int StateUpdatePriority = 1;
  private const int MaximumPendingUpdates = 256;
  private const int MaximumEventJournalBytes =
      MaximumEvents * (MaximumEventLineCharacters + 1);
  private static readonly TimeSpan MaximumWriteWait =
      TimeSpan.FromSeconds(1);
  private readonly object _updateStateLock = new();
  private readonly Queue<PendingUpdate> _pendingUpdates = new();
  private bool _updateInProgress;
  private ConnectorHealthSnapshot? _snapshot;

  public Task RecordProcessStartedAsync(
      DateTimeOffset occurredAt,
      CancellationToken cancellationToken) =>
      RecordBestEffortAsync(
          token => UpdateAsync(
          occurredAt,
          ConnectorHealthEventKinds.ProcessStarted,
          snapshot => snapshot with
          {
            State = snapshot.ActiveOutageId is null
                ? ConnectorHealthStates.Starting
                : ConnectorHealthStates.Degraded,
            ProcessStartedAt = occurredAt,
            UpdatedAt = occurredAt,
          },
          null,
          token),
          cancellationToken);

  public Task RecordSynchronizationAttemptAsync(
      DateTimeOffset occurredAt,
      CancellationToken cancellationToken) =>
      RecordBestEffortAsync(
          token => UpdateSnapshotOnlyAsync(
          occurredAt,
          snapshot => snapshot with
          {
            LastAttemptAt = occurredAt,
            UpdatedAt = occurredAt,
          },
          token),
          cancellationToken,
          AttemptUpdatePriority);

  public Task RecordFailureAsync(
      string eventKind,
      ConnectorHealthFailure failure,
      int consecutiveFailures,
      TimeSpan? retryDelay,
      DateTimeOffset occurredAt,
      CancellationToken cancellationToken)
  {
    var safeFailure = SanitizeFailure(failure);
    return RecordBestEffortAsync(
        token => UpdateAsync(
          occurredAt,
          eventKind,
          snapshot =>
          {
            var outageId = snapshot.ActiveOutageId ?? Guid.NewGuid();
            var outageStartedAt =
                snapshot.ActiveOutageStartedAt ?? occurredAt;
            return snapshot with
            {
              State = ConnectorHealthStates.Degraded,
              UpdatedAt = occurredAt,
              LastAttemptAt = occurredAt,
              ActiveOutageId = outageId,
              ActiveOutageStartedAt = outageStartedAt,
              LastFailureAt = occurredAt,
              LastFailureCategory = safeFailure.Category,
              LastFailureProfileId = safeFailure.ProfileId,
              LastFailureDetail = safeFailure.Detail,
              ConsecutiveFailures = consecutiveFailures,
              NextRetryAt = retryDelay is null
                  ? null
                  : occurredAt.Add(retryDelay.Value),
            };
          },
          safeFailure,
          token),
        cancellationToken);
  }

  public Task RecordSynchronizationSucceededAsync(
      DateTimeOffset occurredAt,
      CancellationToken cancellationToken) =>
      RecordBestEffortAsync(
          token => UpdateAsync(
          occurredAt,
          ConnectorHealthEventKinds.SynchronizationSucceeded,
          snapshot =>
          {
            var recoveredOutageId = snapshot.ActiveOutageId;
            var recoveredOutageStartedAt =
                snapshot.ActiveOutageStartedAt;
            var recoveredFailureCategory =
                snapshot.LastFailureCategory;
            return snapshot with
            {
              State = ConnectorHealthStates.Healthy,
              UpdatedAt = occurredAt,
              LastAttemptAt = occurredAt,
              LastSuccessAt = occurredAt,
              ActiveOutageId = null,
              ActiveOutageStartedAt = null,
              ConsecutiveFailures = 0,
              NextRetryAt = null,
              LastRecoveredOutageId = recoveredOutageId ??
                  snapshot.LastRecoveredOutageId,
              LastRecoveredOutageStartedAt =
                  recoveredOutageId is null
                      ? snapshot.LastRecoveredOutageStartedAt
                      : recoveredOutageStartedAt,
              LastRecoveredAt = recoveredOutageId is null
                  ? snapshot.LastRecoveredAt
                  : occurredAt,
              LastRecoveredFailureCategory =
                  recoveredOutageId is null
                      ? snapshot.LastRecoveredFailureCategory
                      : recoveredFailureCategory,
            };
          },
          null,
          token),
          cancellationToken);

  public Task RecordProcessStoppingAsync(
      DateTimeOffset occurredAt,
      CancellationToken cancellationToken) =>
      RecordBestEffortAsync(
          token => UpdateAsync(
          occurredAt,
          ConnectorHealthEventKinds.ProcessStopping,
          snapshot => snapshot with
          {
            State = ConnectorHealthStates.Stopping,
            UpdatedAt = occurredAt,
          },
          null,
          token),
          cancellationToken);

  private async Task UpdateAsync(
      DateTimeOffset occurredAt,
      string eventKind,
      Func<ConnectorHealthSnapshot, ConnectorHealthSnapshot> update,
      ConnectorHealthFailure? failure,
      CancellationToken cancellationToken)
  {
    var current = _snapshot;
    try
    {
      current ??= await LoadSnapshotOrCreateAsync(
          occurredAt,
          cancellationToken);
      var updated = update(current);
      var eventRecord = CreateEvent(
          eventKind,
          occurredAt,
          current,
          updated,
          failure);
      await WriteSnapshotAsync(
          updated,
          cancellationToken);
      _snapshot = updated;
      await WriteEventJournalAsync(
          eventRecord,
          cancellationToken);
    }
    catch (IOException exception)
        when (!cancellationToken.IsCancellationRequested)
    {
      LogJournalFailure(exception.Message);
    }
    catch (UnauthorizedAccessException exception)
        when (!cancellationToken.IsCancellationRequested)
    {
      LogJournalFailure(exception.Message);
    }
    catch (JsonException exception)
        when (!cancellationToken.IsCancellationRequested)
    {
      LogJournalFailure(exception.Message);
    }
  }

  internal async Task RecordBestEffortAsync(
      Func<CancellationToken, Task> update,
      CancellationToken cancellationToken,
      int priority = StateUpdatePriority)
  {
    PendingUpdate pendingUpdate;
    var startUpdateLoop = false;
    var pendingOverflowed = false;
    lock (_updateStateLock)
    {
      if (_updateInProgress)
      {
        if (priority == AttemptUpdatePriority)
        {
          return;
        }
        if (_pendingUpdates.Count == MaximumPendingUpdates)
        {
          var dropped = _pendingUpdates.Dequeue();
          dropped.Completion.TrySetResult(true);
          pendingOverflowed = true;
        }
        pendingUpdate = CreatePendingUpdate(
            update);
        _pendingUpdates.Enqueue(pendingUpdate);
      }
      else
      {
        _updateInProgress = true;
        pendingUpdate = CreatePendingUpdate(
            update);
        startUpdateLoop = true;
      }
    }
    if (pendingOverflowed)
    {
      LogPendingUpdateOverflow();
    }
    if (startUpdateLoop)
    {
      _ = Task.Run(
          () => RunUpdateLoopAsync(
              pendingUpdate,
              CancellationToken.None),
          CancellationToken.None);
    }
    try
    {
      await pendingUpdate.Completion.Task.WaitAsync(
          MaximumWriteWait,
          _timeProvider,
          cancellationToken);
    }
    catch (TimeoutException)
    {
      LogJournalTimeout();
    }
    catch (OperationCanceledException)
        when (cancellationToken.IsCancellationRequested)
    {
    }
  }

  private async Task RunUpdateLoopAsync(
      PendingUpdate initialUpdate,
      CancellationToken cancellationToken)
  {
    var completedNormally = false;
    try
    {
      var current = initialUpdate;
      while (true)
      {
        try
        {
          await ExecuteUpdateAsync(
              current.Update,
              cancellationToken);
        }
        finally
        {
          current.Completion.TrySetResult(true);
        }
        lock (_updateStateLock)
        {
          if (_pendingUpdates.Count == 0)
          {
            _updateInProgress = false;
            completedNormally = true;
            return;
          }

          current = _pendingUpdates.Dequeue();
        }
      }
    }
    finally
    {
      if (!completedNormally)
      {
        lock (_updateStateLock)
        {
          _updateInProgress = false;
          while (_pendingUpdates.TryDequeue(
              out var pendingUpdate))
          {
            pendingUpdate.Completion.TrySetResult(true);
          }
        }
      }
    }
  }

  private static PendingUpdate CreatePendingUpdate(
      Func<CancellationToken, Task> update) =>
      new(
          update,
          new TaskCompletionSource<bool>(
              TaskCreationOptions.RunContinuationsAsynchronously));

  private async Task ExecuteUpdateAsync(
      Func<CancellationToken, Task> update,
      CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(update);
    try
    {
      await update(cancellationToken);
    }
    catch (InvalidOperationException exception)
    {
      LogJournalFailure(exception.Message);
    }
    catch (NotSupportedException exception)
    {
      LogJournalFailure(exception.Message);
    }
    catch (ArgumentException exception)
    {
      LogJournalFailure(exception.Message);
    }
  }

  private async Task UpdateSnapshotOnlyAsync(
      DateTimeOffset occurredAt,
      Func<ConnectorHealthSnapshot, ConnectorHealthSnapshot> update,
      CancellationToken cancellationToken)
  {
    var current = _snapshot;
    try
    {
      current ??= await LoadSnapshotOrCreateAsync(
          occurredAt,
          cancellationToken);
      var updated = update(current);
      await WriteSnapshotAsync(
          updated,
          cancellationToken);
      _snapshot = updated;
    }
    catch (IOException exception)
        when (!cancellationToken.IsCancellationRequested)
    {
      LogJournalFailure(exception.Message);
    }
    catch (UnauthorizedAccessException exception)
        when (!cancellationToken.IsCancellationRequested)
    {
      LogJournalFailure(exception.Message);
    }
    catch (JsonException exception)
        when (!cancellationToken.IsCancellationRequested)
    {
      LogJournalFailure(exception.Message);
    }
  }

  private async Task<ConnectorHealthSnapshot> LoadSnapshotOrCreateAsync(
      DateTimeOffset occurredAt,
      CancellationToken cancellationToken)
  {
    var path = GetSnapshotPath();
    if (!File.Exists(path))
    {
      return CreateInitialSnapshot(occurredAt);
    }
    await using var stream = new FileStream(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.ReadWrite | FileShare.Delete,
        4096,
        FileOptions.Asynchronous | FileOptions.SequentialScan);
    if (stream.Length <= 0 ||
        stream.Length > MaximumSnapshotBytes)
    {
      LogInvalidSnapshot();
      return CreateInitialSnapshot(occurredAt);
    }
    ConnectorHealthSnapshot? snapshot;
    try
    {
      snapshot = await JsonSerializer.DeserializeAsync(
          stream,
          ConnectorHealthJsonContext.Default.ConnectorHealthSnapshot,
          cancellationToken);
    }
    catch (JsonException)
    {
      LogInvalidSnapshot();
      return CreateInitialSnapshot(occurredAt);
    }
    if (snapshot is null ||
        snapshot.SchemaVersion != SchemaVersion)
    {
      LogInvalidSnapshot();
      return CreateInitialSnapshot(occurredAt);
    }
    return snapshot;
  }

  private async Task WriteEventJournalAsync(
      ConnectorHealthEvent eventRecord,
      CancellationToken cancellationToken)
  {
    var retained = new Queue<ConnectorHealthEvent>(
        MaximumEvents);
    var path = GetEventJournalPath();
    if (File.Exists(path))
    {
      await using var stream = new FileStream(
          path,
          FileMode.Open,
          FileAccess.Read,
          FileShare.ReadWrite | FileShare.Delete,
          4096,
          FileOptions.Asynchronous | FileOptions.SequentialScan);
      if (stream.Length <= MaximumEventJournalBytes)
      {
        var complete = await ReadBoundedLinesAsync(
            stream,
            line => RetainEventLine(
                retained,
                line),
            cancellationToken);
        if (!complete)
        {
          retained.Clear();
          LogInvalidEventJournal();
        }
      }
      else
      {
        LogInvalidEventJournal();
      }
    }
    if (retained.Count == MaximumEvents)
    {
      retained.Dequeue();
    }
    retained.Enqueue(eventRecord);
    var lines = retained.Select(entry =>
        JsonSerializer.Serialize(
            entry,
            ConnectorHealthJsonContext.Default.ConnectorHealthEvent));
    await WriteTextAtomicallyAsync(
        path,
        string.Join('\n', lines) + "\n",
        cancellationToken);
  }

  private static void RetainEventLine(
      Queue<ConnectorHealthEvent> retained,
      string line)
  {
    ConnectorHealthEvent? parsed;
    try
    {
      parsed = JsonSerializer.Deserialize(
          line,
          ConnectorHealthJsonContext.Default.ConnectorHealthEvent);
    }
    catch (JsonException)
    {
      return;
    }
    if (parsed is null ||
        parsed.SchemaVersion != SchemaVersion)
    {
      return;
    }
    if (retained.Count == MaximumEvents - 1)
    {
      retained.Dequeue();
    }
    retained.Enqueue(parsed);
  }

  private static async Task<bool> ReadBoundedLinesAsync(
      Stream stream,
      Action<string> processLine,
      CancellationToken cancellationToken)
  {
    using var reader = new StreamReader(
        stream,
        Encoding.UTF8,
        detectEncodingFromByteOrderMarks: true,
        4096,
        leaveOpen: false);
    var buffer = new char[1024];
    var line = new StringBuilder();
    var discardLine = false;
    var totalCharacters = 0;
    while (true)
    {
      var read = await reader.ReadAsync(
          buffer.AsMemory(),
          cancellationToken);
      if (read == 0)
      {
        break;
      }
      totalCharacters += read;
      if (totalCharacters > MaximumEventJournalBytes)
      {
        return false;
      }
      for (var index = 0; index < read; index++)
      {
        var character = buffer[index];
        if (character == '\n')
        {
          if (!discardLine)
          {
            if (line.Length > 0 &&
                line[^1] == '\r')
            {
              line.Length--;
            }
            if (line.Length > 0)
            {
              processLine(line.ToString());
            }
          }
          line.Clear();
          discardLine = false;
          continue;
        }
        if (discardLine)
        {
          continue;
        }
        if (line.Length == MaximumEventLineCharacters)
        {
          line.Clear();
          discardLine = true;
          continue;
        }
        line.Append(character);
      }
    }
    if (!discardLine &&
        line.Length > 0)
    {
      if (line[^1] == '\r')
      {
        line.Length--;
      }
      if (line.Length > 0)
      {
        processLine(line.ToString());
      }
    }
    return true;
  }

  private Task WriteSnapshotAsync(
      ConnectorHealthSnapshot snapshot,
      CancellationToken cancellationToken) =>
      WriteTextAtomicallyAsync(
          GetSnapshotPath(),
          JsonSerializer.Serialize(
              snapshot,
              ConnectorHealthJsonContext.Default.ConnectorHealthSnapshot) +
              "\n",
          cancellationToken);

  private async Task WriteTextAtomicallyAsync(
      string path,
      string content,
      CancellationToken cancellationToken)
  {
    var directory = Path.GetDirectoryName(path) ??
        throw new InvalidOperationException(
            $"Connector health path '{path}' has no parent directory.");
    var directoryCreated = !Directory.Exists(directory);
    Directory.CreateDirectory(directory);
    if (!OperatingSystem.IsWindows() && directoryCreated)
    {
      File.SetUnixFileMode(
          directory,
          UnixFileMode.UserRead |
              UnixFileMode.UserWrite |
              UnixFileMode.UserExecute);
    }
    var stagingPath = Path.Combine(
        directory,
        $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
    try
    {
      var bytes = new UTF8Encoding(
          encoderShouldEmitUTF8Identifier: false).GetBytes(content);
      await using (var stream = new FileStream(
          stagingPath,
          FileMode.CreateNew,
          FileAccess.Write,
          FileShare.None,
          4096,
          FileOptions.Asynchronous | FileOptions.WriteThrough))
      {
        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        stream.Flush(flushToDisk: true);
      }
      if (!OperatingSystem.IsWindows())
      {
        File.SetUnixFileMode(
            stagingPath,
            UnixFileMode.UserRead |
                UnixFileMode.UserWrite |
                UnixFileMode.GroupRead);
      }
      File.Move(stagingPath, path, overwrite: true);
    }
    finally
    {
      if (File.Exists(stagingPath))
      {
        File.Delete(stagingPath);
      }
    }
  }

  private ConnectorHealthEvent CreateEvent(
      string eventKind,
      DateTimeOffset occurredAt,
      ConnectorHealthSnapshot previous,
      ConnectorHealthSnapshot snapshot,
      ConnectorHealthFailure? failure)
  {
    var recovered = previous.ActiveOutageId is not null &&
        string.Equals(
            eventKind,
            ConnectorHealthEventKinds.SynchronizationSucceeded,
            StringComparison.Ordinal);
    return new(
        SchemaVersion,
        Guid.NewGuid(),
        recovered
            ? ConnectorHealthEventKinds.Recovered
            : eventKind,
        occurredAt,
        snapshot.State,
        snapshot.ActiveOutageId ??
            (recovered
                ? snapshot.LastRecoveredOutageId
                : null),
        snapshot.ActiveOutageStartedAt ??
            (recovered
                ? snapshot.LastRecoveredOutageStartedAt
                : null),
        failure?.Category ??
            (recovered
                ? snapshot.LastRecoveredFailureCategory
                : snapshot.ActiveOutageId is not null
                    ? snapshot.LastFailureCategory
                    : null),
        NormalizeProfileId(failure?.ProfileId),
        snapshot.ConsecutiveFailures,
        snapshot.NextRetryAt is null
            ? null
            : Math.Max(
                0,
                (int)Math.Ceiling(
                    (snapshot.NextRetryAt.Value - occurredAt)
                        .TotalSeconds)),
        failure?.Detail);
  }

  private ConnectorHealthSnapshot CreateInitialSnapshot(
      DateTimeOffset occurredAt) =>
      new(
          SchemaVersion,
          ConnectorHealthStates.Starting,
          occurredAt,
          occurredAt,
          null,
          null,
          null,
          null,
          null,
          null,
          null,
          null,
          0,
          null,
          null,
          null,
          null,
          null);

  private string GetHealthDirectory()
  {
    var identityPath = Path.GetFullPath(
        _options.Value.IdentityPath);
    var identityDirectory = Path.GetDirectoryName(identityPath) ??
        throw new InvalidOperationException(
            $"Connector identity path '{identityPath}' has no parent directory.");
    return Path.Combine(identityDirectory, "health");
  }

  private string GetSnapshotPath() =>
      Path.Combine(
          GetHealthDirectory(),
          "connector-health.json");

  private string GetEventJournalPath() =>
      Path.Combine(
          GetHealthDirectory(),
          "connector-events.jsonl");

  private static string? NormalizeProfileId(string? profileId) =>
      profileId is not null &&
      PitCrewProfileId.IsValid(profileId)
          ? profileId
          : null;

  private static ConnectorHealthFailure SanitizeFailure(
      ConnectorHealthFailure failure) =>
      new(
          failure.Category,
          failure.Category switch
          {
            ConnectorHealthFailureCategories.StateRootMissing =>
                "PitCrew state root is unavailable.",
            ConnectorHealthFailureCategories.StateRootUnreadable =>
                "PitCrew state root could not be enumerated.",
            ConnectorHealthFailureCategories.ProfileDirectoryUnreadable =>
                "Profile state directory could not be inspected.",
            ConnectorHealthFailureCategories.ProfileStateInvalid =>
                "Profile observed state is invalid.",
            ConnectorHealthFailureCategories.ProfileStateUnreadable =>
                "Profile observed state could not be read.",
            ConnectorHealthFailureCategories.SynchronizationNetwork =>
                "Connector synchronization could not reach Dashboard.",
            ConnectorHealthFailureCategories.SynchronizationTimeout =>
                "Dashboard synchronization timed out.",
            ConnectorHealthFailureCategories.SynchronizationRateLimited =>
                "Dashboard rate-limited connector synchronization.",
            ConnectorHealthFailureCategories.SynchronizationServer =>
                "Dashboard returned a transient server error during synchronization.",
            ConnectorHealthFailureCategories.SynchronizationIo =>
                "Connector synchronization could not read or write local state.",
            ConnectorHealthFailureCategories.PayloadRejected =>
                "Dashboard permanently rejected the synchronization payload.",
            ConnectorHealthFailureCategories.CredentialRejected =>
                "Dashboard rejected the connector credential.",
            ConnectorHealthFailureCategories.EnrollmentRejected =>
                "Dashboard rejected connector enrollment.",
            ConnectorHealthFailureCategories.EnrollmentNetwork =>
                "Connector enrollment could not reach Dashboard.",
            ConnectorHealthFailureCategories.EnrollmentTimeout =>
                "Connector enrollment timed out.",
            ConnectorHealthFailureCategories.EnrollmentRateLimited =>
                "Dashboard rate-limited connector enrollment.",
            ConnectorHealthFailureCategories.EnrollmentServer =>
                "Dashboard returned a transient server error during enrollment.",
            ConnectorHealthFailureCategories.ConfigurationInvalid =>
                "Connector configuration is invalid.",
            ConnectorHealthFailureCategories.EnrollmentConfiguration =>
                "Connector enrollment configuration is incomplete.",
            _ => "Connector operation failed.",
          },
          NormalizeProfileId(failure.ProfileId));

  private sealed record PendingUpdate(
      Func<CancellationToken, Task> Update,
      TaskCompletionSource<bool> Completion);

  [LoggerMessage(
      Level = LogLevel.Warning,
      Message = "Connector health journal update failed: {Reason}")]
  private partial void LogJournalFailure(string reason);

  [LoggerMessage(
      Level = LogLevel.Warning,
      Message = "Connector health snapshot is invalid; starting a new local health projection.")]
  private partial void LogInvalidSnapshot();

  [LoggerMessage(
      Level = LogLevel.Warning,
      Message = "Connector health event journal exceeded its bounded size; starting a new local event projection.")]
  private partial void LogInvalidEventJournal();

  [LoggerMessage(
      Level = LogLevel.Warning,
      Message = "Connector health journal pending-update buffer was full; the oldest deferred update was discarded.")]
  private partial void LogPendingUpdateOverflow();

  [LoggerMessage(
      Level = LogLevel.Warning,
      Message = "Connector health journal update exceeded its one-second budget; connector work continued.")]
  private partial void LogJournalTimeout();
}
