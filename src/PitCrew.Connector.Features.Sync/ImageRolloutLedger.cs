using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PitCrew.Connector.Features.Sync;

/// <summary>
/// Persists the durable at-most-once execution ledger for profile-image rollout.
/// </summary>
internal sealed partial class ImageRolloutLedger(
    IOptions<ConnectorOptions> _options,
    ILogger<ImageRolloutLedger> _logger)
{
  private const int MaximumEntryBytes = 65_536;

  public async Task<ImageRolloutLedgerEntry?> FindAsync(
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

  public async Task<bool> RecordStartedAsync(
      ImageRolloutLedgerEntry entry,
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
          ImageRolloutLedgerJsonContext.Default.ImageRolloutLedgerEntry,
          cancellationToken);
      await stream.FlushAsync(cancellationToken);
      stream.Flush(flushToDisk: true);
    }
    return true;
  }

  public async Task RecordTerminalAsync(
      ImageRolloutLedgerEntry entry,
      CancellationToken cancellationToken)
  {
    EnsureLedgerDirectory();
    var path = GetEntryPath(entry.CommandId);
    // Use a unique staging filename per write so concurrent terminal writers
    // for the same command id can never collide on the same temporary file.
    // Any leftover staging file from a crashed writer is still swept by the
    // *.json filter used by ReadUnresolvedAsync and Enumerate... methods.
    var stagingPath =
        path + "." + Guid.NewGuid().ToString("N") + ".staging";
    var stream = new FileStream(
        stagingPath,
        FileMode.CreateNew,
        FileAccess.Write,
        FileShare.None,
        4096,
        FileOptions.Asynchronous | FileOptions.WriteThrough);
    try
    {
      await using (stream)
      {
        await JsonSerializer.SerializeAsync(
            stream,
            entry,
            ImageRolloutLedgerJsonContext.Default.ImageRolloutLedgerEntry,
            cancellationToken);
        await stream.FlushAsync(cancellationToken);
        stream.Flush(flushToDisk: true);
      }
      File.Move(stagingPath, path, overwrite: true);
    }
    catch
    {
      // Best-effort staging cleanup on failure; File.Move ownership means the
      // staging file no longer exists after a successful move.
      try
      {
        if (File.Exists(stagingPath))
        {
          File.Delete(stagingPath);
        }
      }
      catch (IOException)
      {
      }
      catch (UnauthorizedAccessException)
      {
      }
      throw;
    }
  }

  public async Task<IReadOnlyList<ImageRolloutLedgerEntry>> ReadUnresolvedAsync(
      CancellationToken cancellationToken)
  {
    var directory = GetLedgerDirectory();
    if (!Directory.Exists(directory))
    {
      return [];
    }

    var unresolved = new List<ImageRolloutLedgerEntry>();
    foreach (var path in Directory
        .GetFiles(directory, "*.json")
        .Order(StringComparer.Ordinal))
    {
      var entry = await ReadEntryAsync(path, cancellationToken);
      if (entry is not null &&
          string.Equals(
              entry.Phase,
              ImageRolloutLedgerPhases.Started,
              StringComparison.Ordinal))
      {
        unresolved.Add(entry);
      }
    }
    return unresolved
        .OrderBy(entry => entry.StartedAt)
        .ToArray();
  }

  /// <summary>
  /// Returns the set of connector-generated manifest paths that must be kept.
  /// Includes: every started entry (its rollout is still in flight), a bounded
  /// number of the most recent terminal (succeeded/indeterminate) entries, and
  /// every explicit protected path supplied by the caller (typically the
  /// currently applied static-profile <c>manifest.sourcePath</c> for each
  /// resolved profile, so a live manifest is never pruned).
  /// </summary>
  /// <remarks>
  /// This method bounds manifest storage only. Ledger entry files are never
  /// deleted: their command IDs are the durable at-most-once tombstones that
  /// prevent a previously-executed command from running again if it is
  /// redelivered after retention pruning. Callers must therefore treat this
  /// method purely as an orphan-manifest filter, not as a ledger garbage
  /// collector.
  /// </remarks>
  /// <param name="extraProtectedPaths">
  /// Additional paths — for example the currently applied static-profile
  /// manifest source path — that must not be pruned even when they are not
  /// referenced by the ledger.
  /// </param>
  /// <param name="terminalRetentionCap">
  /// The maximum number of terminal (succeeded/indeterminate) entries whose
  /// manifest paths are retained; older terminal manifest paths beyond this
  /// cap are eligible for pruning. Older terminal ledger entries themselves
  /// remain on disk to preserve at-most-once command-id semantics.
  /// </param>
  public IReadOnlySet<string> EnumerateReferencedManifestPaths(
      IReadOnlyCollection<string>? extraProtectedPaths = null,
      int terminalRetentionCap = 8)
  {
    var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    if (extraProtectedPaths is not null)
    {
      foreach (var path in extraProtectedPaths)
      {
        if (!string.IsNullOrWhiteSpace(path))
        {
          referenced.Add(path);
        }
      }
    }
    var directory = GetLedgerDirectory();
    if (!Directory.Exists(directory))
    {
      return referenced;
    }
    var terminalEntries =
        new List<(DateTimeOffset Timestamp, string LocalManifestPath, string EntryPath)>();
    foreach (var path in Directory.GetFiles(directory, "*.json"))
    {
      ImageRolloutLedgerEntry? entry;
      try
      {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length == 0 || bytes.Length > MaximumEntryBytes)
        {
          continue;
        }
        entry = JsonSerializer.Deserialize(
            bytes,
            ImageRolloutLedgerJsonContext.Default.ImageRolloutLedgerEntry);
      }
      catch (IOException)
      {
        continue;
      }
      catch (UnauthorizedAccessException)
      {
        continue;
      }
      catch (JsonException)
      {
        continue;
      }
      if (entry is null)
      {
        continue;
      }
      if (string.Equals(
              entry.Phase,
              ImageRolloutLedgerPhases.Started,
              StringComparison.Ordinal))
      {
        // Never prune the manifest for a started (still-active) attempt.
        referenced.Add(entry.LocalManifestPath);
        continue;
      }
      if (string.Equals(entry.Status, "succeeded", StringComparison.Ordinal) ||
          string.Equals(entry.Status, "indeterminate", StringComparison.Ordinal))
      {
        terminalEntries.Add((
            entry.CompletedAt ?? entry.StartedAt,
            entry.LocalManifestPath,
            path));
      }
    }
    // Retain the bounded most-recent terminal entries. Older ones become
    // eligible for the PruneOrphans sweep.
    foreach (var entry in terminalEntries
        .OrderByDescending(entry => entry.Timestamp)
        .Take(terminalRetentionCap))
    {
      referenced.Add(entry.LocalManifestPath);
    }
    return referenced;
  }

  /// <summary>
  /// Terminal ledger entries are intentionally never pruned: they are the
  /// durable at-most-once tombstones that keep a previously-executed command
  /// id from running again if it is redelivered after retention pruning. Only
  /// generated manifest files are bounded; that is handled by
  /// <see cref="EnumerateReferencedManifestPaths"/> plus the manifest
  /// builder's orphan sweep.
  /// </summary>
  private async Task<ImageRolloutLedgerEntry?> ReadEntryAsync(
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
    catch (InvalidDataException)
    {
      LogUnreadableEntry();
      return null;
    }
    catch (FileNotFoundException)
    {
      LogUnreadableEntry();
      return null;
    }

    try
    {
      return JsonSerializer.Deserialize(
          bytes,
          ImageRolloutLedgerJsonContext.Default.ImageRolloutLedgerEntry);
    }
    catch (JsonException)
    {
      LogUnreadableEntry();
      return null;
    }
  }

  private void EnsureLedgerDirectory()
  {
    var stateRoot = ImageRolloutStatePathGuard.CanonicalizeStateRoot(
        _options.Value.ImageRolloutStatePath);
    if (!Directory.Exists(stateRoot))
    {
      // Fail closed rather than silently creating an insecure ancestor chain:
      // the installer is responsible for provisioning ImageRolloutStatePath
      // with restrictive ownership/permissions before the connector runs.
      throw new UnauthorizedAccessException(
          $"Image rollout state root '{stateRoot}' does not exist. " +
          "Reinstall the connector with -EnableImageRollout so the installer " +
          "can provision the protected rollout state directory.");
    }
    // Refuse to follow a state root that is itself a symlink/junction: the
    // installer must materialize a real protected directory.
    ImageRolloutStatePathGuard.EnsureNotReparsePoint(stateRoot);
    var directory = ImageRolloutStatePathGuard.CombineConfinedChild(
        stateRoot,
        LedgerSubdirectory);
    if (Directory.Exists(directory))
    {
      // Refuse an existing ledger child that is a symlink/junction pointing
      // elsewhere; only real directories are trusted.
      ImageRolloutStatePathGuard.EnsureNotReparsePoint(directory);
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

  private string GetLedgerDirectory()
  {
    var stateRoot = ImageRolloutStatePathGuard.CanonicalizeStateRoot(
        _options.Value.ImageRolloutStatePath);
    return ImageRolloutStatePathGuard.CombineConfinedChild(
        stateRoot,
        LedgerSubdirectory);
  }

  private string GetEntryPath(Guid commandId) =>
      Path.Combine(
          GetLedgerDirectory(),
          $"{commandId:N}.json");

  private const string LedgerSubdirectory = "ledger";

  [LoggerMessage(
      Level = LogLevel.Warning,
      Message = "An image rollout ledger entry could not be read.")]
  private partial void LogUnreadableEntry();
}
