using System.Globalization;

using Microsoft.Data.Sqlite;

namespace PitCrew.Dashboard.Adapters.Sqlite;

/// <summary>
/// Enforces one active operation of any supported type per node and profile.
/// </summary>
internal static class SqliteProfileOperationSlot
{
  public const string CapacityKind = "capacity";

  public const string RecoveryKind = "recovery";

  public static async Task<bool> AcquireAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      Guid nodeId,
      string profileId,
      string operationKind,
      Guid commandId,
      DateTimeOffset acquiredAt,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        INSERT INTO profile_active_operations (
            node_id,
            profile_id,
            operation_kind,
            command_id,
            acquired_at)
        VALUES (
            $nodeId,
            $profileId,
            $operationKind,
            $commandId,
            $acquiredAt)
        ON CONFLICT (node_id, profile_id) DO NOTHING;
        """;
    command.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
    command.Parameters.AddWithValue("$profileId", profileId);
    command.Parameters.AddWithValue("$operationKind", operationKind);
    command.Parameters.AddWithValue("$commandId", commandId.ToString("D"));
    command.Parameters.AddWithValue(
        "$acquiredAt",
        acquiredAt.ToString("O", CultureInfo.InvariantCulture));
    return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
  }

  public static async Task<bool> IsHeldAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      Guid nodeId,
      string profileId,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        SELECT 1
        FROM profile_active_operations
        WHERE node_id = $nodeId
          AND profile_id = $profileId
        LIMIT 1;
        """;
    command.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
    command.Parameters.AddWithValue("$profileId", profileId);
    return await command.ExecuteScalarAsync(cancellationToken) is not null;
  }

  public static async Task ReleaseCompletedAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      Guid nodeId,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        DELETE FROM profile_active_operations
        WHERE node_id = $nodeId
          AND NOT EXISTS (
              SELECT 1
              FROM capacity_commands
              WHERE capacity_commands.command_id =
                      profile_active_operations.command_id
                AND capacity_commands.status IN ('pending', 'delivered'))
          AND NOT EXISTS (
              SELECT 1
              FROM recovery_commands
              WHERE recovery_commands.command_id =
                      profile_active_operations.command_id
                AND recovery_commands.status IN (
                    'queued',
                    'claimed',
                    'started'));
        """;
    command.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
    await command.ExecuteNonQueryAsync(cancellationToken);
  }
}
