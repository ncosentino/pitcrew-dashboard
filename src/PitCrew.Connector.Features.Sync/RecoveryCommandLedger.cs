using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NexusLabs.Needlr;

namespace PitCrew.Connector.Features.Sync;

/// <summary>
/// Persists the durable at-most-once execution ledger for manager recovery.
/// </summary>
[DoNotAutoRegister]
internal sealed partial class RecoveryCommandLedger(
    IOptions<ConnectorOptions> _options,
    ILogger<RecoveryCommandLedger> _logger)
{
  private const int MaximumEntryBytes = 65_536;

  /// <summary>
  /// Reads one previously recorded attempt.
  /// </summary>
  /// <param name="commandId">Command identifier supplied by the dashboard.</param>
  /// <param name="cancellationToken">Token that cancels the read.</param>
  /// <returns>The recorded attempt, or <see langword="null"/> when the command is new.</returns>
  public async Task<RecoveryLedgerEntry?> FindAsync(
      Guid commandId,
      CancellationToken cancellationToken)
  {
    var path = GetEntryPath(commandId);
    if (!File.Exists(path))
    {
      return null;
    }
    return await ReadEntryAsync(path, cancellationToken);
  }

  /// <summary>
  /// Durably records the intent to execute one command exactly once.
  /// </summary>
  /// <param name="entry">Attempt including fences and locally resolved identity.</param>
  /// <param name="cancellationToken">Token that cancels the write.</param>
  /// <returns><see langword="true"/> when this process owns the attempt.</returns>
  public async Task<bool> RecordStartedAsync(
      RecoveryLedgerEntry entry,
      CancellationToken cancellationToken)
  {
    EnsureLedgerDirectory();
    var path = GetEntryPath(entry.CommandId);
    FileStream stream;
    try
    {
      stream = new FileStream(
          path,
          FileMode.CreateNew,
          FileAccess.Write,
          FileShare.None,
          4096,
          FileOptions.Asynchronous | FileOptions.WriteThrough);
    }
    catch (IOException) when (File.Exists(path))
    {
      return false;
    }

    await using (stream)
    {
      await JsonSerializer.SerializeAsync(
          stream,
          entry,
          RecoveryLedgerJsonContext.Default.RecoveryLedgerEntry,
          cancellationToken);
      await stream.FlushAsync(cancellationToken);
      stream.Flush(flushToDisk: true);
    }
    return true;
  }

  /// <summary>
  /// Durably records the terminal result of one attempt.
  /// </summary>
  /// <param name="entry">Attempt with its resolved terminal state.</param>
  /// <param name="cancellationToken">Token that cancels the write.</param>
  /// <returns>A task that completes once the result is durable.</returns>
  public async Task RecordTerminalAsync(
      RecoveryLedgerEntry entry,
      CancellationToken cancellationToken)
  {
    EnsureLedgerDirectory();
    var path = GetEntryPath(entry.CommandId);
    var stagingPath = path + ".staging";
    var stream = new FileStream(
        stagingPath,
        FileMode.Create,
        FileAccess.Write,
        FileShare.None,
        4096,
        FileOptions.Asynchronous | FileOptions.WriteThrough);
    await using (stream)
    {
      await JsonSerializer.SerializeAsync(
          stream,
          entry,
          RecoveryLedgerJsonContext.Default.RecoveryLedgerEntry,
          cancellationToken);
      await stream.FlushAsync(cancellationToken);
      stream.Flush(flushToDisk: true);
    }
    File.Move(stagingPath, path, overwrite: true);
  }

  /// <summary>
  /// Reads every attempt that started but never reached a terminal state.
  /// </summary>
  /// <param name="cancellationToken">Token that cancels the read.</param>
  /// <returns>Unresolved attempts ordered by their durable start time.</returns>
  public async Task<IReadOnlyList<RecoveryLedgerEntry>> ReadUnresolvedAsync(
      CancellationToken cancellationToken)
  {
    var directory = GetLedgerDirectory();
    if (!Directory.Exists(directory))
    {
      return [];
    }

    var unresolved = new List<RecoveryLedgerEntry>();
    foreach (var path in Directory
        .GetFiles(directory, "*.json")
        .Order(StringComparer.Ordinal))
    {
      var entry = await ReadEntryAsync(path, cancellationToken);
      if (entry is not null &&
          string.Equals(
              entry.Phase,
              RecoveryLedgerPhases.Started,
              StringComparison.Ordinal))
      {
        unresolved.Add(entry);
      }
    }
    return unresolved
        .OrderBy(entry => entry.StartedAt)
        .ToArray();
  }

  private async Task<RecoveryLedgerEntry?> ReadEntryAsync(
      string path,
      CancellationToken cancellationToken)
  {
    byte[] bytes;
    try
    {
      bytes = await LocalProfileStateLocator.ReadBoundedAsync(
          path,
          MaximumEntryBytes,
          cancellationToken);
    }
    catch (InvalidDataException exception)
    {
      LogUnreadableEntry(path, exception.Message);
      return null;
    }
    catch (FileNotFoundException exception)
    {
      LogUnreadableEntry(path, exception.Message);
      return null;
    }

    try
    {
      return JsonSerializer.Deserialize(
          bytes,
          RecoveryLedgerJsonContext.Default.RecoveryLedgerEntry);
    }
    catch (JsonException exception)
    {
      LogUnreadableEntry(path, exception.Message);
      return null;
    }
  }

  private void EnsureLedgerDirectory()
  {
    var directory = GetLedgerDirectory();
    if (Directory.Exists(directory))
    {
      return;
    }
    if (OperatingSystem.IsWindows())
    {
      Directory.CreateDirectory(directory);
      return;
    }
    Directory.CreateDirectory(
        directory,
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
  }

  private string GetLedgerDirectory() =>
      Path.GetFullPath(_options.Value.RecoveryLedgerPath);

  private string GetEntryPath(Guid commandId) =>
      Path.Combine(
          GetLedgerDirectory(),
          $"{commandId:N}.json");

  [LoggerMessage(
      Level = LogLevel.Warning,
      Message = "Recovery ledger entry {EntryPath} could not be read: {Reason}")]
  private partial void LogUnreadableEntry(
      string entryPath,
      string reason);
}
