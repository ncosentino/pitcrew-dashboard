using System.ComponentModel.DataAnnotations;

using NexusLabs.Needlr.Generators;

namespace PitCrew.Dashboard.Adapters.Sqlite;

/// <summary>
/// Configures the single-replica SQLite fleet projection.
/// </summary>
[Options("PitCrew:Sqlite", ValidateOnStart = true)]
public sealed class SqliteFleetStoreOptions
{
  /// <summary>
  /// Gets or sets the path to the SQLite database file.
  /// </summary>
  [Required]
  public string DatabasePath { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets how long each SQLite operation waits for a contended lock.
  /// </summary>
  [Range(1, 30_000)]
  public int BusyTimeoutMilliseconds { get; set; } = 1_500;

  /// <summary>
  /// Gets or sets the maximum attempts for retry-safe SQLite contention.
  /// </summary>
  [Range(1, 5)]
  public int ContentionMaximumAttempts { get; set; } = 3;

  /// <summary>
  /// Gets or sets the base delay between retry-safe contention attempts.
  /// </summary>
  [Range(0, 1_000)]
  public int ContentionRetryDelayMilliseconds { get; set; } = 100;
}
