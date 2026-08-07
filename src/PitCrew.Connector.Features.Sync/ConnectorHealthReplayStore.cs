using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NexusLabs.Needlr;

using PitCrew.Protocol;

namespace PitCrew.Connector.Features.Sync;

[DoNotAutoRegister]
internal sealed partial class ConnectorHealthReplayStore(
    IOptions<ConnectorOptions> _options,
    TimeProvider _timeProvider,
    ILogger<ConnectorHealthReplayStore> _logger)
{
  private const int SchemaVersion = 1;
  private const int MaximumEvents = 256;
  private const int MaximumEventLineCharacters = 4096;
  private const int MaximumEventJournalBytes =
      MaximumEvents * (MaximumEventLineCharacters + 1);
  private const int MaximumSnapshotBytes = 65_536;
  private const int MaximumAcknowledgementBytes = 65_536;
  private static readonly TimeSpan MaximumWriteWait =
      TimeSpan.FromSeconds(1);
  private int _acknowledgementWriteInProgress;

  public async Task<ConnectorHealthReplayReadResult> ReadPendingAsync(
      CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();
    try
    {
      var snapshot = await ReadSnapshotAsync(cancellationToken);
      if (snapshot is null)
      {
        return new ConnectorHealthReplayReadResult(
            null,
            false,
            false);
      }
      var events = await ReadEventsAsync(cancellationToken);
      var acknowledgement =
          await ReadAcknowledgementAsync(cancellationToken);
      var acknowledgedIds = acknowledgement?.EventIds.ToHashSet() ?? [];
      var replaySnapshot = ToReplaySnapshot(snapshot);
      var maximumTimestamp = _timeProvider.GetUtcNow().AddMinutes(5);
      if (!ConnectorHealthReplayContract.IsValid(
          new ConnectorHealthReplay(
              replaySnapshot,
              []),
          maximumTimestamp))
      {
        LogInvalidReplay();
        return new ConnectorHealthReplayReadResult(
            null,
            false,
            false);
      }
      var candidateEvents = events
          .Where(entry => !acknowledgedIds.Contains(entry.EventId))
          .Select(ToReplayEvent)
          .ToArray();
      var pendingEvents = candidateEvents
          .Where(entry =>
              ConnectorHealthReplayContract.IsValid(
                  new ConnectorHealthReplay(
                      replaySnapshot,
                      [entry]),
                  maximumTimestamp))
          .DistinctBy(entry => entry.EventId)
          .ToArray();
      if (pendingEvents.Length != candidateEvents.Length)
      {
        LogInvalidReplayEvents(
            candidateEvents.Length - pendingEvents.Length);
      }
      var replay = new ConnectorHealthReplay(
          replaySnapshot,
          pendingEvents);
      return new ConnectorHealthReplayReadResult(
          replay,
          pendingEvents.Length > 0,
          pendingEvents.Any(entry =>
              entry.Kind is
                  "synchronization-failed" or
                  "observation-incomplete" or
                  "enrollment-failed" or
                  "rejected"));
    }
    catch (IOException exception)
        when (!cancellationToken.IsCancellationRequested)
    {
      LogReplayFailure(exception.Message);
      return new ConnectorHealthReplayReadResult(
          null,
          false,
          false);
    }
    catch (UnauthorizedAccessException exception)
        when (!cancellationToken.IsCancellationRequested)
    {
      LogReplayFailure(exception.Message);
      return new ConnectorHealthReplayReadResult(
          null,
          false,
          false);
    }
    catch (JsonException exception)
        when (!cancellationToken.IsCancellationRequested)
    {
      LogReplayFailure(exception.Message);
      return new ConnectorHealthReplayReadResult(
          null,
          false,
          false);
    }
    catch (InvalidOperationException exception)
    {
      LogReplayFailure(exception.Message);
      return new ConnectorHealthReplayReadResult(
          null,
          false,
          false);
    }
    catch (NotSupportedException exception)
    {
      LogReplayFailure(exception.Message);
      return new ConnectorHealthReplayReadResult(
          null,
          false,
          false);
    }
    catch (ArgumentException exception)
    {
      LogReplayFailure(exception.Message);
      return new ConnectorHealthReplayReadResult(
          null,
          false,
          false);
    }
  }

  public async Task AcknowledgeAsync(
      IReadOnlyList<Guid> eventIds,
      CancellationToken cancellationToken)
  {
    if (eventIds.Count == 0)
    {
      return;
    }
    if (Interlocked.CompareExchange(
        ref _acknowledgementWriteInProgress,
        1,
        0) != 0)
    {
      return;
    }
    var acceptedEventIds = eventIds.ToArray();
    var updateTask = Task.Run(
        async () =>
        {
          try
          {
            await PersistAcknowledgementAsync(
                acceptedEventIds,
                CancellationToken.None);
          }
          finally
          {
            Volatile.Write(
                ref _acknowledgementWriteInProgress,
                0);
          }
        },
        CancellationToken.None);
    try
    {
      await updateTask.WaitAsync(
          MaximumWriteWait,
          _timeProvider,
          cancellationToken);
    }
    catch (TimeoutException)
    {
      LogAcknowledgementTimeout();
    }
    catch (OperationCanceledException)
        when (cancellationToken.IsCancellationRequested)
    {
    }
  }

  private async Task PersistAcknowledgementAsync(
      IReadOnlyList<Guid> eventIds,
      CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(eventIds);
    try
    {
      var events = await ReadEventsAsync(cancellationToken);
      var retainedEventIds = events
          .Select(entry => entry.EventId)
          .ToHashSet();
      var acknowledged = await ReadAcknowledgementAsync(
          cancellationToken);
      var acceptedIds = acknowledged?.EventIds.ToHashSet() ?? [];
      acceptedIds.UnionWith(eventIds);
      acceptedIds.IntersectWith(retainedEventIds);
      var state = new ConnectorHealthAcknowledgementState(
          SchemaVersion,
          _timeProvider.GetUtcNow(),
          acceptedIds
              .Order()
              .Take(MaximumEvents)
              .ToArray());
      await WriteTextAtomicallyAsync(
          GetAcknowledgementPath(),
          JsonSerializer.Serialize(
              state,
              ConnectorHealthJsonContext.Default
                  .ConnectorHealthAcknowledgementState) +
              "\n",
          cancellationToken);
    }
    catch (IOException exception)
        when (!cancellationToken.IsCancellationRequested)
    {
      LogAcknowledgementFailure(exception.Message);
    }
    catch (UnauthorizedAccessException exception)
        when (!cancellationToken.IsCancellationRequested)
    {
      LogAcknowledgementFailure(exception.Message);
    }
    catch (JsonException exception)
        when (!cancellationToken.IsCancellationRequested)
    {
      LogAcknowledgementFailure(exception.Message);
    }
    catch (InvalidOperationException exception)
    {
      LogAcknowledgementFailure(exception.Message);
    }
    catch (NotSupportedException exception)
    {
      LogAcknowledgementFailure(exception.Message);
    }
    catch (ArgumentException exception)
    {
      LogAcknowledgementFailure(exception.Message);
    }
  }

  private async Task<ConnectorHealthSnapshot?> ReadSnapshotAsync(
      CancellationToken cancellationToken)
  {
    var path = GetSnapshotPath();
    if (!File.Exists(path))
    {
      return null;
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
      return null;
    }
    var snapshot = await JsonSerializer.DeserializeAsync(
        stream,
        ConnectorHealthJsonContext.Default.ConnectorHealthSnapshot,
        cancellationToken);
    return snapshot?.SchemaVersion == SchemaVersion
        ? snapshot
        : null;
  }

  private async Task<IReadOnlyList<ConnectorHealthEvent>> ReadEventsAsync(
      CancellationToken cancellationToken)
  {
    var path = GetEventJournalPath();
    if (!File.Exists(path))
    {
      return [];
    }
    await using var stream = new FileStream(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.ReadWrite | FileShare.Delete,
        4096,
        FileOptions.Asynchronous | FileOptions.SequentialScan);
    if (stream.Length <= 0 ||
        stream.Length > MaximumEventJournalBytes)
    {
      return [];
    }
    var retained = new Queue<ConnectorHealthEvent>(
        MaximumEvents);
    var complete = await ReadBoundedLinesAsync(
        stream,
        line => RetainEventLine(
            retained,
            line),
        cancellationToken);
    return complete
        ? retained.ToArray()
        : [];
  }

  private async Task<ConnectorHealthAcknowledgementState?>
      ReadAcknowledgementAsync(
          CancellationToken cancellationToken)
  {
    var path = GetAcknowledgementPath();
    if (!File.Exists(path))
    {
      return null;
    }
    await using var stream = new FileStream(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.ReadWrite | FileShare.Delete,
        4096,
        FileOptions.Asynchronous | FileOptions.SequentialScan);
    if (stream.Length <= 0 ||
        stream.Length > MaximumAcknowledgementBytes)
    {
      return null;
    }
    ConnectorHealthAcknowledgementState? acknowledgement;
    try
    {
      acknowledgement = await JsonSerializer.DeserializeAsync(
          stream,
          ConnectorHealthJsonContext.Default
              .ConnectorHealthAcknowledgementState,
          cancellationToken);
    }
    catch (JsonException)
    {
      return null;
    }
    return acknowledgement is
        {
          SchemaVersion: SchemaVersion,
          EventIds.Count: <= MaximumEvents,
        }
        ? acknowledgement
        : null;
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
    if (retained.Count == MaximumEvents)
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

  private async Task WriteTextAtomicallyAsync(
      string path,
      string content,
      CancellationToken cancellationToken)
  {
    var directory = Path.GetDirectoryName(path) ??
        throw new InvalidOperationException(
            $"Connector health path '{path}' has no parent directory.");
    Directory.CreateDirectory(directory);
    if (!OperatingSystem.IsWindows())
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
            UnixFileMode.UserRead | UnixFileMode.UserWrite);
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

  private static ConnectorHealthReplaySnapshot ToReplaySnapshot(
      ConnectorHealthSnapshot snapshot) =>
      new(
          snapshot.State,
          snapshot.ProcessStartedAt,
          snapshot.UpdatedAt,
          snapshot.LastAttemptAt,
          snapshot.LastSuccessAt,
          snapshot.ActiveOutageId,
          snapshot.ActiveOutageStartedAt,
          snapshot.LastFailureAt,
          snapshot.LastFailureCategory,
          snapshot.LastFailureProfileId,
          snapshot.LastFailureDetail,
          snapshot.ConsecutiveFailures,
          snapshot.NextRetryAt,
          snapshot.LastRecoveredOutageId,
          snapshot.LastRecoveredOutageStartedAt,
          snapshot.LastRecoveredAt,
          snapshot.LastRecoveredFailureCategory);

  private static ConnectorHealthReplayEvent ToReplayEvent(
      ConnectorHealthEvent entry) =>
      new(
          entry.EventId,
          entry.Kind,
          entry.OccurredAt,
          entry.State,
          entry.OutageId,
          entry.OutageStartedAt,
          entry.FailureCategory,
          entry.ProfileId,
          entry.ConsecutiveFailures,
          entry.RetryDelaySeconds,
          entry.Detail);

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

  private string GetAcknowledgementPath() =>
      Path.Combine(
          GetHealthDirectory(),
          "connector-health-acknowledgement.json");

  [LoggerMessage(
      Level = LogLevel.Warning,
      Message = "Connector health replay could not be prepared: {Reason}")]
  private partial void LogReplayFailure(string reason);

  [LoggerMessage(
      Level = LogLevel.Warning,
      Message = "Connector health replay acknowledgement could not be persisted: {Reason}")]
  private partial void LogAcknowledgementFailure(string reason);

  [LoggerMessage(
      Level = LogLevel.Warning,
      Message = "Connector health replay acknowledgement exceeded its one-second budget; events will be redelivered.")]
  private partial void LogAcknowledgementTimeout();

  [LoggerMessage(
      Level = LogLevel.Warning,
      Message = "Connector health replay was omitted because the local journal does not satisfy the protocol contract.")]
  private partial void LogInvalidReplay();

  [LoggerMessage(
      Level = LogLevel.Warning,
      Message = "Connector health replay omitted {InvalidEventCount} invalid or duplicate local event(s).")]
  private partial void LogInvalidReplayEvents(int invalidEventCount);
}
