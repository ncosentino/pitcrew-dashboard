namespace PitCrew.Dashboard.Adapters.Sqlite;

/// <summary>
/// Reports the outcome of one SQLite backup, verification, or restore operation.
/// </summary>
/// <param name="Succeeded">Whether the operation completed successfully.</param>
/// <param name="Message">Operator-facing result or failure detail.</param>
/// <param name="RollbackPath">Pre-restore rollback file when one was created.</param>
public sealed record SqliteMaintenanceResult(
    bool Succeeded,
    string Message,
    string? RollbackPath);
