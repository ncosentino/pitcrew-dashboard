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
}
