using System.Globalization;

using Microsoft.Data.Sqlite;

namespace PitCrew.Dashboard.Adapters.Sqlite;

/// <summary>
/// Performs operator-initiated SQLite backup, verification, and stopped-app restore operations.
/// </summary>
public sealed class SqliteDatabaseMaintenance
{
  /// <summary>
  /// Creates and verifies an online SQLite backup.
  /// </summary>
  /// <param name="databasePath">Live source database path.</param>
  /// <param name="outputPath">Backup file path.</param>
  /// <param name="cancellationToken">Token checked before blocking SQLite operations.</param>
  /// <returns>The operation result.</returns>
  public SqliteMaintenanceResult Backup(
      string databasePath,
      string outputPath,
      CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();
    var sourcePath = Path.GetFullPath(databasePath);
    var destinationPath = Path.GetFullPath(outputPath);
    if (string.Equals(
        sourcePath,
        destinationPath,
        StringComparison.OrdinalIgnoreCase))
    {
      return Failure(
          "Backup output must differ from the live database path.");
    }
    if (!File.Exists(sourcePath))
    {
      return Failure($"SQLite database '{sourcePath}' does not exist.");
    }

    var destinationDirectory = Path.GetDirectoryName(destinationPath);
    if (string.IsNullOrWhiteSpace(destinationDirectory))
    {
      return Failure(
          $"Backup path '{destinationPath}' does not have a parent directory.");
    }
    Directory.CreateDirectory(destinationDirectory);
    var temporaryPath = CreateTemporaryPath(destinationPath);
    try
    {
      CopyDatabase(sourcePath, temporaryPath);
      var verification = VerifyCore(
          temporaryPath,
          fullIntegrityCheck: false);
      if (!verification.Succeeded)
      {
        return verification;
      }
      File.Move(
          temporaryPath,
          destinationPath,
          true);
      return Success(
          $"SQLite backup created at '{destinationPath}'.");
    }
    catch (SqliteException exception)
    {
      return Failure(
          $"SQLite backup failed: {exception.Message}");
    }
    catch (IOException exception)
    {
      return Failure(
          $"SQLite backup file operation failed: {exception.Message}");
    }
    catch (UnauthorizedAccessException exception)
    {
      return Failure(
          $"SQLite backup path was not accessible: {exception.Message}");
    }
    finally
    {
      DeleteIfExists(temporaryPath);
      DeleteSidecars(temporaryPath);
    }
  }

  /// <summary>
  /// Verifies SQLite integrity and foreign-key consistency.
  /// </summary>
  /// <param name="databasePath">Database or backup file to verify.</param>
  /// <param name="cancellationToken">Token checked before blocking SQLite operations.</param>
  /// <returns>The operation result.</returns>
  public SqliteMaintenanceResult Verify(
      string databasePath,
      CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();
    var path = Path.GetFullPath(databasePath);
    if (!File.Exists(path))
    {
      return Failure($"SQLite database '{path}' does not exist.");
    }
    try
    {
      return VerifyCore(
          path,
          fullIntegrityCheck: true);
    }
    catch (SqliteException exception)
    {
      return Failure(
          $"SQLite verification failed: {exception.Message}");
    }
    catch (IOException exception)
    {
      return Failure(
          $"SQLite verification file operation failed: {exception.Message}");
    }
    catch (UnauthorizedAccessException exception)
    {
      return Failure(
          $"SQLite verification path was not accessible: {exception.Message}");
    }
  }

  /// <summary>
  /// Restores a verified backup through a same-directory atomic replacement.
  /// </summary>
  /// <param name="backupPath">Verified source backup path.</param>
  /// <param name="databasePath">Stopped dashboard database path to replace.</param>
  /// <param name="cancellationToken">Token checked before blocking SQLite operations.</param>
  /// <returns>The operation result and rollback path when a live database was replaced.</returns>
  public SqliteMaintenanceResult Restore(
      string backupPath,
      string databasePath,
      CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();
    var sourcePath = Path.GetFullPath(backupPath);
    var destinationPath = Path.GetFullPath(databasePath);
    if (string.Equals(
        sourcePath,
        destinationPath,
        StringComparison.OrdinalIgnoreCase))
    {
      return Failure(
          "Restore input must differ from the live database path.");
    }
    if (!File.Exists(sourcePath))
    {
      return Failure($"SQLite backup '{sourcePath}' does not exist.");
    }

    var sourceVerification = Verify(
        sourcePath,
        cancellationToken);
    if (!sourceVerification.Succeeded)
    {
      return sourceVerification;
    }

    var destinationDirectory = Path.GetDirectoryName(destinationPath);
    if (string.IsNullOrWhiteSpace(destinationDirectory))
    {
      return Failure(
          $"Restore path '{destinationPath}' does not have a parent directory.");
    }
    Directory.CreateDirectory(destinationDirectory);
    var temporaryPath = CreateTemporaryPath(destinationPath);
    var rollbackPath =
        $"{destinationPath}.pre-restore-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.bak";
    try
    {
      CopyDatabase(sourcePath, temporaryPath);
      var stagedVerification = VerifyCore(
          temporaryPath,
          fullIntegrityCheck: true);
      if (!stagedVerification.Succeeded)
      {
        return stagedVerification;
      }

      SqliteConnection.ClearAllPools();
      if (File.Exists(destinationPath))
      {
        MoveSidecars(
            destinationPath,
            rollbackPath);
        try
        {
          File.Replace(
              temporaryPath,
              destinationPath,
              rollbackPath,
              true);
        }
        catch (IOException)
        {
          MoveSidecars(
              rollbackPath,
              destinationPath);
          throw;
        }
        catch (UnauthorizedAccessException)
        {
          MoveSidecars(
              rollbackPath,
              destinationPath);
          throw;
        }
      }
      else
      {
        File.Move(temporaryPath, destinationPath);
        rollbackPath = string.Empty;
      }
      DeleteSidecars(destinationPath);
      return Success(
          $"SQLite database restored to '{destinationPath}'.",
          rollbackPath);
    }
    catch (SqliteException exception)
    {
      return Failure(
          $"SQLite restore failed: {exception.Message}");
    }
    catch (IOException exception)
    {
      return Failure(
          $"SQLite restore file operation failed: {exception.Message}");
    }
    catch (UnauthorizedAccessException exception)
    {
      return Failure(
          $"SQLite restore path was not accessible: {exception.Message}");
    }
    finally
    {
      DeleteIfExists(temporaryPath);
      DeleteSidecars(temporaryPath);
    }
  }

  private static void CopyDatabase(
      string sourcePath,
      string destinationPath)
  {
    using var source = OpenConnection(
        sourcePath,
        SqliteOpenMode.ReadOnly);
    using var destination = OpenConnection(
        destinationPath,
        SqliteOpenMode.ReadWriteCreate);
    source.BackupDatabase(destination);
  }

  private static SqliteMaintenanceResult VerifyCore(
      string path,
      bool fullIntegrityCheck)
  {
    using var connection = OpenConnection(
        path,
        SqliteOpenMode.ReadOnly);
    using var integrityCommand = connection.CreateCommand();
    integrityCommand.CommandText = fullIntegrityCheck
        ? "PRAGMA integrity_check;"
        : "PRAGMA quick_check;";
    var integrity = Convert.ToString(
        integrityCommand.ExecuteScalar(),
        CultureInfo.InvariantCulture);
    if (!string.Equals(
        integrity,
        "ok",
        StringComparison.OrdinalIgnoreCase))
    {
      return Failure(
          $"SQLite integrity check failed for '{path}': {integrity}");
    }

    using var foreignKeyCommand = connection.CreateCommand();
    foreignKeyCommand.CommandText = "PRAGMA foreign_key_check;";
    using var reader = foreignKeyCommand.ExecuteReader();
    if (reader.Read())
    {
      return Failure(
          $"SQLite foreign-key check failed for '{path}'.");
    }
    return Success($"SQLite database '{path}' passed verification.");
  }

  private static SqliteConnection OpenConnection(
      string path,
      SqliteOpenMode mode)
  {
    var connection = new SqliteConnection(
        new SqliteConnectionStringBuilder
        {
          DataSource = path,
          Mode = mode,
          Pooling = false,
        }.ToString());
    connection.Open();
    return connection;
  }

  private static string CreateTemporaryPath(string targetPath)
  {
    var directory = Path.GetDirectoryName(targetPath) ??
        throw new InvalidOperationException(
            $"Target path '{targetPath}' does not have a parent directory.");
    return Path.Combine(
        directory,
        $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
  }

  private static void DeleteSidecars(string databasePath)
  {
    DeleteIfExists($"{databasePath}-wal");
    DeleteIfExists($"{databasePath}-shm");
  }

  private static void MoveSidecars(
      string sourceDatabasePath,
      string destinationDatabasePath)
  {
    var sourceWal = $"{sourceDatabasePath}-wal";
    var destinationWal = $"{destinationDatabasePath}-wal";
    var sourceSharedMemory = $"{sourceDatabasePath}-shm";
    var destinationSharedMemory =
        $"{destinationDatabasePath}-shm";
    var walMoved = MoveIfExists(
        sourceWal,
        destinationWal);
    try
    {
      MoveIfExists(
          sourceSharedMemory,
          destinationSharedMemory);
    }
    catch (IOException)
    {
      if (walMoved)
      {
        MoveIfExists(
            destinationWal,
            sourceWal);
      }
      throw;
    }
    catch (UnauthorizedAccessException)
    {
      if (walMoved)
      {
        MoveIfExists(
            destinationWal,
            sourceWal);
      }
      throw;
    }
  }

  private static bool MoveIfExists(
      string sourcePath,
      string destinationPath)
  {
    if (!File.Exists(sourcePath))
    {
      return false;
    }
    File.Move(
        sourcePath,
        destinationPath,
        true);
    return true;
  }

  private static void DeleteIfExists(string path)
  {
    if (File.Exists(path))
    {
      File.Delete(path);
    }
  }

  private static SqliteMaintenanceResult Success(string message) =>
      new(true, message, null);

  private static SqliteMaintenanceResult Success(
      string message,
      string rollbackPath) =>
      new(
          true,
          message,
          string.IsNullOrWhiteSpace(rollbackPath)
              ? null
              : rollbackPath);

  private static SqliteMaintenanceResult Failure(string message) =>
      new(false, message, null);
}
